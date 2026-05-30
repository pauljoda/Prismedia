using System.Text.Json;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.StashCompat;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Runtime configuration for community plugin discovery and execution.
/// </summary>
/// <param name="DevPaths">Local plugin repository or plugin directories to scan during development.</param>
/// <param name="CacheRoot">Cache directory used for transient plugin request envelopes.</param>
/// <param name="CurrentPrismediaVersion">Current Prismedia version used for compatibility gating.</param>
/// <param name="CommunityIndexUrl">Optional URL for the Prismedia community plugin index.</param>
public sealed record PluginCatalogOptions(
    IReadOnlyList<string> DevPaths,
    string CacheRoot,
    string CurrentPrismediaVersion,
    string? CommunityIndexUrl = null,
    string? StashScraperIndexUrl = null);

/// <summary>
/// Resolved local plugin artifact ready to execute.
/// </summary>
/// <param name="Manifest">Plugin manifest declared by the plugin.</param>
/// <param name="ManifestPath">Absolute manifest file path.</param>
/// <param name="WorkingDirectory">Plugin directory used as the process working context.</param>
/// <param name="EntryPath">Absolute entry assembly or executable path.</param>
public sealed record PluginDescriptor(
    PluginManifest Manifest,
    string ManifestPath,
    string WorkingDirectory,
    string EntryPath);

/// <summary>
/// Discovers plugin manifests, applies compatibility gates, and stores installed provider state.
/// </summary>
public sealed class PluginCatalogService : IPluginCatalogService {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly PrismediaDbContext _db;
    private readonly PluginCatalogOptions _options;
    private readonly HttpClient _http;

    public PluginCatalogService(PrismediaDbContext db, PluginCatalogOptions options, HttpClient? http = null) {
        _db = db;
        _options = options;
        _http = http ?? new HttpClient();
    }

    /// <summary>
    /// Lists locally discoverable providers and overlays installed/auth state from the database.
    /// </summary>
    public async Task<IReadOnlyList<PluginProvider>> ListProvidersAsync(CancellationToken cancellationToken) {
        var current = ParseVersion(_options.CurrentPrismediaVersion);
        var descriptors = await DiscoverAsync(cancellationToken);
        var indexed = await ListRemoteIndexEntriesAsync(current, cancellationToken);
        var indexedById = indexed.ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);
        var configs = await _db.ProviderConfigs
            .AsNoTracking()
            .ToDictionaryAsync(row => row.ProviderCode, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var credentialRows = await _db.ProviderCredentials
            .AsNoTracking()
            .Join(
                _db.ProviderConfigs.AsNoTracking(),
                credential => credential.ProviderConfigId,
                config => config.Id,
                (credential, config) => new { config.ProviderCode, credential.CredentialKey, credential.EncryptedValue })
            .Where(row => row.EncryptedValue != string.Empty)
            .ToArrayAsync(cancellationToken);
        var credentialKeys = credentialRows
            .GroupBy(row => row.ProviderCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => row.CredentialKey).ToHashSet(StringComparer.Ordinal),
                StringComparer.OrdinalIgnoreCase);

        var providers = descriptors
            .Select(descriptor => {
                indexedById.TryGetValue(descriptor.Manifest.Id, out var remote);
                return ToProvider(descriptor, configs, credentialKeys, remote);
            })
            .ToList();
        var localIds = providers.Select(provider => provider.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        providers.AddRange(indexed
            .Where(entry => !localIds.Contains(entry.Id))
            .Select(entry => {
                configs.TryGetValue(entry.Id, out var config);
                return new PluginProvider(
                    entry.Id,
                    entry.Name,
                    entry.Version,
                    Installed: config is not null,
                    Enabled: config?.Enabled ?? false,
                    IsNsfw: entry.IsNsfw || config?.IsNsfw == true,
                    entry.Supports,
                    Auth: [],
                    MissingAuthKeys: []);
            }));

        return providers
            .OrderBy(provider => provider.Name)
            .ToArray();
    }

    /// <summary>
    /// Finds the latest compatible local provider artifact for an identify request.
    /// </summary>
    public async Task<PluginDescriptor?> FindProviderAsync(
        string providerId,
        string? entityKind,
        CancellationToken cancellationToken) {
        var descriptors = await DiscoverAsync(cancellationToken);
        return descriptors
            .Where(descriptor => descriptor.Manifest.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase))
            .Where(descriptor => entityKind is null || descriptor.Manifest.Supports.Any(support =>
                support.EntityKind.Equals(entityKind, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(descriptor => ParseVersion(descriptor.Manifest.Version))
            .FirstOrDefault();
    }

    /// <summary>
    /// Saves a provider config row so the plugin is visible as installed/enabled.
    /// </summary>
    public async Task<PluginProvider?> InstallAsync(string providerId, CancellationToken cancellationToken) {
        var descriptor = await FindProviderAsync(providerId, null, cancellationToken);
        if (descriptor is null && providerId.StartsWith("stash-", StringComparison.OrdinalIgnoreCase)) {
            var installer = new StashScraperInstaller(_http, StashScrapersRoot(), _options.StashScraperIndexUrl);
            if (await installer.InstallAsync(providerId, cancellationToken)) {
                descriptor = await FindProviderAsync(providerId, null, cancellationToken);
            }
        }

        descriptor ??= await PullProviderAsync(providerId, cancellationToken);
        if (descriptor is null) {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var config = await _db.ProviderConfigs
            .FirstOrDefaultAsync(row => row.ProviderCode == providerId, cancellationToken);
        if (config is null) {
            config = new ProviderConfigRow {
                Id = Guid.NewGuid(),
                ProviderCode = descriptor.Manifest.Id,
                CreatedAt = now
            };
            _db.ProviderConfigs.Add(config);
        }

        config.DisplayName = descriptor.Manifest.Name;
        config.ProviderType = descriptor.Manifest.Runtime.Equals("stash-compat", StringComparison.OrdinalIgnoreCase)
            ? ProviderType.StashCompat
            : ProviderType.ExternalProcess;
        config.SettingsJson = JsonSerializer.Serialize(new InstalledPluginSettings(
            descriptor.Manifest.Version,
            descriptor.ManifestPath,
            descriptor.EntryPath), JsonOptions);
        config.Enabled = true;
        config.IsNsfw = descriptor.Manifest.IsNsfw;
        config.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        return (await ListProvidersAsync(cancellationToken))
            .FirstOrDefault(provider => provider.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Pulls the newest compatible remote artifact for an installed provider and re-points its config to it.
    /// </summary>
    public async Task<PluginProvider?> UpdateAsync(string providerId, CancellationToken cancellationToken) {
        var current = ParseVersion(_options.CurrentPrismediaVersion);
        var remote = PluginCompatibilityResolver.LatestCompatible(
            await FetchRemoteIndexAsync(cancellationToken),
            providerId,
            current);
        if (remote is null) {
            return null;
        }

        var currentDescriptor = await FindProviderAsync(providerId, null, cancellationToken);
        if (currentDescriptor is not null &&
            ParseVersion(currentDescriptor.Manifest.Version) >= ParseVersion(remote.Version)) {
            return (await ListProvidersAsync(cancellationToken))
                .FirstOrDefault(provider => provider.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase));
        }

        var updated = await PullProviderAsync(providerId, cancellationToken, remote);
        return updated is null
            ? null
            : await InstallAsync(providerId, cancellationToken);
    }

    /// <summary>
    /// Removes installed provider state while leaving local plugin files untouched.
    /// </summary>
    public async Task<bool> RemoveAsync(string providerId, CancellationToken cancellationToken) {
        var config = await _db.ProviderConfigs
            .FirstOrDefaultAsync(row => row.ProviderCode == providerId, cancellationToken);
        if (config is null) {
            return false;
        }

        _db.ProviderConfigs.Remove(config);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Stores credential values for a provider in the existing provider credential table.
    /// </summary>
    public async Task<bool> SaveAuthAsync(
        string providerId,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken cancellationToken) {
        var provider = await InstallAsync(providerId, cancellationToken);
        if (provider is null) {
            return false;
        }

        var config = await _db.ProviderConfigs
            .SingleAsync(row => row.ProviderCode == providerId, cancellationToken);
        var existing = await _db.ProviderCredentials
            .Where(row => row.ProviderConfigId == config.Id)
            .ToDictionaryAsync(row => row.CredentialKey, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        foreach (var (key, value) in values) {
            if (string.IsNullOrWhiteSpace(value)) {
                if (existing.TryGetValue(key, out var removed)) {
                    _db.ProviderCredentials.Remove(removed);
                }

                continue;
            }

            if (!existing.TryGetValue(key, out var credential)) {
                credential = new ProviderCredentialRow {
                    Id = Guid.NewGuid(),
                    ProviderConfigId = config.Id,
                    CredentialKey = key,
                    CreatedAt = now
                };
                _db.ProviderCredentials.Add(credential);
            }

            credential.EncryptedValue = value;
            credential.UpdatedAt = now;
        }

        config.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Reads credentials from provider storage plus environment overrides.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetAuthAsync(
        PluginManifest manifest,
        CancellationToken cancellationToken) {
        var config = await _db.ProviderConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.ProviderCode == manifest.Id && row.Enabled, cancellationToken);
        var stored = config is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : await _db.ProviderCredentials
                .AsNoTracking()
                .Where(row => row.ProviderConfigId == config.Id)
                .ToDictionaryAsync(row => row.CredentialKey, row => row.EncryptedValue, StringComparer.Ordinal, cancellationToken);

        foreach (var field in manifest.Auth) {
            var value = PluginCredentialResolver.ResolveEnvironmentCredential(manifest.Id, field.Key);
            if (!string.IsNullOrWhiteSpace(value)) {
                stored[field.Key] = value;
            }
        }

        return stored;
    }

    private async Task<IReadOnlyList<PluginDescriptor>> DiscoverAsync(CancellationToken cancellationToken) {
        var current = ParseVersion(_options.CurrentPrismediaVersion);
        var descriptors = new List<PluginDescriptor>();

        foreach (var root in EnumerateDiscoveryRoots().Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase)) {
            foreach (var manifestPath in EnumerateManifestPaths(root)) {
                cancellationToken.ThrowIfCancellationRequested();
                var manifest = await ReadManifestAsync(manifestPath, cancellationToken);
                if (manifest is null || !IsCompatible(manifest, current)) {
                    continue;
                }

                var directory = Path.GetDirectoryName(manifestPath) ?? root;
                var entryPath = Path.GetFullPath(Path.IsPathRooted(manifest.Entry)
                    ? manifest.Entry
                    : Path.Combine(directory, manifest.Entry));
                descriptors.Add(new PluginDescriptor(manifest, manifestPath, directory, entryPath));
            }
        }

        descriptors.AddRange(await DiscoverStashScrapersAsync(cancellationToken));

        return descriptors
            .GroupBy(descriptor => descriptor.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(descriptor => ParseVersion(descriptor.Manifest.Version)).First())
            .ToArray();
    }

    /// <summary>
    /// Discovers installed Stash community scraper YAML files and synthesizes a provider
    /// manifest for each. Stash scrapers carry no Prismedia manifest, so capabilities and
    /// supported kinds are derived from the YAML's declared actions.
    /// </summary>
    private async Task<IReadOnlyList<PluginDescriptor>> DiscoverStashScrapersAsync(CancellationToken cancellationToken) {
        var descriptors = new List<PluginDescriptor>();
        foreach (var root in EnumerateStashScraperRoots().Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase)) {
            foreach (var yamlPath in EnumerateStashScraperPaths(root)) {
                cancellationToken.ThrowIfCancellationRequested();
                string yaml;
                try {
                    yaml = await File.ReadAllTextAsync(yamlPath, cancellationToken);
                } catch (IOException) {
                    continue;
                }

                var manifest = StashScraperManifestFactory.TryCreate(yaml, yamlPath);
                if (manifest is null) {
                    continue;
                }

                var directory = Path.GetDirectoryName(yamlPath) ?? root;
                descriptors.Add(new PluginDescriptor(manifest, yamlPath, directory, yamlPath));
            }
        }

        return descriptors;
    }

    private IEnumerable<string> EnumerateStashScraperRoots() {
        foreach (var path in _options.DevPaths) {
            yield return path;
        }

        yield return StashScrapersRoot();
    }

    private string StashScrapersRoot() => Path.Combine(_options.CacheRoot, "scrapers");

    /// <summary>
    /// Lists Stash community scrapers available in the remote CommunityScrapers index for install.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Available index entries; empty when the index cannot be fetched.</returns>
    public async Task<IReadOnlyList<StashScraperListing>> ListStashScrapersAsync(CancellationToken cancellationToken) {
        var installer = new StashScraperInstaller(_http, StashScrapersRoot(), _options.StashScraperIndexUrl);
        var entries = await installer.ListAvailableAsync(cancellationToken);
        return entries
            .Select(entry => new StashScraperListing(entry.ProviderId, entry.Name, entry.Version))
            .ToArray();
    }

    private static IEnumerable<string> EnumerateStashScraperPaths(string root) =>
        Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase));

    private PluginProvider ToProvider(
        PluginDescriptor descriptor,
        IReadOnlyDictionary<string, ProviderConfigRow> configs,
        IReadOnlyDictionary<string, HashSet<string>> credentialKeys,
        PluginIndexEntry? remote) {
        configs.TryGetValue(descriptor.Manifest.Id, out var config);
        credentialKeys.TryGetValue(descriptor.Manifest.Id, out var keys);
        keys ??= [];
        var missing = descriptor.Manifest.Auth
            .Where(field => field.Required &&
                !PluginCredentialResolver.HasCredentialForField(keys, field.Key) &&
                !PluginCredentialResolver.HasEnvironmentCredential(descriptor.Manifest.Id, field.Key))
            .Select(field => field.Key)
            .ToArray();
        var updateAvailable = config is not null &&
            remote is not null &&
            ParseVersion(remote.Version) > ParseVersion(descriptor.Manifest.Version);

        return new PluginProvider(
            descriptor.Manifest.Id,
            descriptor.Manifest.Name,
            descriptor.Manifest.Version,
            Installed: config is not null,
            Enabled: config?.Enabled ?? false,
            IsNsfw: descriptor.Manifest.IsNsfw || config?.IsNsfw == true,
            descriptor.Manifest.Supports,
            descriptor.Manifest.Auth,
            missing,
            UpdateAvailable: updateAvailable,
            AvailableVersion: updateAvailable ? remote?.Version : null);
    }

    private IEnumerable<string> EnumerateDiscoveryRoots() {
        foreach (var path in _options.DevPaths) {
            yield return path;
        }

        yield return CommunityPluginRoot();
    }

    private async Task<IReadOnlyList<PluginIndexEntry>> ListRemoteIndexEntriesAsync(
        Version current,
        CancellationToken cancellationToken) {
        var entries = await FetchRemoteIndexAsync(cancellationToken);
        return entries
            .Where(entry => PluginCompatibilityResolver.IsCompatible(entry, current))
            .GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(entry => ParseVersion(entry.Version)).First())
            .ToArray();
    }

    private async Task<PluginDescriptor?> PullProviderAsync(
        string providerId,
        CancellationToken cancellationToken,
        PluginIndexEntry? requestedEntry = null) {
        if (string.IsNullOrWhiteSpace(_options.CommunityIndexUrl)) {
            return null;
        }

        var current = ParseVersion(_options.CurrentPrismediaVersion);
        var entry = requestedEntry ?? PluginCompatibilityResolver.LatestCompatible(
                await FetchRemoteIndexAsync(cancellationToken),
                providerId,
                current);
        if (entry is null) {
            return null;
        }

        var destination = Path.Combine(CommunityPluginRoot(), SafePathSegment(entry.Id), SafePathSegment(entry.Version));
        if (!Directory.Exists(destination) || !EnumerateManifestPaths(destination).Any()) {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var artifact = await DownloadArtifactAsync(entry, cancellationToken);
            var tempDirectory = Path.Combine(CommunityPluginRoot(), $".tmp-{Guid.NewGuid():N}");
            try {
                Directory.CreateDirectory(tempDirectory);
                ExtractArtifact(artifact, tempDirectory);
                if (Directory.Exists(destination)) {
                    Directory.Delete(destination, recursive: true);
                }

                Directory.Move(tempDirectory, destination);
            } finally {
                if (File.Exists(artifact)) {
                    File.Delete(artifact);
                }

                if (Directory.Exists(tempDirectory)) {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        var descriptors = await DiscoverAsync(cancellationToken);
        return descriptors
            .Where(descriptor => descriptor.Manifest.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(descriptor => ParseVersion(descriptor.Manifest.Version))
            .FirstOrDefault();
    }

    private async Task<string> DownloadArtifactAsync(PluginIndexEntry entry, CancellationToken cancellationToken) {
        var url = ResolveEntryUrl(entry.Path);
        var artifacts = Path.Combine(CommunityPluginRoot(), "artifacts");
        Directory.CreateDirectory(artifacts);
        var extension = ArtifactExtension(new Uri(url).AbsolutePath);
        var path = Path.Combine(artifacts, $"{SafePathSegment(entry.Id)}-{SafePathSegment(entry.Version)}{extension}");

        await using (var remote = await _http.GetStreamAsync(url, cancellationToken))
        await using (var local = File.Create(path)) {
            await remote.CopyToAsync(local, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(entry.Sha256)) {
            await using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            if (!hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)) {
                File.Delete(path);
                throw new InvalidOperationException($"Plugin artifact checksum mismatch for '{entry.Id}'.");
            }
        }

        return path;
    }

    private async Task<IReadOnlyList<PluginIndexEntry>> FetchRemoteIndexAsync(CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(_options.CommunityIndexUrl)) {
            return [];
        }

        try {
            var indexUrl = ResolveIndexUrl(_options.CommunityIndexUrl);
            using var request = new HttpRequestMessage(HttpMethod.Get, AddCacheBuster(indexUrl));
            request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
            request.Headers.TryAddWithoutValidation("Pragma", "no-cache");
            using var response = await _http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return PluginIndexParser.Parse(body, indexUrl);
        } catch (HttpRequestException) {
            return [];
        } catch (JsonException) {
            return [];
        } catch (FormatException) {
            return [];
        } catch (IOException) {
            return [];
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return [];
        }
    }

    private static string ResolveIndexUrl(string configured) {
        if (configured.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
            configured.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
            configured.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)) {
            return configured;
        }

        return configured.TrimEnd('/') + "/index.yml";
    }

    private static string AddCacheBuster(string url) {
        var separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{url}{separator}_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
    }

    private string ResolveEntryUrl(string path) {
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute)) {
            return absolute.ToString();
        }

        var indexUrl = ResolveIndexUrl(_options.CommunityIndexUrl ?? string.Empty);
        return new Uri(new Uri(indexUrl), path).ToString();
    }

    private static void ExtractArtifact(string artifact, string destination) {
        if (artifact.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) {
            ExtractZip(artifact, destination);
            return;
        }

        if (artifact.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
            artifact.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)) {
            using var file = File.OpenRead(artifact);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            ExtractTar(gzip, destination);
            return;
        }

        if (artifact.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) {
            using var file = File.OpenRead(artifact);
            ExtractTar(file, destination);
            return;
        }

        throw new InvalidOperationException("Only .zip, .tar, .tar.gz, and .tgz plugin artifacts are supported.");
    }

    private static void ExtractZip(string artifact, string destination) {
        using var archive = ZipFile.OpenRead(artifact);
        foreach (var entry in archive.Entries) {
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!IsSafeExtractionPath(destination, target)) {
                throw new InvalidOperationException("Plugin artifact contains an unsafe path.");
            }

            if (string.IsNullOrEmpty(entry.Name)) {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static void ExtractTar(Stream stream, string destination) {
        using var archive = new TarReader(stream);
        while (archive.GetNextEntry() is { } entry) {
            var target = Path.GetFullPath(Path.Combine(destination, entry.Name));
            if (!IsSafeExtractionPath(destination, target)) {
                throw new InvalidOperationException("Plugin artifact contains an unsafe path.");
            }

            if (entry.EntryType == TarEntryType.Directory) {
                Directory.CreateDirectory(target);
                continue;
            }

            if (entry.DataStream is null) {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var output = File.Create(target);
            entry.DataStream.CopyTo(output);
        }
    }

    private string CommunityPluginRoot() =>
        Path.Combine(_options.CacheRoot, "plugins", "community");

    private static string SafePathSegment(string value) {
        var chars = value.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' or '.'
                ? character
                : '-').ToArray();
        return new string(chars).Trim('-', '.');
    }

    private static bool IsSafeExtractionPath(string destination, string target) {
        var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return target.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string ArtifactExtension(string urlPath) {
        if (urlPath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) {
            return ".tar.gz";
        }

        if (urlPath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)) {
            return ".tgz";
        }

        return Path.GetExtension(urlPath);
    }

    private static IEnumerable<string> EnumerateManifestPaths(string root) {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "manifest.json",
            "plugin.json"
        };

        return Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Where(path => names.Contains(Path.GetFileName(path)));
    }

    private static async Task<PluginManifest?> ReadManifestAsync(string path, CancellationToken cancellationToken) {
        try {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<PluginManifest>(stream, JsonOptions, cancellationToken);
        } catch (JsonException) {
            return null;
        } catch (IOException) {
            return null;
        }
    }

    private static bool IsCompatible(PluginManifest manifest, Version current) {
        if (manifest.ManifestVersion != 1 ||
            !manifest.Runtime.Equals("dotnet-process", StringComparison.OrdinalIgnoreCase) ||
            !manifest.ApiTags.Contains("prismedia", StringComparer.OrdinalIgnoreCase)) {
            return false;
        }

        var min = ParseVersion(manifest.Compat.PrismediaMin);
        var max = string.IsNullOrWhiteSpace(manifest.Compat.PrismediaMax)
            ? null
            : ParseVersion(manifest.Compat.PrismediaMax);
        return current >= min && (max is null || current <= max);
    }

    private static Version ParseVersion(string version) {
        var normalized = version.Split('-', 2)[0];
        return Version.TryParse(normalized, out var parsed) ? parsed : new Version(0, 0, 0);
    }

    private sealed record InstalledPluginSettings(string Version, string ManifestPath, string EntryPath);
}
