namespace Prismedia.Contracts.Media;

/// <summary>
/// Canonical media container tokens used across playback negotiation, transcoding, and
/// direct-play policy. Centralized here as the single source of truth so the same
/// spelling is reused everywhere instead of being retyped inline.
/// </summary>
public static class MediaContainers {
    /// <summary>Matroska container.</summary>
    public const string Mkv = "mkv";

    /// <summary>MP4 container family.</summary>
    public const string Mp4 = "mp4";

    /// <summary>MPEG transport stream container.</summary>
    public const string Ts = "ts";

    /// <summary>
    /// Normalizes an external container spelling (probe <c>format_name</c>, file
    /// extensions, client capability strings) to its canonical token. Unknown tokens
    /// pass through lowercased.
    /// </summary>
    /// <param name="token">Raw container token from an external source.</param>
    /// <returns>The canonical container token.</returns>
    public static string Normalize(string token) {
        var value = token.Trim().ToLowerInvariant();
        return value switch {
            // prism-vocab: external — alias spellings seen in probe output and file extensions.
            "matroska" or "mkv" or "x-matroska" => Mkv,
            "mp4" or "m4v" or "mov" or "m4a" or "qt" => Mp4,
            "ts" or "mpegts" or "m2ts" or "mts" => Ts,
            _ => value,
        };
    }
}
