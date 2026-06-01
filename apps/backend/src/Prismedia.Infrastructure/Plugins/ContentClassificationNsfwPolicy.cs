namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Decides whether a provider-supplied content classification (rating or certification) describes
/// adult or mature content that should be treated as NSFW. This lets non-NSFW providers such as
/// TMDB and MangaDex flag individual proposals when the upstream rating is mature, R, 18+, and so on,
/// without marking the entire provider NSFW.
/// </summary>
public static class ContentClassificationNsfwPolicy {
    /// <summary>
    /// Exact rating/certification codes that always indicate mature content, compared after
    /// normalizing case and stripping spaces, dots, and hyphens (so "NC-17" and "nc17" match).
    /// </summary>
    private static readonly HashSet<string> MatureCodes = new(StringComparer.OrdinalIgnoreCase) {
        "R", "NC17", "X", "XXX", "AO", "TVMA", "MA", "MA15", "MA15+",
        "R18", "R18+", "18", "18+", "R+", "RX", "X18", "X18+"
    };

    /// <summary>
    /// Substrings that indicate mature content wherever they appear in a classification label,
    /// covering descriptive ratings such as "Adults Only", "Mature 17+", and MangaDex's
    /// "erotica"/"pornographic" content ratings.
    /// </summary>
    private static readonly string[] MatureKeywords = {
        "mature", "adult", "explicit", "porn", "hentai", "erotic", "nsfw", "nudity", "sexual"
    };

    /// <summary>
    /// Returns true when the classification describes adult or mature content.
    /// </summary>
    /// <param name="classification">Provider rating or certification, such as "R", "TV-MA", or "pornographic".</param>
    public static bool IsMature(string? classification) {
        if (string.IsNullOrWhiteSpace(classification)) {
            return false;
        }

        var lowered = classification.ToLowerInvariant();
        foreach (var keyword in MatureKeywords) {
            if (lowered.Contains(keyword, StringComparison.Ordinal)) {
                return true;
            }
        }

        var normalized = Normalize(classification);
        return normalized.Length > 0 && MatureCodes.Contains(normalized);
    }

    private static string Normalize(string value) {
        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;
        foreach (var ch in value) {
            if (ch is ' ' or '.' or '-' or '_' or '/') {
                continue;
            }

            buffer[length++] = char.ToUpperInvariant(ch);
        }

        return new string(buffer[..length]);
    }
}
