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
using Prismedia.Infrastructure.Serialization;
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
public sealed partial class PluginCatalogService : IPluginCatalogService {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        Converters = { new CodecJsonConverterFactory() }
    };

    private readonly PrismediaDbContext _db;
    private readonly PluginCatalogOptions _options;
    private readonly HttpClient _http;
    private readonly PluginIndexCache _indexCache;

    public PluginCatalogService(
        PrismediaDbContext db, PluginCatalogOptions options, HttpClient? http = null, PluginIndexCache? indexCache = null) {
        _db = db;
        _options = options;
        // A bounded timeout: the remote index and artifact downloads ride this client, and a hung
        // remote must never stall a caller for the framework's default 100 seconds.
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _indexCache = indexCache ?? new PluginIndexCache();
    }

    /// <summary>
    /// Lists locally discoverable providers and overlays installed/auth state from the database,
    /// including not-yet-installed entries from the remote community index (for the plugin browser).
    /// </summary>
    public async Task<IReadOnlyList<PluginProvider>> ListProvidersAsync(CancellationToken cancellationToken) =>
        await ListProvidersCoreAsync(includeRemote: true, cancellationToken);

    /// <summary>
    /// Lists locally discoverable providers only — no remote community-index round-trip. Identify,
    /// eligibility, and request reads resolve against installed plugins, so this is their listing.
    /// </summary>
    public async Task<IReadOnlyList<PluginProvider>> ListInstalledProvidersAsync(CancellationToken cancellationToken) =>
        await ListProvidersCoreAsync(includeRemote: false, cancellationToken);

    private async Task<IReadOnlyList<PluginProvider>> ListProvidersCoreAsync(bool includeRemote, CancellationToken cancellationToken) {
        var current = ParseVersion(_options.CurrentPrismediaVersion);
        var descriptors = await DiscoverAsync(cancellationToken);
        var indexed = includeRemote ? await ListRemoteIndexEntriesAsync(current, cancellationToken) : [];
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
                PluginEntityKindCompatibility.SupportsKind(support, entityKind)))
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

        return (await ListInstalledProvidersAsync(cancellationToken))
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
            return (await ListInstalledProvidersAsync(cancellationToken))
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
                var discoveredManifest = await ReadManifestAsync(manifestPath, cancellationToken);
                if (discoveredManifest is null || !IsCompatible(discoveredManifest, current)) {
                    continue;
                }

                var manifest = PluginManifestContract.Normalize(discoveredManifest);

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

                var discoveredManifest = StashScraperManifestFactory.TryCreate(yaml, yamlPath);
                if (discoveredManifest is null) {
                    continue;
                }

                var manifest = PluginManifestContract.Normalize(discoveredManifest);

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

}
