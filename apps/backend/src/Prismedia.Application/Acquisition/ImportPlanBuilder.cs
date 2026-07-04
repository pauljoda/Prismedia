using System.Text.RegularExpressions;

namespace Prismedia.Application.Acquisition;

/// <summary>Why an import was blocked for manual resolution rather than performed automatically.</summary>
public enum ImportBlockReason {
    /// <summary>The payload contained no supported book files.</summary>
    NoSupportedPayload,

    /// <summary>The payload contained multiple standalone books, so the correct one is ambiguous.</summary>
    AmbiguousMultiplePrimaries,

    /// <summary>The payload mixed a standalone book with comic archives, so the intent is ambiguous.</summary>
    MixedPayload
}

/// <summary>One file to import and the sanitized library-relative path it should land at.</summary>
public sealed record ImportPlanItem(string SourceRelativePath, string TargetRelativePath);

/// <summary>The outcome of planning an import: either a set of moves, or a block reason needing manual resolution.</summary>
public sealed record ImportPlan(bool Blocked, ImportBlockReason? BlockReason, IReadOnlyList<ImportPlanItem> Items) {
    public static ImportPlan Block(ImportBlockReason reason) => new(true, reason, []);
    public static ImportPlan For(IReadOnlyList<ImportPlanItem> items) => new(false, null, items);
}

/// <summary>Book metadata available for rendering target paths.</summary>
public sealed record ImportTemplateContext(string Title, string? Author, int? Year);

/// <summary>
/// Pure import planning: decides which downloaded files are the book payload and renders their target
/// paths from a profile template. Filesystem access lives in the infrastructure planner; this core is
/// deterministic so the format rules, ambiguity handling, and path sanitization are unit-testable.
/// </summary>
public static partial class ImportPlanBuilder {
    private static readonly IReadOnlySet<string> PrimaryBookExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".epub", ".pdf" };

    private static readonly IReadOnlySet<string> ComicArchiveExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cbz", ".zip" };

    [GeneratedRegex(@"[<>:""/\\|?*\x00-\x1f]")]
    private static partial Regex IllegalPathCharsRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex CollapseWhitespaceRegex();

    // Optional template tokens that embed connector text (e.g. "{ - Volume}"). v1 does not parse volume,
    // so these are dropped wholesale along with their connectors rather than left as literal braces.
    [GeneratedRegex(@"\{[^{}]*Volume[^{}]*\}", RegexOptions.IgnoreCase)]
    private static partial Regex OptionalVolumeTokenRegex();

    /// <summary>The book file extensions the importer recognizes.</summary>
    public static IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(PrimaryBookExtensions.Concat(ComicArchiveExtensions), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Plans the import for a set of files (paths relative to the download content root) given the book
    /// metadata and the profile path template.
    /// </summary>
    public static ImportPlan Plan(
        IReadOnlyList<string> relativeFilePaths,
        ImportTemplateContext context,
        string pathTemplate) {
        var supported = relativeFilePaths
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
            .ToArray();
        var primaries = supported.Where(path => PrimaryBookExtensions.Contains(Path.GetExtension(path))).ToArray();
        var archives = supported.Where(path => ComicArchiveExtensions.Contains(Path.GetExtension(path))).ToArray();

        if (supported.Length == 0) {
            return ImportPlan.Block(ImportBlockReason.NoSupportedPayload);
        }

        // A standalone book file mixed with comic archives is ambiguous intent.
        if (primaries.Length >= 1 && archives.Length >= 1) {
            return ImportPlan.Block(ImportBlockReason.MixedPayload);
        }

        if (primaries.Length >= 1) {
            // Distinct base names mean genuinely different books; a single base name in several formats
            // (e.g. "Book.epub" + "Book.pdf" + "Book.mobi") is one book, so pick the preferred format.
            var distinctBooks = primaries
                .Select(path => NormalizeBaseName(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            if (distinctBooks > 1) {
                return ImportPlan.Block(ImportBlockReason.AmbiguousMultiplePrimaries);
            }

            var chosen = PreferredPrimary(primaries);
            var target = RenderPath(pathTemplate, context, Path.GetExtension(chosen), fileNameOnly: false);
            return ImportPlan.For([new ImportPlanItem(chosen, target)]);
        }

        // One or more comic archives: treat them as volumes/chapters of one book under the rendered folder.
        var folder = RenderFolder(pathTemplate, context);
        var items = archives
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new ImportPlanItem(path, CombineRelative(folder, SanitizeSegment(Path.GetFileName(path)))))
            .ToArray();
        return ImportPlan.For(items);
    }

    /// <summary>Renders the template to a sanitized relative path, optionally keeping only the directory portion.</summary>
    private static string RenderPath(string template, ImportTemplateContext context, string extension, bool fileNameOnly) {
        var segments = template.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var rendered = new List<string>(segments.Length);
        foreach (var segment in segments) {
            var value = SanitizeSegment(ReplaceTokens(segment, context, extension));
            if (!string.IsNullOrWhiteSpace(value)) {
                rendered.Add(value);
            }
        }

        return string.Join('/', rendered);
    }

    /// <summary>Renders just the directory portion of the template (drops the final file-name segment).</summary>
    private static string RenderFolder(string template, ImportTemplateContext context) {
        var lastSlash = template.LastIndexOf('/');
        var directoryTemplate = lastSlash < 0 ? string.Empty : template[..lastSlash];
        return RenderPath(directoryTemplate, context, extension: string.Empty, fileNameOnly: false);
    }

    private static string ReplaceTokens(string segment, ImportTemplateContext context, string extension) {
        var result = OptionalVolumeTokenRegex().Replace(segment, string.Empty)
            .Replace("{Author}", context.Author ?? string.Empty, StringComparison.Ordinal)
            .Replace("{Title}", context.Title, StringComparison.Ordinal)
            .Replace("{Year}", context.Year?.ToString() ?? string.Empty, StringComparison.Ordinal)
            .Replace("{ext}", extension.TrimStart('.'), StringComparison.Ordinal);

        return CleanEmptyDecorations(result);
    }

    /// <summary>
    /// Cleans a token-substituted segment: drops connectors and brackets left empty by missing tokens
    /// (e.g. "()", "[]", a trailing " - "), collapses whitespace, and trims stray connectors. Shared by
    /// the book and media naming renderers so an absent token (a null year, an unknown quality) degrades
    /// to a clean name rather than leaving literal punctuation behind.
    /// </summary>
    internal static string CleanEmptyDecorations(string value) {
        value = Regex.Replace(value, @"\(\s*\)", string.Empty);
        value = Regex.Replace(value, @"\[\s*\]", string.Empty);
        value = CollapseWhitespaceRegex().Replace(value, " ");
        // Whitespace left dangling before a dot (e.g. "Title .ext" after "()" vanished before ".ext").
        value = Regex.Replace(value, @"\s+\.", ".");
        return value.Trim().Trim('-', '.', ' ').Trim();
    }

    /// <summary>
    /// Sanitizes one path segment for library placement: illegal filesystem characters become spaces,
    /// whitespace collapses, and relative markers are neutralized. Shared by the per-kind import planners
    /// so every import renders target paths under the same rules.
    /// </summary>
    public static string SanitizeSegment(string segment) {
        var cleaned = IllegalPathCharsRegex().Replace(segment, " ");
        cleaned = CollapseWhitespaceRegex().Replace(cleaned, " ").Trim();
        // Never allow a segment to escape upward or resolve to a relative marker.
        return cleaned is "." or ".." ? "_" : cleaned;
    }

    private static string CombineRelative(string folder, string fileName) =>
        string.IsNullOrEmpty(folder) ? fileName : $"{folder}/{fileName}";

    /// <summary>Normalizes a file's base name (no extension) so format variants of one book compare equal.</summary>
    private static string NormalizeBaseName(string path) =>
        CollapseWhitespaceRegex().Replace(Path.GetFileNameWithoutExtension(path).Replace('_', ' '), " ").Trim();

    /// <summary>Chooses the preferred format among one book's files: EPUB first, then PDF, else the first.</summary>
    private static string PreferredPrimary(IReadOnlyList<string> primaries) =>
        primaries.FirstOrDefault(path => string.Equals(Path.GetExtension(path), ".epub", StringComparison.OrdinalIgnoreCase))
        ?? primaries.FirstOrDefault(path => string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
        ?? primaries[0];
}
