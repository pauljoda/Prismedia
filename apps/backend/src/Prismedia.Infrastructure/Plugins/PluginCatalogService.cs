using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Runtime configuration for community plugin discovery and execution.
/// </summary>
/// <param name="DevPaths">Local plugin repository or plugin directories to scan during development.</param>
/// <param name="CacheRoot">Cache directory used for transient plugin request envelopes.</param>
/// <param name="CurrentPrismediaVersion">Current Prismedia version used for compatibility gating.</param>
public sealed record PluginCatalogOptions(
    IReadOnlyList<string> DevPaths,
    string CacheRoot,
    string CurrentPrismediaVersion);

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

    public PluginCatalogService(PrismediaDbContext db, PluginCatalogOptions options) {
        _db = db;
        _options = options;
    }

    /// <summary>
    /// Lists locally discoverable providers and overlays installed/auth state from the database.
    /// </summary>
    public async Task<IReadOnlyList<PluginProvider>> ListProvidersAsync(CancellationToken cancellationToken) {
        var descriptors = await DiscoverAsync(cancellationToken);
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
                group => group.Select(row => row.CredentialKey).ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        return descriptors
            .Select(descriptor => {
                configs.TryGetValue(descriptor.Manifest.Id, out var config);
                credentialKeys.TryGetValue(descriptor.Manifest.Id, out var keys);
                keys ??= [];
                var missing = descriptor.Manifest.Auth
                    .Where(field => field.Required &&
                        !PluginCredentialResolver.HasCredentialForField(keys, descriptor.Manifest.Id, field.Key) &&
                        !PluginCredentialResolver.HasEnvironmentCredential(descriptor.Manifest.Id, field.Key))
                    .Select(field => field.Key)
                    .ToArray();

                return new PluginProvider(
                    descriptor.Manifest.Id,
                    descriptor.Manifest.Name,
                    descriptor.Manifest.Version,
                    Installed: config is not null,
                    Enabled: config?.Enabled ?? false,
                    IsNsfw: descriptor.Manifest.IsNsfw || config?.IsNsfw == true,
                    descriptor.Manifest.Supports,
                    descriptor.Manifest.Auth,
                    missing);
            })
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
        config.ProviderType = ProviderType.ExternalProcess;
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
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : await _db.ProviderCredentials
                .AsNoTracking()
                .Where(row => row.ProviderConfigId == config.Id)
                .ToDictionaryAsync(row => row.CredentialKey, row => row.EncryptedValue, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var field in manifest.Auth) {
            if (!stored.ContainsKey(field.Key) &&
                PluginCredentialResolver.TryResolveStoredCredential(stored, manifest.Id, field.Key, out var aliasedValue)) {
                stored[field.Key] = aliasedValue;
            }

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

        foreach (var root in _options.DevPaths.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase)) {
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

        return descriptors
            .GroupBy(descriptor => descriptor.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(descriptor => ParseVersion(descriptor.Manifest.Version)).First())
            .ToArray();
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
