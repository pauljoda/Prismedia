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

        var withoutBitrate = TrailingBitrate().Replace(value.Trim(), string.Empty);
        var withoutTrackNumber = LeadingTrackNumber().Replace(withoutBitrate, string.Empty);
        return ReleaseTitleText.Normalize(withoutTrackNumber);
    }

    [GeneratedRegex(@"\s*\[\s*\d+(?:\.\d+)?\s*kbps\s*\]\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrailingBitrate();

    [GeneratedRegex(@"^\s*\d{1,3}(?:\s*[-._]\s*|\s+)", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingTrackNumber();
}
