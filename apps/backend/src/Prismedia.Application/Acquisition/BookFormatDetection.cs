using System.Text.RegularExpressions;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Detects book formats referenced in a release title. Release titles routinely name their payload
/// format (e.g. "Saga Vol 1 (2012) (Digital) (CBR)"), which lets the decision engine reject formats
/// a profile disallows without guessing about titles that name no format at all.
/// </summary>
public static partial class BookFormatDetection {
    [GeneratedRegex(@"\b(cbz|cbr|epub|pdf|mobi|azw3?|zip|rar)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FormatTokenRegex();

    /// <summary>
    /// Book formats named in <paramref name="title"/>, mapped onto Prismedia's <see cref="BookFormat"/> set.
    /// Returns an empty set when the title names no recognizable format token.
    /// </summary>
    public static IReadOnlySet<BookFormat> Detect(string title) {
        var formats = new HashSet<BookFormat>();
        foreach (Match match in FormatTokenRegex().Matches(title)) {
            switch (match.Value.ToLowerInvariant()) {
                case "cbz":
                case "cbr":
                case "zip":
                case "rar":
                    formats.Add(BookFormat.ImageArchive);
                    break;
                case "epub":
                case "mobi":
                case "azw":
                case "azw3":
                    formats.Add(BookFormat.Epub);
                    break;
                case "pdf":
                    formats.Add(BookFormat.Pdf);
                    break;
            }
        }

        return formats;
    }
}
