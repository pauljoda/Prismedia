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
        // Music metadata providers commonly append "(score)" to distinguish the score section of a
        // soundtrack, while the matching release files omit that catalog-only role marker. It does not
        // distinguish the recording itself, unlike version labels such as demo, reprise, or karaoke.
        var withoutCatalogRole = TrailingScoreRole().Replace(withoutBitrate, string.Empty);
        var withoutTrackNumber = LeadingTrackNumber().Replace(withoutCatalogRole, string.Empty);
        var filesystemSafe = ImportPlanBuilder.SanitizeSegment(withoutTrackNumber);
        return ReleaseTitleText.Normalize(filesystemSafe);
    }

    /// <summary>
    /// Whether a scanned filename safely identifies one metadata-authored track title. Exact normalized
    /// equality is preferred; a numbered filename may also carry a leading artist credit before the exact
    /// title, which is common in Soulseek folders and remains unambiguous only when the caller finds one match.
    /// Unnumbered artist prefixes require an exact match to the supplied artist.
    /// </summary>
    public static bool MatchesMetadataTitle(string? metadataTitle, string? scannedTitle, string? artist = null) {
        var metadata = ReleaseTitleText.Tokens(Normalize(metadataTitle));
        var scanned = ReleaseTitleText.Tokens(Normalize(scannedTitle));
        if (metadata.Count == 0 || scanned.Count < metadata.Count) {
            return false;
        }
        if (metadata.SequenceEqual(scanned, StringComparer.Ordinal)) {
            return true;
        }
        var numbered = LeadingTrackNumber().IsMatch(scannedTitle ?? string.Empty);
        if (!numbered && string.IsNullOrWhiteSpace(artist)) {
            return false;
        }

        var withoutBitrate = TrailingBitrate().Replace(scannedTitle!, string.Empty);
        var withoutTrackNumber = LeadingTrackNumber().Replace(withoutBitrate, string.Empty);
        var artistAndTitle = ArtistTitleSeparator().Split(withoutTrackNumber, 2);
        if (artistAndTitle.Length < 2) {
            return false;
        }
        // Standalone Soulseek files often omit album numbering. Accept their prefix only when it
        // matches the requested artist, so a similarly named song by another artist cannot bind here.
        if (!numbered && !ReleaseTitleText.Tokens(Normalize(artist))
                .SequenceEqual(ReleaseTitleText.Tokens(Normalize(artistAndTitle[0])), StringComparer.Ordinal)) {
            return false;
        }

        var separatedTitle = ReleaseTitleText.Tokens(Normalize(artistAndTitle[^1]));
        return metadata.SequenceEqual(separatedTitle, StringComparer.Ordinal);
    }

    /// <summary>
    /// Reads the one-based track number decorating the start of a release filename, or null when the
    /// filename has no leading number. Provider track positions are zero-based, so callers compare this
    /// value with <c>position + 1</c>.
    /// </summary>
    public static int? ReadLeadingTrackNumber(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        var match = LeadingTrackNumber().Match(value);
        return match.Success && int.TryParse(match.Groups["number"].Value, out var number)
            ? number
            : null;
    }

    [GeneratedRegex("[\"“”„‟«»‹›]", RegexOptions.CultureInvariant)]
    private static partial Regex QuotationMarks();

    [GeneratedRegex(@"\s*\[\s*\d+(?:\.\d+)?\s*kbps\s*\]\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrailingBitrate();

    [GeneratedRegex(@"\s*[\[(]\s*score\s*[\])]\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrailingScoreRole();

    [GeneratedRegex(@"^\s*(?<number>\d{1,3})(?:\s*[-._]\s*|\s+)", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingTrackNumber();

    [GeneratedRegex(@"\s+(?:-|–|—)\s+", RegexOptions.CultureInvariant)]
    private static partial Regex ArtistTitleSeparator();
}
