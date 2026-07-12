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
public static class BookFormatDetection {
    // prism-vocab: external — release-title vocabulary, matched only here.
    private static readonly string[] ImportableFormatTokens = ["cbz", "zip", "epub", "pdf", "m4b", "m4a", "mp3"];
    private static readonly string[] UnsupportedFormatTokens = ["cbr", "rar", "mobi", "azw", "azw3"];
    private static readonly string[] RetailSourceTokens = ["retail", "official"];
    private static readonly string[] WebSourceTokens = ["web", "webrip", "webdl", "digital", "converted", "calibre"];

    /// <summary>
    /// Importable book formats named in <paramref name="title"/>, mapped onto Prismedia's
    /// <see cref="BookFormat"/> set. Returns an empty set when the title names no importable format token.
    /// </summary>
    public static IReadOnlySet<BookFormat> Detect(string title) {
        var formats = new HashSet<BookFormat>();
        foreach (var token in ReleaseTitleText.Tokens(title)) {
            if (!ImportableFormatTokens.Contains(token, StringComparer.Ordinal)) {
                continue;
            }

            switch (token) {
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
                case "m4b":
                case "m4a":
                case "mp3":
                    formats.Add(BookFormat.Audio);
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
    public static bool NamesUnsupportedFormat(string title) =>
        ReleaseTitleText.ContainsToken(title, UnsupportedFormatTokens);

    /// <summary>
    /// Detects the provenance/edition tier named in <paramref name="title"/>: retail/official tokens map to
    /// <see cref="BookSourceTier.Retail"/>, web/digital/converted tokens to <see cref="BookSourceTier.Web"/>,
    /// and an untagged title to <see cref="BookSourceTier.Unknown"/>. Retail is checked first so a title that
    /// names both ("retail web conversion") is treated as the higher tier.
    /// </summary>
    public static BookSourceTier DetectSource(string title) =>
        ReleaseTitleText.ContainsToken(title, RetailSourceTokens) ? BookSourceTier.Retail
        : ReleaseTitleText.ContainsToken(title, WebSourceTokens) ? BookSourceTier.Web
        : BookSourceTier.Unknown;

    /// <summary>
    /// The best importable format tier named in <paramref name="title"/> (e.g. a title naming both PDF and
    /// EPUB resolves to <see cref="BookFormatTier.Reflowable"/>), or <see cref="BookFormatTier.Unknown"/> when
    /// no importable format is named.
    /// </summary>
    public static BookFormatTier DetectFormatTier(string title) {
        var best = BookFormatTier.Unknown;
        foreach (var format in Detect(title)) {
            var tier = BookQualityRank.TierFor(format);
            if (tier > best) {
                best = tier;
            }
        }

        return best;
    }

    /// <summary>The composite (source, format) quality rank named in a release title.</summary>
    public static BookQualityRank DetectQuality(string title) => new(DetectSource(title), DetectFormatTier(title));

    /// <summary>
    /// The format tier of an actual file, from its extension only. Use this for files on disk — unlike
    /// <see cref="DetectFormatTier"/> (which scans free text and would match a token anywhere in a path,
    /// e.g. an "epub" folder name above a PDF), this trusts the concrete extension and nothing else.
    /// </summary>
    public static BookFormatTier FormatTierFromExtension(string path) => Path.GetExtension(path).ToLowerInvariant() switch {
        ".epub" => BookFormatTier.Reflowable,
        ".pdf" => BookFormatTier.Fixed,
        ".cbz" or ".zip" => BookFormatTier.Archive,
        ".m4b" or ".m4a" or ".mp3" => BookFormatTier.Unknown,
        _ => BookFormatTier.Unknown
    };
}
