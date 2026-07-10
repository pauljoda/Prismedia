using System.Text.Json;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Files;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.StashCompat;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Memo for the community plugin index. The index changes rarely but is consulted by user-facing
/// reads (the plugin browser), so a successful fetch is reused for a generous window and a failed one
/// for a short backoff — a GitHub hiccup neither stalls nor hammers anything. Registered as a
/// singleton so scoped catalog instances share one fetch; a catalog constructed without one (tests)
/// gets a private instance and full isolation.
/// </summary>
public sealed class PluginIndexCache {
    private static readonly TimeSpan SuccessTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FailureTtl = TimeSpan.FromMinutes(2);

    /// <summary>Single-flight gate so concurrent cache misses produce one fetch.</summary>
    internal SemaphoreSlim Gate { get; } = new(1, 1);

    private (string Url, DateTimeOffset FetchedAt, IReadOnlyList<PluginIndexEntry> Entries, bool Success)? _entry;

    /// <summary>The cached entries for the url, or null when absent, expired, or for a different url.</summary>
    internal IReadOnlyList<PluginIndexEntry>? Cached(string indexUrl) {
        if (_entry is not { } cache || !cache.Url.Equals(indexUrl, StringComparison.Ordinal)) {
            return null;
        }

        var ttl = cache.Success ? SuccessTtl : FailureTtl;
        return DateTimeOffset.UtcNow - cache.FetchedAt < ttl ? cache.Entries : null;
    }

    /// <summary>Records a fetch outcome (success or failure) for the url.</summary>
    internal void Store(string indexUrl, IReadOnlyList<PluginIndexEntry> entries, bool success) =>
        _entry = (indexUrl, DateTimeOffset.UtcNow, entries, success);
}

/// <summary>
/// Remote index fetch, artifact download/extraction, and manifest parsing helpers for
/// <see cref="PluginCatalogService"/>.
/// </summary>
public sealed partial class PluginCatalogService {
    private async Task<IReadOnlyList<PluginIndexEntry>> ListRemoteIndexEntriesAsync(
        Version current,
        CancellationToken cancellationToken) {
        var entries = await FetchRemoteIndexAsync(cancellationToken);
        return entries
            .Where(entry => PluginCompatibilityResolver.IsCompatible(entry, current))
            .Select(PluginManifestContract.Normalize)
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

        var indexUrl = ResolveIndexUrl(_options.CommunityIndexUrl);
        if (_indexCache.Cached(indexUrl) is { } cached) {
            return cached;
        }

        await _indexCache.Gate.WaitAsync(cancellationToken);
        try {
            if (_indexCache.Cached(indexUrl) is { } refreshed) {
                return refreshed; // another caller fetched while we waited
            }

            var entries = await FetchRemoteIndexCoreAsync(indexUrl, cancellationToken);
            _indexCache.Store(indexUrl, entries.Entries, entries.Success);
            return entries.Entries;
        } finally {
            _indexCache.Gate.Release();
        }
    }

    private async Task<(IReadOnlyList<PluginIndexEntry> Entries, bool Success)> FetchRemoteIndexCoreAsync(
        string indexUrl, CancellationToken cancellationToken) {
        try {
            using var response = await _http.GetAsync(indexUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return (PluginIndexParser.Parse(body, indexUrl), Success: true);
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return ([], Success: false); // HttpClient timeout, not caller cancellation
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return ([], Success: false);
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

    internal static bool IsSafeExtractionPath(string destination, string target) {
        return FileSystemPathComparison.IsSameOrDescendant(destination, target);
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
        } catch (ArgumentOutOfRangeException) {
            // A code-bearing enum in an untrusted manifest used an unknown wire value.
            return null;
        } catch (IOException) {
            return null;
        }
    }

    private static bool IsCompatible(PluginManifest manifest, Version current) =>
        PluginCompatibilityResolver.IsCompatible(manifest, current);

    private static Version ParseVersion(string version) {
        var normalized = version.Split('-', 2)[0];
        return Version.TryParse(normalized, out var parsed) ? parsed : new Version(0, 0, 0);
    }

    private sealed record InstalledPluginSettings(string Version, string ManifestPath, string EntryPath);
}
