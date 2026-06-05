using Microsoft.Extensions.Logging;
using Prismedia.Application.Videos;

namespace Prismedia.Infrastructure.Videos;

/// <summary>
/// Manages the on-disk transcode/remux cache: reports its size, clears it, and evicts the
/// least-recently-played cached items when it grows past a configured limit, so generated HLS output
/// cannot silently fill the host disk.
/// </summary>
/// <remarks>
/// The cache lives under <see cref="HlsAssetServiceOptions.CacheRoot"/> in three sibling roots —
/// <c>hlsv</c> (adaptive transcode + remux), <c>hls</c>, and <c>hls2</c> (legacy packages) — each
/// holding one subdirectory per media item, named by the item's GUID. Eviction is non-destructive:
/// removing a cached package only forces the next play of that item to regenerate its segments from
/// the untouched source file.
/// </remarks>
public sealed class TranscodeCacheService : ITranscodeCacheService {
    // The cache roots, relative to the cache directory, that hold generated HLS/transcode output.
    private static readonly string[] CacheRootNames = ["hlsv", "hls", "hls2"];

    private readonly HlsAssetServiceOptions _options;
    private readonly ILogger<TranscodeCacheService>? _logger;

    /// <summary>Creates the cache manager over the configured HLS cache root.</summary>
    /// <param name="options">HLS options carrying the cache root directory.</param>
    /// <param name="logger">Optional logger for eviction diagnostics.</param>
    public TranscodeCacheService(HlsAssetServiceOptions options, ILogger<TranscodeCacheService>? logger = null) {
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public long ComputeSizeBytes() {
        long total = 0;
        foreach (var root in CacheRoots()) {
            total += DirectorySizeBytes(root);
        }

        return total;
    }

    /// <inheritdoc />
    public long Clear() {
        HlsAssetService.CancelAllActiveGenerations();
        var freed = ComputeSizeBytes();
        foreach (var root in CacheRoots()) {
            TryDeleteDirectoryContents(root);
        }

        _logger?.LogInformation("Transcode cache cleared ({Bytes} bytes freed).", freed);
        return freed;
    }

    /// <inheritdoc />
    public int PruneToLimit(long maxBytes, IReadOnlySet<Guid> liveItemIds) {
        if (maxBytes <= 0) {
            return 0;
        }

        var items = EnumerateCachedItems();
        var total = items.Sum(item => item.SizeBytes);
        if (total <= maxBytes) {
            return 0;
        }

        var evicted = 0;
        foreach (var item in items.OrderBy(item => item.LastWriteUtc)) {
            if (total <= maxBytes) {
                break;
            }

            if (liveItemIds.Contains(item.Id)) {
                continue;
            }

            // Cancel any job for this item before deleting so ffmpeg is not writing into the directory.
            HlsAssetService.CancelActiveGenerationsForItem(item.Id);
            foreach (var directory in item.Directories) {
                TryDeleteDirectory(directory);
            }

            total -= item.SizeBytes;
            evicted++;
        }

        if (evicted > 0) {
            _logger?.LogInformation(
                "Transcode cache eviction removed {Evicted} cached item(s) to stay under {MaxBytes} bytes.",
                evicted,
                maxBytes);
        }

        return evicted;
    }

    private IEnumerable<string> CacheRoots() {
        var cacheRoot = Path.GetFullPath(_options.CacheRoot);
        return CacheRootNames.Select(name => Path.Combine(cacheRoot, name));
    }

    // Groups per-item cache directories across the three roots into one logical cached item keyed by the
    // item GUID, with its total size and most-recent write time (used as the LRU eviction key).
    private IReadOnlyList<CachedItem> EnumerateCachedItems() {
        var byId = new Dictionary<Guid, CachedItemAccumulator>();
        foreach (var root in CacheRoots()) {
            if (!Directory.Exists(root)) {
                continue;
            }

            foreach (var directory in SafeEnumerateDirectories(root)) {
                if (!Guid.TryParse(Path.GetFileName(directory), out var id)) {
                    continue;
                }

                if (!byId.TryGetValue(id, out var accumulator)) {
                    accumulator = new CachedItemAccumulator(id);
                    byId[id] = accumulator;
                }

                accumulator.Add(directory, DirectorySizeBytes(directory), DirectoryLastWriteUtc(directory));
            }
        }

        return byId.Values.Select(accumulator => accumulator.ToCachedItem()).ToList();
    }

    private static long DirectorySizeBytes(string directory) {
        if (!Directory.Exists(directory)) {
            return 0;
        }

        long total = 0;
        foreach (var file in SafeEnumerateFiles(directory)) {
            try {
                total += new FileInfo(file).Length;
            } catch {
                // The file may have been deleted by a concurrent job between enumeration and stat.
            }
        }

        return total;
    }

    private static DateTime DirectoryLastWriteUtc(string directory) {
        var latest = DateTime.MinValue;
        try {
            latest = Directory.GetLastWriteTimeUtc(directory);
        } catch {
            // Directory vanished; fall through to file scan.
        }

        foreach (var file in SafeEnumerateFiles(directory)) {
            try {
                var written = File.GetLastWriteTimeUtc(file);
                if (written > latest) {
                    latest = written;
                }
            } catch {
                // Ignore files that disappear mid-scan.
            }
        }

        return latest;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string directory) {
        try {
            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories);
        } catch {
            return [];
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string root) {
        try {
            return Directory.EnumerateDirectories(root);
        } catch {
            return [];
        }
    }

    private void TryDeleteDirectory(string directory) {
        try {
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "Failed to evict cache directory {Directory}.", directory);
        }
    }

    private void TryDeleteDirectoryContents(string root) {
        if (!Directory.Exists(root)) {
            return;
        }

        foreach (var directory in SafeEnumerateDirectories(root)) {
            TryDeleteDirectory(directory);
        }

        foreach (var file in SafeEnumerateFilesTopLevel(root)) {
            try {
                File.Delete(file);
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "Failed to delete cache file {File}.", file);
            }
        }
    }

    private static IEnumerable<string> SafeEnumerateFilesTopLevel(string root) {
        try {
            return Directory.EnumerateFiles(root);
        } catch {
            return [];
        }
    }

    private sealed record CachedItem(Guid Id, long SizeBytes, DateTime LastWriteUtc, IReadOnlyList<string> Directories);

    private sealed class CachedItemAccumulator(Guid id) {
        private readonly List<string> _directories = [];
        private long _sizeBytes;
        private DateTime _lastWriteUtc = DateTime.MinValue;

        public void Add(string directory, long sizeBytes, DateTime lastWriteUtc) {
            _directories.Add(directory);
            _sizeBytes += sizeBytes;
            if (lastWriteUtc > _lastWriteUtc) {
                _lastWriteUtc = lastWriteUtc;
            }
        }

        public CachedItem ToCachedItem() => new(id, _sizeBytes, _lastWriteUtc, _directories);
    }
}
