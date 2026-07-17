using Prismedia.Application.Files;
using Prismedia.Contracts.Media;

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
        Path.Combine(_cacheRoot, AssetPaths.Videos, entityId.ToString(), AssetPaths.ThumbnailFile);

    /// <summary>
    /// Disk path for the small grid-sized cover variant. Stored in one flat,
    /// kind-agnostic directory keyed by entity id so any entity kind shares the
    /// same convention.
    /// </summary>
    public string GridThumbnailPath(Guid entityId) =>
        Path.Combine(_cacheRoot, AssetPaths.GridThumbs, entityId.ToString() + ".jpg");

    /// <summary>Disk path for the double-density grid cover variant.</summary>
    public string GridThumbnail2xPath(Guid entityId) =>
        Path.Combine(_cacheRoot, AssetPaths.GridThumbs, entityId.ToString() + "@2x.jpg");

    /// <summary>Maps a stored <c>/assets/...</c> cover URL back to its cache-relative disk path.</summary>
    public string? ResolveAssetDiskPath(string assetUrl) {
        const string prefix = AssetPaths.AssetsUrlPrefix;
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
        Path.Combine(_cacheRoot, AssetPaths.Videos, entityId.ToString(), AssetPaths.PreviewFile);

    public string VideoSpritePath(Guid entityId) =>
        Path.Combine(_cacheRoot, AssetPaths.Videos, entityId.ToString(), AssetPaths.SpriteFile);

    public string VideoTrickplayVttPath(Guid entityId) =>
        Path.Combine(_cacheRoot, AssetPaths.Videos, entityId.ToString(), AssetPaths.TrickplayVttFile);

    public string TrickplayFrameDir(Guid entityId) =>
        Path.Combine(_cacheRoot, AssetPaths.Videos, entityId.ToString(), AssetPaths.TrickplayFrames);

    public string TrickplayTileDir(Guid entityId, int width) =>
        Path.Combine(_cacheRoot, AssetPaths.Trickplay, entityId.ToString(), width.ToString());

    public string ImageThumbnailPath(Guid entityId) =>
        Path.Combine(_cacheRoot, AssetPaths.Images, entityId.ToString(), AssetPaths.ThumbnailFile);

    public string ImagePreviewPath(Guid entityId) =>
        Path.Combine(_cacheRoot, AssetPaths.Images, entityId.ToString(), AssetPaths.PreviewFile);

    public string BookPageThumbnailPath(Guid entityId) =>
        Path.Combine(_cacheRoot, AssetPaths.BookPages, entityId.ToString(), AssetPaths.ThumbnailFile);

    public string BookCoverThumbnailPath(Guid entityId) =>
        Path.Combine(_cacheRoot, AssetPaths.BookCovers, entityId.ToString(), AssetPaths.ThumbnailFile);

    public string AudioWaveformPath(Guid entityId) =>
        Path.Combine(_cacheRoot, AssetPaths.AudioTracks, entityId.ToString(), AssetPaths.WaveformFile);

    public string SubtitleDir(Guid entityId) =>
        Path.Combine(_cacheRoot, AssetPaths.Videos, entityId.ToString(), AssetPaths.Subtitles);

    /// <summary>
    /// Creates the entity subtitle directory one component at a time while rejecting any existing
    /// cache component that is a filesystem link. This avoids recursive creation traversing a
    /// planted <c>videos</c>, entity, or <c>subtitles</c> symlink.
    /// </summary>
    public string EnsureSubtitleDirectorySafe(Guid entityId) {
        EnsureOrdinaryDirectory(_cacheRoot, createIfMissing: true);
        var current = _cacheRoot;
        foreach (var segment in new[] { AssetPaths.Videos, entityId.ToString(), AssetPaths.Subtitles }) {
            current = Path.Combine(current, segment);
            EnsureOrdinaryDirectory(current, createIfMissing: true);
        }

        return current;
    }

    /// <summary>Same-filesystem staging directory for one atomic subtitle import.</summary>
    public string SubtitleStagingDir(Guid entityId, Guid operationId) =>
        Path.Combine(SubtitleDir(entityId), $".staging-{operationId:N}");

    /// <summary>
    /// Computes paired content-versioned sidecar asset paths from already-safe hash tokens.
    /// </summary>
    public SubtitleAssetPaths SidecarSubtitlePaths(
        Guid entityId,
        string sourceToken,
        string contentToken,
        string sourceFormat) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentToken);
        if (!IsLowerHexToken(sourceToken) || !IsLowerHexToken(contentToken)) {
            throw new ArgumentException("Subtitle asset tokens must contain lowercase hexadecimal characters only.");
        }

        var stem = $"sidecar-{sourceToken}-{contentToken}";
        var normalizedPath = Path.Combine(SubtitleDir(entityId), stem + SubtitleFileExtensions.Vtt);
        var sourcePath = SubtitleFormats.IsStyled(sourceFormat)
            ? Path.Combine(SubtitleDir(entityId), stem + SubtitleFileExtensions.ForFormat(sourceFormat))
            : null;
        return new SubtitleAssetPaths(normalizedPath, sourcePath);
    }

    /// <summary>
    /// Whether a path is an exact generated subtitle file directly inside one video's subtitle directory.
    /// </summary>
    public bool IsSubtitleAssetPath(Guid entityId, string path) {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) {
            return false;
        }

        try {
            var fullPath = Path.GetFullPath(path);
            var expectedDirectory = Path.GetFullPath(SubtitleDir(entityId));
            return FileSystemPathComparison.Equals(
                Path.GetDirectoryName(fullPath) ?? string.Empty,
                expectedDirectory) &&
                HasOrdinaryDirectoryChain(expectedDirectory);
        } catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) {
            return false;
        }
    }

    /// <summary>
    /// Whether a path is an exact generated subtitle file for any video under the configured cache root.
    /// </summary>
    public bool IsSubtitleAssetPath(string path) {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) {
            return false;
        }

        try {
            var fullPath = Path.GetFullPath(path);
            if (!FileSystemPathComparison.IsSameOrDescendant(_cacheRoot, fullPath)) {
                return false;
            }

            var segments = Path.GetRelativePath(_cacheRoot, fullPath)
                .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            return segments.Length == 4 &&
                string.Equals(segments[0], AssetPaths.Videos, StringComparison.Ordinal) &&
                Guid.TryParse(segments[1], out var entityId) &&
                string.Equals(segments[2], AssetPaths.Subtitles, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(segments[3]) &&
                IsSubtitleAssetPath(entityId, fullPath);
        } catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) {
            return false;
        }
    }

    public static string VideoThumbnailUrl(Guid entityId) =>
        $"{AssetPaths.AssetsUrlPrefix}{AssetPaths.Videos}/{entityId}/{AssetPaths.ThumbnailFile}";

    public static string GridThumbnailUrl(Guid entityId) =>
        $"{AssetPaths.AssetsUrlPrefix}{AssetPaths.GridThumbs}/{entityId}.jpg";

    public static string GridThumbnail2xUrl(Guid entityId) =>
        $"{AssetPaths.AssetsUrlPrefix}{AssetPaths.GridThumbs}/{entityId}@2x.jpg";

    public static string VideoPreviewUrl(Guid entityId) =>
        $"{AssetPaths.AssetsUrlPrefix}{AssetPaths.Videos}/{entityId}/{AssetPaths.PreviewFile}";

    public static string VideoTrickplayVttUrl(Guid entityId) =>
        $"{AssetPaths.AssetsUrlPrefix}{AssetPaths.Videos}/{entityId}/{AssetPaths.TrickplayVttFile}";

    public static string TrickplayPlaylistUrl(Guid entityId, int width) =>
        $"/Videos/{entityId}/Trickplay/{width}/tiles.m3u8";

    public static string ImageThumbnailUrl(Guid entityId) =>
        $"{AssetPaths.AssetsUrlPrefix}{AssetPaths.Images}/{entityId}/{AssetPaths.ThumbnailFile}";

    public static string ImagePreviewUrl(Guid entityId) =>
        $"{AssetPaths.AssetsUrlPrefix}{AssetPaths.Images}/{entityId}/{AssetPaths.PreviewFile}";

    public static string BookPageThumbnailUrl(Guid entityId) =>
        $"{AssetPaths.AssetsUrlPrefix}{AssetPaths.BookPages}/{entityId}/{AssetPaths.ThumbnailFile}";

    public static string BookCoverThumbnailUrl(Guid entityId) =>
        $"{AssetPaths.AssetsUrlPrefix}{AssetPaths.BookCovers}/{entityId}/{AssetPaths.ThumbnailFile}";

    public static string AudioWaveformUrl(Guid entityId) =>
        $"{AssetPaths.AssetsUrlPrefix}{AssetPaths.AudioTracks}/{entityId}/{AssetPaths.WaveformFile}";

    public static string SubtitleUrl(Guid entityId, string fileName) =>
        $"{AssetPaths.AssetsUrlPrefix}{AssetPaths.Videos}/{entityId}/{AssetPaths.Subtitles}/{fileName}";

    private static bool IsLowerHexToken(string value) =>
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private bool HasOrdinaryDirectoryChain(string targetDirectory) {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_cacheRoot));
        if (!IsOrdinaryDirectory(root)) {
            return false;
        }

        var relative = Path.GetRelativePath(root, targetDirectory);
        if (relative == "." || Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal)) {
            return relative == ".";
        }

        var current = root;
        foreach (var segment in relative.Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries)) {
            current = Path.Combine(current, segment);
            if (!IsOrdinaryDirectory(current)) {
                return false;
            }
        }

        return true;
    }

    private static void EnsureOrdinaryDirectory(string path, bool createIfMissing) {
        if (!Directory.Exists(path) && createIfMissing) {
            Directory.CreateDirectory(path);
        }

        if (!IsOrdinaryDirectory(path)) {
            throw new IOException("Generated subtitle cache directories must be ordinary directories, not filesystem links.");
        }
    }

    private static bool IsOrdinaryDirectory(string path) {
        try {
            var attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.Directory) &&
                !attributes.HasFlag(FileAttributes.ReparsePoint);
        } catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) {
            return false;
        }
    }
}

/// <summary>Paired normalized and optional styled-source subtitle cache paths.</summary>
public sealed record SubtitleAssetPaths(string StoragePath, string? SourcePath);
