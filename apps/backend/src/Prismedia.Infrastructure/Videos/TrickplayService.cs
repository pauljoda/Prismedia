using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Videos;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Videos;

/// <summary>
/// Filesystem-backed Jellyfin-style trickplay playlist and tile resolver.
/// </summary>
public sealed class TrickplayService : ITrickplayService {
    private const int DefaultTileColumns = 5;
    private const int DefaultTileRows = 5;
    private const double DefaultIntervalSeconds = 10;
    private readonly string _cacheRoot;
    private readonly PrismediaDbContext? _db;

    public TrickplayService(HlsAssetServiceOptions options) {
        _cacheRoot = Path.GetFullPath(options.CacheRoot);
    }

    public TrickplayService(HlsAssetServiceOptions options, PrismediaDbContext db)
        : this(options) {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<TrickplayPlaylist?> GetPlaylistAsync(Guid itemId, int width, CancellationToken cancellationToken) {
        if (width <= 0) {
            return null;
        }

        var resolvedWidth = await ResolveTrickplayWidthAsync(itemId, width, cancellationToken);
        if (resolvedWidth is null) {
            return null;
        }

        var root = resolvedWidth.Root;
        var playlistPath = Path.Combine(root, "tiles.m3u8");
        if (File.Exists(playlistPath)) {
            return new TrickplayPlaylist(
                await File.ReadAllTextAsync(playlistPath, cancellationToken),
                "public, max-age=60");
        }

        if (!Directory.Exists(root)) {
            return null;
        }

        var tiles = Directory.EnumerateFiles(root, "*.jpg")
            .Select(path => new { Path = path, Index = ParseTileIndex(path) })
            .Where(tile => tile.Index is not null)
            .OrderBy(tile => tile.Index)
            .ToList();
        if (tiles.Count == 0) {
            return null;
        }

        var info = await GetInfoAsync(itemId, resolvedWidth.Width, tiles.Count, cancellationToken);
        return new TrickplayPlaylist(BuildImagesOnlyPlaylist(tiles.Count, info), "public, max-age=60");
    }

    /// <inheritdoc />
    public async Task<TrickplayTile?> GetTileAsync(Guid itemId, int width, int index, CancellationToken cancellationToken) {
        if (width <= 0 || index < 0) {
            return null;
        }

        var resolvedWidth = await ResolveTrickplayWidthAsync(itemId, width, cancellationToken);
        if (resolvedWidth is null) {
            return null;
        }

        var path = Path.Combine(resolvedWidth.Root, $"{index}.jpg");
        var resolved = ResolveInside(resolvedWidth.Root, $"{index}.jpg");
        return resolved is not null && File.Exists(path)
            ? new TrickplayTile(path, "image/jpeg", "public, max-age=31536000, immutable")
            : null;
    }

    private string TrickplayRoot(Guid itemId, int width) =>
        Path.Combine(_cacheRoot, "trickplay", itemId.ToString(), width.ToString(CultureInfo.InvariantCulture));

    private async Task<ResolvedTrickplayWidth?> ResolveTrickplayWidthAsync(
        Guid itemId,
        int requestedWidth,
        CancellationToken cancellationToken) {
        var exactRoot = TrickplayRoot(itemId, requestedWidth);
        if (Directory.Exists(exactRoot)) {
            return new ResolvedTrickplayWidth(requestedWidth, exactRoot);
        }

        var widths = new HashSet<int>();
        if (_db is not null) {
            var persistedWidths = await _db.TrickplayInfos.AsNoTracking()
                .Where(row => row.EntityId == itemId)
                .Select(row => row.Width)
                .ToListAsync(cancellationToken);
            foreach (var persistedWidth in persistedWidths) {
                widths.Add(persistedWidth);
            }
        }

        var itemRoot = Path.Combine(_cacheRoot, "trickplay", itemId.ToString());
        if (Directory.Exists(itemRoot)) {
            foreach (var directory in Directory.EnumerateDirectories(itemRoot)) {
                if (int.TryParse(
                    Path.GetFileName(directory),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var generatedWidth)) {
                    widths.Add(generatedWidth);
                }
            }
        }

        var fallbackWidth = widths
            .Where(candidate => Directory.Exists(TrickplayRoot(itemId, candidate)))
            .OrderBy(candidate => Math.Abs(candidate - requestedWidth))
            .ThenByDescending(candidate => candidate)
            .FirstOrDefault();

        return fallbackWidth > 0
            ? new ResolvedTrickplayWidth(fallbackWidth, TrickplayRoot(itemId, fallbackWidth))
            : null;
    }

    private async Task<TrickplayInfoRow> GetInfoAsync(
        Guid itemId,
        int width,
        int tileCount,
        CancellationToken cancellationToken) {
        if (_db is not null) {
            var persisted = await _db.TrickplayInfos.AsNoTracking()
                .FirstOrDefaultAsync(row => row.EntityId == itemId && row.Width == width, cancellationToken);
            if (persisted is not null) {
                return persisted;
            }
        }

        return new TrickplayInfoRow {
            EntityId = itemId,
            Width = width,
            Height = Math.Max(1, (int)Math.Round(width * 9 / 16d)),
            TileWidth = DefaultTileColumns,
            TileHeight = DefaultTileRows,
            ThumbnailCount = tileCount * DefaultTileColumns * DefaultTileRows,
            IntervalSeconds = DefaultIntervalSeconds,
            Bandwidth = 0
        };
    }

    private static string BuildImagesOnlyPlaylist(int tileCount, TrickplayInfoRow info) {
        var durationPerTile = info.IntervalSeconds * info.TileWidth * info.TileHeight;
        var lines = new List<string>
        {
            "#EXTM3U",
            "#EXT-X-VERSION:7",
            "#EXT-X-PLAYLIST-TYPE:VOD",
            "#EXT-X-IMAGES-ONLY",
            $"#EXT-X-TARGETDURATION:{Math.Max(1, (int)Math.Ceiling(durationPerTile))}",
            $"#EXT-X-TILES:RESOLUTION={info.Width}x{info.Height},LAYOUT={info.TileWidth}x{info.TileHeight},DURATION={info.IntervalSeconds:0.###}"
        };

        for (var index = 0; index < tileCount; index++) {
            lines.Add($"#EXTINF:{durationPerTile:0.###},");
            lines.Add($"{index}.jpg");
        }

        lines.Add("#EXT-X-ENDLIST");
        lines.Add(string.Empty);
        return string.Join('\n', lines);
    }

    private static int? ParseTileIndex(string path) {
        var name = Path.GetFileNameWithoutExtension(path);
        return int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index : null;
    }

    private static string? ResolveInside(string root, string assetPath) {
        var rootFullPath = Path.GetFullPath(root);
        var resolved = Path.GetFullPath(Path.Combine(rootFullPath, assetPath));
        var rootWithSeparator = rootFullPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootFullPath
            : rootFullPath + Path.DirectorySeparatorChar;

        return resolved.StartsWith(rootWithSeparator, StringComparison.Ordinal) ? resolved : null;
    }

    private sealed record ResolvedTrickplayWidth(int Width, string Root);
}
