using System.Text.RegularExpressions;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Detects book formats referenced in a release title. Release titles routinely name their payload
/// format (e.g. "Saga Vol 1 (2012) (Digital) (CBZ)"), which lets the decision engine reject formats the
/// pipeline cannot import. The detected formats are deliberately limited to the formats Prismedia can
/// actually import and scan (EPUB, PDF, and CBZ/ZIP image archives) — the same set the import planner
/// (<see cref="ImportPlanBuilder.SupportedExtensions"/>) and the book scanner accept — so a release the
/// engine marks acceptable on format can actually be imported.
/// </summary>
public static partial class BookFormatDetection {
    // Importable: a release naming one of these can be imported and scanned.
    [GeneratedRegex(@"\b(cbz|zip|epub|pdf)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ImportableTokenRegex();

    // Known book/comic formats Prismedia cannot currently import (no reader/scanner support).
    [GeneratedRegex(@"\b(cbr|rar|mobi|azw3?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex UnsupportedTokenRegex();

    /// <summary>
    /// Importable book formats named in <paramref name="title"/>, mapped onto Prismedia's
    /// <see cref="BookFormat"/> set. Returns an empty set when the title names no importable format token.
    /// </summary>
    public static IReadOnlySet<BookFormat> Detect(string title) {
        var formats = new HashSet<BookFormat>();
        foreach (Match match in ImportableTokenRegex().Matches(title)) {
            switch (match.Value.ToLowerInvariant()) {
                case "cbz":
                case "zip":
                    formats.Add(BookFormat.ImageArchive);
                    break;
                case "epub":
                    formats.Add(BookFormat.Epub);
                    break;
                case "pdf":
                    formats.Add(BookFormat.Pdf);
                    break;
            }
        }

        return formats;
    }

    /// <summary>
    /// True when the title names a known-but-unimportable format (CBR, RAR, MOBI, AZW/AZW3). Used to reject
    /// releases whose only declared format Prismedia cannot import, so they are never queued and downloaded
    /// only to dead-end at import.
    /// </summary>
    public static bool NamesUnsupportedFormat(string title) => UnsupportedTokenRegex().IsMatch(title);
}
