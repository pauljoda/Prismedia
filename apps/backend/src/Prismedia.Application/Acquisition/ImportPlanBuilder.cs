using System.Text.RegularExpressions;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>Why an import was blocked for manual resolution rather than performed automatically.</summary>
public enum ImportBlockReason {
    /// <summary>The payload contained no supported publication files.</summary>
    NoSupportedPayload,

    /// <summary>The payload contained supported video, but its positive TV identifiers did not agree with the requested unit.</summary>
    NoMatchingTvUnit,

    /// <summary>The payload contained multiple standalone prose books, so the correct one is ambiguous.</summary>
    AmbiguousMultiplePrimaries,

    /// <summary>The payload mixed a standalone book with comic archives, so the intent is ambiguous.</summary>
    MixedPayload
}

/// <summary>One file to import and the sanitized library-relative path it should land at.</summary>
/// <param name="SourceRelativePath">Download-payload-relative source path.</param>
/// <param name="TargetRelativePath">Sanitized target path beneath the library root.</param>
/// <param name="TargetEntityId">Requested Entity this exact file was selected to satisfy, when known.</param>
public sealed record ImportPlanItem(
    string SourceRelativePath,
    string TargetRelativePath,
    Guid? TargetEntityId = null);

/// <summary>The outcome of planning an import: either a set of moves, or a block reason needing manual resolution.</summary>
public sealed record ImportPlan(bool Blocked, ImportBlockReason? BlockReason, IReadOnlyList<ImportPlanItem> Items) {
    public static ImportPlan Block(ImportBlockReason reason) => new(true, reason, []);
    public static ImportPlan For(IReadOnlyList<ImportPlanItem> items) => new(false, null, items);
}

/// <summary>Publication metadata available for rendering target paths.</summary>
public sealed record ImportTemplateContext(
    string Title,
    string? Author,
    int? Year,
    string? Series = null,
    int? VolumeNumber = null);

/// <summary>
/// Pure import planning: decides which downloaded files are the publication payload and renders their target
/// paths from a profile template. Filesystem access lives in the infrastructure planner; this core is
/// deterministic so the format rules, ambiguity handling, and path sanitization are unit-testable.
/// </summary>
public static partial class ImportPlanBuilder {
    /// <summary>Standalone ebook formats accepted by publication import and file-based discovery.</summary>
    public static IReadOnlySet<string> PrimaryBookExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".epub", ".pdf" };

    /// <summary>Serialized comic archives accepted by publication import and file-based discovery.</summary>
    public static IReadOnlySet<string> ComicArchiveExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cbz", ".zip" };

    /// <summary>Audiobook formats accepted by publication import and file-based discovery.</summary>
    public static IReadOnlySet<string> AudiobookExtensions { get; } = AudiobookReleaseShape.ImportableExtensions;

    // Download clients and providers report either separator.
    private static readonly char[] PathSeparators = ['/', '\\'];

    [GeneratedRegex(@"[<>:""/\\|?*\x00-\x1f]")]
    private static partial Regex IllegalPathCharsRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex CollapseWhitespaceRegex();

    // Optional template tokens that embed connector text (e.g. "{ - Volume}"). v1 does not parse volume,
    // so these are dropped wholesale along with their connectors rather than left as literal braces.
    [GeneratedRegex(@"\{[^{}]*\bVolume\b[^{}]*\}", RegexOptions.IgnoreCase)]
    private static partial Regex OptionalVolumeTokenRegex();

    /// <summary>The prose-book, serialized-comic, and audiobook file extensions the publication importer recognizes.</summary>
    public static IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(PrimaryBookExtensions.Concat(ComicArchiveExtensions).Concat(AudiobookExtensions), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Plans the import for a set of files (paths relative to the download content root) given the book
    /// metadata and the profile path template.
    /// </summary>
    public static ImportPlan Plan(
        IReadOnlyList<string> relativeFilePaths,
        ImportTemplateContext context,
        string pathTemplate,
        BookRendition rendition = BookRendition.Ebook) {
        if (rendition == BookRendition.Audiobook) {
            // One coherent set: a download that mixes formats imports only its preferred one (M4B/M4A before MP3).
            var audio = AudiobookReleaseShape.ImportSet(relativeFilePaths
                .Select(path => new ImportCandidateFile(path, 0))
                .ToArray());
            if (audio.Count == 0) {
                return ImportPlan.Block(ImportBlockReason.NoSupportedPayload);
            }

            var audioFolder = RenderFolder(pathTemplate, context);
            return ImportPlan.For(OrderAudiobookParts(audio.Select(file => file.RelativePath).ToArray())
                .Select(part => new ImportPlanItem(
                    part.SourceRelativePath,
                    CombineRelative(audioFolder, SanitizeSegment(part.FileName))))
                .ToArray());
        }

        var supported = relativeFilePaths
            .Where(path => PrimaryBookExtensions.Contains(Path.GetExtension(path))
                || ComicArchiveExtensions.Contains(Path.GetExtension(path)))
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
            if (BookNamingTemplates.ValidateFileTemplate(pathTemplate) is { } problem) {
                throw new InvalidDataException(problem);
            }

            var chosen = PreferredPrimary(primaries);
            var target = RenderPath(pathTemplate, context, Path.GetExtension(chosen), fileNameOnly: false);
            if (BookNamingTemplates.ValidateRenderedFile(target, Path.GetExtension(chosen)) is { } renderedProblem) {
                throw new InvalidDataException(renderedProblem);
            }
            return ImportPlan.For([new ImportPlanItem(chosen, target)]);
        }

        // One or more comic archives: retain each released installment under the rendered series/volume folder.
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
            .Replace("{Series}", context.Series ?? context.Title, StringComparison.Ordinal)
            .Replace(
                "{VolumeFolder}",
                context.VolumeNumber is { } volume ? $"Volume {volume:00}" : string.Empty,
                StringComparison.Ordinal)
            .Replace("{Title}", context.Title, StringComparison.Ordinal)
            .Replace("{Year}", context.Year?.ToString() ?? string.Empty, StringComparison.Ordinal)
            .Replace(BookNamingTemplates.ExtensionToken, extension.TrimStart('.'), StringComparison.Ordinal);

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

    /// <summary>
    /// Orders an audiobook's files disc by disc, then track by track, and names them so a natural sort of the
    /// flattened audiobook folder keeps that order. Files from one folder keep their names. Files from several
    /// folders gain a "Disc NN - " prefix, numbered from the folders' disc markers (CD1, Disc 2) when every
    /// folder has a distinct one and by natural folder order otherwise, so CD1/01.mp3 and CD2/01.mp3 neither
    /// collide nor interleave.
    /// </summary>
    private static IEnumerable<(string SourceRelativePath, string FileName)> OrderAudiobookParts(IReadOnlyList<string> paths) {
        var folders = paths
            .GroupBy(FolderOf, StringComparer.OrdinalIgnoreCase)
            .Select(folder => (
                Path: folder.Key,
                Disc: DiscOf(folder.Key),
                Files: folder.OrderBy(FileNameOf, NaturalOrder.Instance).ToArray()))
            .ToArray();
        var numberedDiscs = folders.Length > 1
            && folders.All(folder => folder.Disc is not null)
            && folders.Select(folder => folder.Disc).Distinct().Count() == folders.Length;
        var ordered = numberedDiscs
            ? folders.OrderBy(folder => folder.Disc)
            : folders.OrderBy(folder => folder.Path, NaturalOrder.Instance);
        var position = 0;
        foreach (var folder in ordered) {
            position++;
            var disc = numberedDiscs ? folder.Disc!.Value : position;
            foreach (var path in folder.Files) {
                yield return (path, folders.Length > 1 ? $"Disc {disc:00} - {FileNameOf(path)}" : FileNameOf(path));
            }
        }
    }

    /// <summary>The disc number declared by the deepest folder segment that names one, or null.</summary>
    private static int? DiscOf(string folder) =>
        folder.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Reverse()
            .Select(BookReleaseTokens.ParseAudioDisc)
            .FirstOrDefault(disc => disc is not null);

    private static string FolderOf(string path) => path[..Math.Max(0, path.LastIndexOfAny(PathSeparators))];

    private static string FileNameOf(string path) => path[(path.LastIndexOfAny(PathSeparators) + 1)..];

    /// <summary>Compares names with embedded numbers by value, so "2" sorts before "10".</summary>
    private sealed class NaturalOrder : IComparer<string> {
        public static readonly NaturalOrder Instance = new();

        public int Compare(string? x, string? y) {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var ix = 0;
            var iy = 0;
            while (ix < x.Length && iy < y.Length) {
                if (char.IsDigit(x[ix]) && char.IsDigit(y[iy])) {
                    var startX = ix;
                    var startY = iy;
                    while (ix < x.Length && char.IsDigit(x[ix])) ix++;
                    while (iy < y.Length && char.IsDigit(y[iy])) iy++;
                    var digitsX = x.AsSpan(startX, ix - startX).TrimStart('0');
                    var digitsY = y.AsSpan(startY, iy - startY).TrimStart('0');
                    var number = digitsX.Length != digitsY.Length
                        ? digitsX.Length.CompareTo(digitsY.Length)
                        : digitsX.CompareTo(digitsY, StringComparison.Ordinal);
                    if (number != 0) return number;
                    continue;
                }

                var character = char.ToUpperInvariant(x[ix]).CompareTo(char.ToUpperInvariant(y[iy]));
                if (character != 0) return character;
                ix++;
                iy++;
            }

            return x.Length.CompareTo(y.Length);
        }
    }
}
