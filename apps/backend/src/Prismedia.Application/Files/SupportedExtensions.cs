using Prismedia.Contracts.Media;

namespace Prismedia.Application.Files;

/// <summary>
/// Canonical file-extension sets for each media category, used by file discovery and scan handlers.
/// </summary>
public static class SupportedExtensions {
    /// <summary>Canonical ComicInfo metadata sidecar filename.</summary>
    public const string ComicInfoSidecarFileName = "ComicInfo.xml";

    /// <summary>Canonical Metron metadata sidecar filename.</summary>
    public const string MetronInfoSidecarFileName = "MetronInfo.xml";

    public static readonly IReadOnlySet<string> Video = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mkv", ".mov", ".webm", ".avi", ".wmv", ".flv", ".ts", ".m2ts", ".mpg", ".mpeg"
    };

    /// <summary>Adjacent subtitle extensions included in video scan snapshots.</summary>
    public static IReadOnlySet<string> VideoSubtitleSidecar => SubtitleFileExtensions.Supported;

    public static readonly IReadOnlySet<string> Image = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tiff", ".tif", ".heic", ".avif",
        ".svg", ".ico", ".tga", ".psd",
        // Gallery items can be animated image-like clips commonly used on the web.
        ".webm", ".mp4", ".m4v", ".mkv", ".mov", ".avi", ".wmv", ".flv"
    };

    public static readonly IReadOnlySet<string> Audio = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".ogg", ".aac", ".m4a", ".m4b", ".wma", ".opus",
        ".aiff", ".aif", ".alac", ".ape", ".dsf", ".dff", ".wv"
    };

    public static readonly IReadOnlySet<string> ComicArchive = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".cbz", ".zip"
    };

    /// <summary>
    /// Raster formats that can safely become pages in a normalized comic archive. This is
    /// intentionally narrower than <see cref="Image"/>, which also accepts gallery-oriented
    /// vector, layered, icon, and video formats.
    /// </summary>
    public static readonly IReadOnlySet<string> ComicPage = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".apng", ".gif", ".webp", ".avif", ".bmp", ".tiff", ".tif"
    };

    /// <summary>XML sidecars whose exact filenames establish or enrich a loose-page comic.</summary>
    public static readonly IReadOnlySet<string> ComicMetadataSidecar = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".xml"
    };

    /// <summary>Returns whether a filename is a supported comic metadata sidecar.</summary>
    public static bool IsComicMetadataSidecar(string path) {
        var name = Path.GetFileName(path);
        return name.Equals(ComicInfoSidecarFileName, StringComparison.OrdinalIgnoreCase)
            || name.Equals(MetronInfoSidecarFileName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Single-file book formats (EPUB, PDF). Each file is one self-contained book whose
    /// chapters live inside the file, unlike comic archives which group pages and chapters.
    /// </summary>
    public static readonly IReadOnlySet<string> Book = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".epub", ".pdf"
    };

    /// <summary>Common audiobook containers discovered by a book-library scan.</summary>
    public static readonly IReadOnlySet<string> Audiobook = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".m4b", ".m4a", ".mp3"
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
