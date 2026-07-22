using System.Text.RegularExpressions;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Normalizes scanned audio filenames for strict matching to metadata-authored track titles.
/// Track-number prefixes and scanner-added bitrate suffixes are presentation decorations rather
/// than title identity, so imports may reuse the stable wanted track without copying filename noise.
/// </summary>
public static partial class AudioTrackTitleText {
    /// <summary>Returns a canonical title suitable for exact wanted-track reconciliation.</summary>
    public static string Normalize(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        var withoutQuotes = QuotationMarks().Replace(value.Trim(), string.Empty);
        var withoutBitrate = TrailingBitrate().Replace(withoutQuotes, string.Empty);
        var withoutTrackNumber = LeadingTrackNumber().Replace(withoutBitrate, string.Empty);
        var filesystemSafe = ImportPlanBuilder.SanitizeSegment(withoutTrackNumber);
        return ReleaseTitleText.Normalize(filesystemSafe);
    }

    /// <summary>
    /// Whether a scanned filename safely identifies one metadata-authored track title. Exact normalized
    /// equality is preferred; a numbered filename may also carry a leading artist credit before the exact
    /// title, which is common in Soulseek folders and remains unambiguous only when the caller finds one match.
    /// </summary>
    public static bool MatchesMetadataTitle(string? metadataTitle, string? scannedTitle) {
        var metadata = ReleaseTitleText.Tokens(Normalize(metadataTitle));
        var scanned = ReleaseTitleText.Tokens(Normalize(scannedTitle));
        if (metadata.Count == 0 || scanned.Count < metadata.Count) {
            return false;
        }
        if (metadata.SequenceEqual(scanned, StringComparer.Ordinal)) {
            return true;
        }
        if (!LeadingTrackNumber().IsMatch(scannedTitle ?? string.Empty)) {
            return false;
        }

        var withoutBitrate = TrailingBitrate().Replace(scannedTitle!, string.Empty);
        var withoutTrackNumber = LeadingTrackNumber().Replace(withoutBitrate, string.Empty);
        var artistAndTitle = ArtistTitleSeparator().Split(withoutTrackNumber, 2);
        if (artistAndTitle.Length < 2) {
            return false;
        }

        var separatedTitle = ReleaseTitleText.Tokens(Normalize(artistAndTitle[^1]));
        return metadata.SequenceEqual(separatedTitle, StringComparer.Ordinal);
    }

    [GeneratedRegex("[\"“”„‟«»‹›]", RegexOptions.CultureInvariant)]
    private static partial Regex QuotationMarks();

    [GeneratedRegex(@"\s*\[\s*\d+(?:\.\d+)?\s*kbps\s*\]\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrailingBitrate();

    [GeneratedRegex(@"^\s*\d{1,3}(?:\s*[-._]\s*|\s+)", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingTrackNumber();

    [GeneratedRegex(@"\s+(?:-|–|—)\s+", RegexOptions.CultureInvariant)]
    private static partial Regex ArtistTitleSeparator();
}
