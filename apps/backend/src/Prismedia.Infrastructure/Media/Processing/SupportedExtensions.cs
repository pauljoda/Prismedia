namespace Prismedia.Infrastructure.Media.Processing;

/// <summary>
/// Canonical file-extension sets for each media category, used by file discovery and scan handlers.
/// </summary>
public static class SupportedExtensions {
    public static readonly IReadOnlySet<string> Video = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mkv", ".mov", ".webm", ".avi", ".wmv", ".flv", ".ts", ".m2ts", ".mpg", ".mpeg"
    };

    public static readonly IReadOnlySet<string> Image = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tiff", ".tif", ".heic", ".avif",
        ".svg", ".ico", ".tga", ".psd"
    };

    public static readonly IReadOnlySet<string> Audio = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".ogg", ".aac", ".m4a", ".wma", ".opus",
        ".aiff", ".aif", ".alac", ".ape", ".dsf", ".dff", ".wv"
    };

    public static readonly IReadOnlySet<string> ComicArchive = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".cbz", ".cbr", ".zip"
    };

    /// <summary>
    /// Filename suffixes that indicate a generated/derived file (preview, thumb, sample).
    /// Files whose name (without extension) ends with one of these are skipped during scanning.
    /// </summary>
    public static bool IsGeneratedSuffix(string fileNameWithoutExtension) {
        return fileNameWithoutExtension.EndsWith("-preview", StringComparison.OrdinalIgnoreCase)
            || fileNameWithoutExtension.EndsWith("_preview", StringComparison.OrdinalIgnoreCase)
            || fileNameWithoutExtension.EndsWith(".preview", StringComparison.OrdinalIgnoreCase)
            || fileNameWithoutExtension.EndsWith("-thumb", StringComparison.OrdinalIgnoreCase)
            || fileNameWithoutExtension.EndsWith("_thumb", StringComparison.OrdinalIgnoreCase)
            || fileNameWithoutExtension.EndsWith(".thumb", StringComparison.OrdinalIgnoreCase)
            || fileNameWithoutExtension.EndsWith("-sprite", StringComparison.OrdinalIgnoreCase)
            || fileNameWithoutExtension.EndsWith("_sprite", StringComparison.OrdinalIgnoreCase)
            || fileNameWithoutExtension.EndsWith(".sprite", StringComparison.OrdinalIgnoreCase)
            || fileNameWithoutExtension.EndsWith("-sample", StringComparison.OrdinalIgnoreCase)
            || fileNameWithoutExtension.EndsWith("_sample", StringComparison.OrdinalIgnoreCase)
            || fileNameWithoutExtension.EndsWith(".sample", StringComparison.OrdinalIgnoreCase);
    }
}
