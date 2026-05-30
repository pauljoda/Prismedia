using System.IO.Compression;
using System.Security.Cryptography;
using Prismedia.Infrastructure.StashCompat.Model;

namespace Prismedia.Infrastructure.StashCompat;

/// <summary>
/// A scraper listed in the CommunityScrapers stable index.
/// </summary>
/// <param name="Id">CommunityScrapers package id (e.g. <c>Algolia</c>).</param>
/// <param name="ProviderId">Synthesized Prismedia provider id (<c>stash-&lt;slug&gt;</c>).</param>
/// <param name="Name">Human-readable scraper name.</param>
/// <param name="Version">Index-reported version (a content hash).</param>
/// <param name="Path">Relative zip path within the index.</param>
/// <param name="Sha256">Expected archive checksum.</param>
/// <param name="Requires">Dependency package ids (e.g. <c>py_common</c>).</param>
public sealed record StashScraperIndexEntry(
    string Id,
    string ProviderId,
    string Name,
    string Version,
    string Path,
    string Sha256,
    IReadOnlyList<string> Requires);

/// <summary>
/// Fetches the Stash CommunityScrapers stable index and installs scrapers (plus their
/// declared dependencies such as <c>py_common</c>) into the scrapers cache directory, where
/// the catalog discovers them as identify providers. Archive checksums are verified before use.
/// </summary>
public sealed class StashScraperInstaller {
    private readonly HttpClient _http;
    private readonly string _scrapersRoot;
    private readonly string _indexUrl;

    /// <summary>
    /// Creates the installer.
    /// </summary>
    /// <param name="http">HTTP client used to fetch the index and archives.</param>
    /// <param name="scrapersRoot">Directory that holds installed scrapers (cache/scrapers).</param>
    /// <param name="indexUrl">Stable index URL (defaults to the official CommunityScrapers index).</param>
    public StashScraperInstaller(HttpClient http, string scrapersRoot, string? indexUrl) {
        _http = http;
        _scrapersRoot = scrapersRoot;
        _indexUrl = string.IsNullOrWhiteSpace(indexUrl)
            ? "https://stashapp.github.io/CommunityScrapers/stable/index.yml"
            : indexUrl;
    }

    /// <summary>
    /// Lists scrapers available in the remote index (dependency-only packages excluded).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Available index entries; empty when the index cannot be fetched.</returns>
    public async Task<IReadOnlyList<StashScraperIndexEntry>> ListAvailableAsync(CancellationToken cancellationToken) {
        var entries = await FetchIndexAsync(cancellationToken);
        return entries.Values
            .Where(entry => !IsDependencyOnly(entry.Id))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Installs the scraper whose synthesized provider id matches, plus its dependencies.
    /// </summary>
    /// <param name="providerId">Synthesized provider id (<c>stash-&lt;slug&gt;</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the scraper was downloaded and extracted.</returns>
    public async Task<bool> InstallAsync(string providerId, CancellationToken cancellationToken) {
        var entries = await FetchIndexAsync(cancellationToken);
        var entry = entries.Values.FirstOrDefault(candidate =>
            candidate.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
        if (entry is null) {
            return false;
        }

        Directory.CreateDirectory(_scrapersRoot);
        await InstallEntryAsync(entry, Path.Combine(_scrapersRoot, SafeSegment(entry.Id)), cancellationToken);

        // Dependencies (py_common, shared API helpers) extract directly under the scrapers root so
        // they resolve on PYTHONPATH (which is set to the parent of each scraper directory).
        var installedDeps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await InstallDependenciesAsync(entry, entries, installedDeps, cancellationToken);
        return true;
    }

    private async Task InstallDependenciesAsync(
        StashScraperIndexEntry entry,
        IReadOnlyDictionary<string, StashScraperIndexEntry> entries,
        HashSet<string> installed,
        CancellationToken cancellationToken) {
        foreach (var dependencyId in entry.Requires) {
            if (!installed.Add(dependencyId) || !entries.TryGetValue(dependencyId, out var dependency)) {
                continue;
            }

            await InstallEntryAsync(dependency, Path.Combine(_scrapersRoot, SafeSegment(dependency.Id)), cancellationToken);
            await InstallDependenciesAsync(dependency, entries, installed, cancellationToken);
        }
    }

    private async Task InstallEntryAsync(StashScraperIndexEntry entry, string destination, CancellationToken cancellationToken) {
        var archive = await DownloadArchiveAsync(entry, cancellationToken);
        try {
            if (Directory.Exists(destination)) {
                Directory.Delete(destination, recursive: true);
            }

            Directory.CreateDirectory(destination);
            ExtractZip(archive, destination);
        } finally {
            if (File.Exists(archive)) {
                File.Delete(archive);
            }
        }
    }

    private async Task<string> DownloadArchiveAsync(StashScraperIndexEntry entry, CancellationToken cancellationToken) {
        var url = new Uri(new Uri(_indexUrl), entry.Path).ToString();
        var path = Path.Combine(Path.GetTempPath(), $"stash-{SafeSegment(entry.Id)}-{Guid.NewGuid():N}.zip");

        await using (var remote = await _http.GetStreamAsync(url, cancellationToken))
        await using (var local = File.Create(path)) {
            await remote.CopyToAsync(local, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(entry.Sha256)) {
            await using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            if (!hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)) {
                File.Delete(path);
                throw new InvalidOperationException($"Stash scraper archive checksum mismatch for '{entry.Id}'.");
            }
        }

        return path;
    }

    private async Task<IReadOnlyDictionary<string, StashScraperIndexEntry>> FetchIndexAsync(CancellationToken cancellationToken) {
        try {
            var body = await _http.GetStringAsync(_indexUrl, cancellationToken);
            return ParseIndex(body);
        } catch (HttpRequestException) {
            return new Dictionary<string, StashScraperIndexEntry>();
        } catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return new Dictionary<string, StashScraperIndexEntry>();
        }
    }

    /// <summary>
    /// Parses the CommunityScrapers index YAML into entries keyed by package id.
    /// </summary>
    public static IReadOnlyDictionary<string, StashScraperIndexEntry> ParseIndex(string yaml) {
        var root = StashYamlNode.Parse(yaml);
        var entries = new Dictionary<string, StashScraperIndexEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in root.Items()) {
            var id = item.StringAt("id");
            var path = item.StringAt("path");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(path)) {
                continue;
            }

            var requires = item["requires"].Items()
                .Select(node => node.Scalar)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .ToArray();

            entries[id] = new StashScraperIndexEntry(
                id,
                StashScraperManifestFactory.ProviderIdFor(id),
                item.StringAt("name") ?? id,
                item.StringAt("version") ?? string.Empty,
                path,
                item.StringAt("sha256") ?? string.Empty,
                requires);
        }

        return entries;
    }

    private static bool IsDependencyOnly(string id) =>
        id.Equals("py_common", StringComparison.OrdinalIgnoreCase) ||
        id.EndsWith("API", StringComparison.Ordinal);

    private static void ExtractZip(string archivePath, string destination) {
        using var archive = ZipFile.OpenRead(archivePath);
        var root = Path.GetFullPath(destination);
        foreach (var entry in archive.Entries) {
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.Equals(root, StringComparison.Ordinal) &&
                !target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) {
                throw new InvalidOperationException("Stash scraper archive contains an unsafe path.");
            }

            if (string.IsNullOrEmpty(entry.Name)) {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static string SafeSegment(string value) =>
        new string(value.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '-').ToArray())
            .Trim('-', '.');
}
