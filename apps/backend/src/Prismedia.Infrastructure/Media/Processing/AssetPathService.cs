namespace Prismedia.Infrastructure.Media.Processing;

/// <summary>
/// Computes disk paths for generated asset I/O and API-relative URL paths
/// for database storage. Disk paths use the configured data directory;
/// URL paths use the <c>/assets/</c> prefix that the API serves from the cache root.
/// </summary>
public sealed class AssetPathService {
    private readonly string _cacheRoot;

    public AssetPathService(string dataDir, string? cacheDir = null) {
        var resolvedDataDir = Path.GetFullPath(dataDir);
        _cacheRoot = cacheDir is null
            ? Path.Combine(resolvedDataDir, "cache")
            : Path.GetFullPath(cacheDir);
    }

    public string CacheRoot => _cacheRoot;

    public string VideoThumbnailPath(Guid entityId) =>
        Path.Combine(_cacheRoot, "videos", entityId.ToString(), "thumb.jpg");

    /// <summary>
    /// Disk path for the small grid-sized cover variant. Stored in one flat,
    /// kind-agnostic directory keyed by entity id so any entity kind shares the
    /// same convention.
    /// </summary>
    public string GridThumbnailPath(Guid entityId) =>
        Path.Combine(_cacheRoot, "grid-thumbs", entityId.ToString() + ".jpg");

    /// <summary>Disk path for the double-density grid cover variant.</summary>
    public string GridThumbnail2xPath(Guid entityId) =>
        Path.Combine(_cacheRoot, "grid-thumbs", entityId.ToString() + "@2x.jpg");

    /// <summary>Maps a stored <c>/assets/...</c> cover URL back to its cache-relative disk path.</summary>
    public string? ResolveAssetDiskPath(string assetUrl) {
        const string prefix = "/assets/";
        if (string.IsNullOrEmpty(assetUrl) || !assetUrl.StartsWith(prefix, StringComparison.Ordinal)) {
            return null;
        }

        var relative = assetUrl[prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(_cacheRoot, relative));
        var rootPrefix = _cacheRoot.EndsWith(Path.DirectorySeparatorChar)
            ? _cacheRoot
            : _cacheRoot + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootPrefix, StringComparison.Ordinal)
            ? candidate
            : null;
    }

    public string VideoPreviewPath(Guid entityId) =>
        Path.Combine(_cacheRoot, "videos", entityId.ToString(), "preview.mp4");

    public string VideoSpritePath(Guid entityId) =>
        Path.Combine(_cacheRoot, "videos", entityId.ToString(), "sprite.jpg");

    public string VideoTrickplayVttPath(Guid entityId) =>
        Path.Combine(_cacheRoot, "videos", entityId.ToString(), "trickplay.vtt");

    public string TrickplayFrameDir(Guid entityId) =>
        Path.Combine(_cacheRoot, "videos", entityId.ToString(), "trickplay-frames");

    public string TrickplayTileDir(Guid entityId, int width) =>
        Path.Combine(_cacheRoot, "trickplay", entityId.ToString(), width.ToString());

    public string ImageThumbnailPath(Guid entityId) =>
        Path.Combine(_cacheRoot, "images", entityId.ToString(), "thumb.jpg");

    public string ImagePreviewPath(Guid entityId) =>
        Path.Combine(_cacheRoot, "images", entityId.ToString(), "preview.mp4");

    public string BookPageThumbnailPath(Guid entityId) =>
        Path.Combine(_cacheRoot, "book-pages", entityId.ToString(), "thumb.jpg");

    public string BookCoverThumbnailPath(Guid entityId) =>
        Path.Combine(_cacheRoot, "book-covers", entityId.ToString(), "thumb.jpg");

    public string AudioWaveformPath(Guid entityId) =>
        Path.Combine(_cacheRoot, "audio-tracks", entityId.ToString(), "waveform.json");

    public string SubtitleDir(Guid entityId) =>
        Path.Combine(_cacheRoot, "videos", entityId.ToString(), "subtitles");

    public static string VideoThumbnailUrl(Guid entityId) =>
        $"/assets/videos/{entityId}/thumb.jpg";

    public static string GridThumbnailUrl(Guid entityId) =>
        $"/assets/grid-thumbs/{entityId}.jpg";

    public static string GridThumbnail2xUrl(Guid entityId) =>
        $"/assets/grid-thumbs/{entityId}@2x.jpg";

    public static string VideoPreviewUrl(Guid entityId) =>
        $"/assets/videos/{entityId}/preview.mp4";

    public static string VideoTrickplayVttUrl(Guid entityId) =>
        $"/assets/videos/{entityId}/trickplay.vtt";

    public static string TrickplayPlaylistUrl(Guid entityId, int width) =>
        $"/Videos/{entityId}/Trickplay/{width}/tiles.m3u8";

    public static string ImageThumbnailUrl(Guid entityId) =>
        $"/assets/images/{entityId}/thumb.jpg";

    public static string ImagePreviewUrl(Guid entityId) =>
        $"/assets/images/{entityId}/preview.mp4";

    public static string BookPageThumbnailUrl(Guid entityId) =>
        $"/assets/book-pages/{entityId}/thumb.jpg";

    public static string BookCoverThumbnailUrl(Guid entityId) =>
        $"/assets/book-covers/{entityId}/thumb.jpg";

    public static string AudioWaveformUrl(Guid entityId) =>
        $"/assets/audio-tracks/{entityId}/waveform.json";

    public static string SubtitleUrl(Guid entityId, string fileName) =>
        $"/assets/videos/{entityId}/subtitles/{fileName}";
}
