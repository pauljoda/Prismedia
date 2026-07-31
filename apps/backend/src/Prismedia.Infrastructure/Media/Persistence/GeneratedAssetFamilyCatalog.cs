using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Videos;

namespace Prismedia.Infrastructure.Media.Persistence;

/// <summary>Complete infrastructure cache strategies selected by domain processing asset family.</summary>
internal static class GeneratedAssetFamilyCatalog {
    private sealed record Layout(
        Func<AssetPathService, Guid, IReadOnlyList<string>> ExpectedPaths,
        Action<AssetPathService, Guid, Action<string>, Action<string>> Delete,
        Func<AssetPathService, ISet<string>, CancellationToken, int> Cleanup);

    private static readonly IReadOnlyDictionary<GeneratedAssetFamily, Layout> Layouts =
        new Dictionary<GeneratedAssetFamily, Layout> {
            [GeneratedAssetFamily.Video] = new(VideoExpected, DeleteVideo, CleanupVideo),
            [GeneratedAssetFamily.Image] = new(ImageExpected, DeleteImage, (assets, ids, token) => CleanupDirectories(assets, AssetPaths.Images, ids, token)),
            [GeneratedAssetFamily.BookPage] = new(BookPageExpected, DeleteBookPage, (assets, ids, token) => CleanupDirectories(assets, AssetPaths.BookPages, ids, token)),
            [GeneratedAssetFamily.AudioTrack] = new(AudioExpected, DeleteAudio, (assets, ids, token) => CleanupDirectories(assets, AssetPaths.AudioTracks, ids, token))
        };

    static GeneratedAssetFamilyCatalog() {
        var required = Enum.GetValues<GeneratedAssetFamily>().Where(family => family != GeneratedAssetFamily.None);
        if (required.Any(family => !Layouts.ContainsKey(family))) {
            throw new InvalidOperationException("Every generated asset family requires a complete infrastructure cache strategy.");
        }
        if (EntityKindRegistry.All.Select(definition => definition.Processing.AssetFamily)
            .Where(family => family != GeneratedAssetFamily.None)
            .Any(family => !Layouts.ContainsKey(family))) {
            throw new InvalidOperationException("A discovered entity definition selected an unregistered generated asset family.");
        }
    }

    public static IReadOnlyList<string> ExpectedPaths(AssetPathService assets, GeneratedAssetFamily family, Guid entityId) =>
        Require(family).ExpectedPaths(assets, entityId);

    public static void DeleteGeneratedAssets(AssetPathService assets, GeneratedAssetFamily family, Guid entityId,
        Action<string> deleteFile, Action<string> deleteDirectory) =>
        Require(family).Delete(assets, entityId, deleteFile, deleteDirectory);

    public static int CleanupOrphanDirectories(AssetPathService assets, GeneratedAssetFamily family,
        ISet<string> activeIds, CancellationToken cancellationToken) =>
        Require(family).Cleanup(assets, activeIds, cancellationToken);

    private static Layout Require(GeneratedAssetFamily family) =>
        family != GeneratedAssetFamily.None && Layouts.TryGetValue(family, out var layout)
            ? layout
            : throw new InvalidOperationException($"Generated asset family '{family.ToCode()}' has no cache strategy.");

    private static IReadOnlyList<string> VideoExpected(AssetPathService assets, Guid id) => [assets.VideoThumbnailPath(id)];
    private static IReadOnlyList<string> ImageExpected(AssetPathService assets, Guid id) => [assets.ImageThumbnailPath(id)];
    private static IReadOnlyList<string> BookPageExpected(AssetPathService assets, Guid id) => [assets.BookPageThumbnailPath(id)];
    private static IReadOnlyList<string> AudioExpected(AssetPathService assets, Guid id) => [assets.AudioWaveformPath(id)];

    private static void DeleteVideo(AssetPathService assets, Guid entityId, Action<string> deleteFile, Action<string> deleteDirectory) {
        var id = entityId.ToString();
        HlsAssetService.CancelActiveGenerationsForItem(entityId);
        deleteFile(Path.Combine(assets.CacheRoot, AssetPaths.Videos, id, AssetPaths.ThumbnailFile));
        deleteFile(Path.Combine(assets.CacheRoot, AssetPaths.Videos, id, AssetPaths.PreviewFile));
        deleteFile(Path.Combine(assets.CacheRoot, AssetPaths.Videos, id, AssetPaths.SpriteFile));
        deleteFile(Path.Combine(assets.CacheRoot, AssetPaths.GridThumbs, id + ".jpg"));
        deleteFile(Path.Combine(assets.CacheRoot, AssetPaths.GridThumbs, id + "@2x.jpg"));
        deleteFile(Path.Combine(assets.CacheRoot, AssetPaths.Videos, id, AssetPaths.TrickplayVttFile));
        deleteDirectory(Path.Combine(assets.CacheRoot, AssetPaths.Videos, id, AssetPaths.TrickplayFrames));
        deleteDirectory(Path.Combine(assets.CacheRoot, AssetPaths.Trickplay, id));
        deleteDirectory(Path.Combine(assets.CacheRoot, AssetPaths.Hlsv, id));
        deleteDirectory(Path.Combine(assets.CacheRoot, AssetPaths.Hls2, id));
        deleteDirectory(Path.Combine(assets.CacheRoot, AssetPaths.Hls, id));
    }

    private static void DeleteImage(AssetPathService assets, Guid id, Action<string> deleteFile, Action<string> _) {
        deleteFile(Path.Combine(assets.CacheRoot, AssetPaths.Images, id.ToString(), AssetPaths.ThumbnailFile));
        deleteFile(Path.Combine(assets.CacheRoot, AssetPaths.Images, id.ToString(), AssetPaths.PreviewFile));
    }
    private static void DeleteBookPage(AssetPathService assets, Guid id, Action<string> deleteFile, Action<string> _) =>
        deleteFile(Path.Combine(assets.CacheRoot, AssetPaths.BookPages, id.ToString(), AssetPaths.ThumbnailFile));
    private static void DeleteAudio(AssetPathService assets, Guid id, Action<string> deleteFile, Action<string> _) =>
        deleteFile(Path.Combine(assets.CacheRoot, AssetPaths.AudioTracks, id.ToString(), AssetPaths.WaveformFile));

    private static int CleanupVideo(AssetPathService assets, ISet<string> activeIds, CancellationToken token) =>
        CleanupDirectories(assets, AssetPaths.Videos, activeIds, token)
        + CleanupDirectories(assets, AssetPaths.Trickplay, activeIds, token)
        + CleanupDirectories(assets, AssetPaths.Hlsv, activeIds, token)
        + CleanupDirectories(assets, AssetPaths.Hls2, activeIds, token)
        + CleanupDirectories(assets, AssetPaths.Hls, activeIds, token)
        + CleanupGridFiles(assets, activeIds, token);

    private static int CleanupDirectories(AssetPathService assets, string root, ISet<string> activeIds, CancellationToken token) {
        var directory = Path.Combine(assets.CacheRoot, root);
        if (!Directory.Exists(directory)) return 0;
        var removed = 0;
        foreach (var path in Directory.EnumerateDirectories(directory)) {
            token.ThrowIfCancellationRequested();
            if (!Guid.TryParse(Path.GetFileName(path), out var id) || activeIds.Contains(id.ToString())) continue;
            try { Directory.Delete(path, recursive: true); removed++; } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return removed;
    }

    private static int CleanupGridFiles(AssetPathService assets, ISet<string> activeIds, CancellationToken token) {
        var directory = Path.Combine(assets.CacheRoot, AssetPaths.GridThumbs);
        if (!Directory.Exists(directory)) return 0;
        var orphanIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(directory, "*.jpg")) {
            token.ThrowIfCancellationRequested();
            var stem = Path.GetFileNameWithoutExtension(path).Replace("@2x", "", StringComparison.Ordinal);
            if (Guid.TryParse(stem, out var id) && !activeIds.Contains(id.ToString())) orphanIds.Add(id.ToString());
        }
        var removed = 0;
        foreach (var id in orphanIds) {
            foreach (var suffix in new[] { ".jpg", "@2x.jpg" }) {
                var path = Path.Combine(directory, id + suffix);
                if (!File.Exists(path)) continue;
                try { File.Delete(path); removed++; } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        return removed;
    }
}
