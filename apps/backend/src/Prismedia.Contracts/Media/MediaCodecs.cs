namespace Prismedia.Contracts.Media;

/// <summary>
/// Canonical audio/video codec tokens used across playback negotiation, HLS encoding,
/// remuxing, and direct-play policy. Centralized here as the single source of truth so
/// the same spelling is reused everywhere instead of being retyped inline. Values match
/// ffprobe's canonical <c>codec_name</c> output, which is also how probed streams are
/// persisted.
/// </summary>
public static class MediaCodecs {
    // ── Video ───────────────────────────────────────────────────────────
    /// <summary>HEVC / H.265 video.</summary>
    public const string Hevc = "hevc";

    /// <summary>AVC / H.264 video.</summary>
    public const string H264 = "h264";

    /// <summary>AV1 video.</summary>
    public const string Av1 = "av1";

    /// <summary>VP9 video.</summary>
    public const string Vp9 = "vp9";

    // ── Audio ───────────────────────────────────────────────────────────
    /// <summary>AAC audio.</summary>
    public const string Aac = "aac";

    /// <summary>Dolby Digital Plus (E-AC-3) audio.</summary>
    public const string Eac3 = "eac3";

    /// <summary>Dolby Digital (AC-3) audio.</summary>
    public const string Ac3 = "ac3";

    /// <summary>MP3 audio.</summary>
    public const string Mp3 = "mp3";

    /// <summary>FLAC audio.</summary>
    public const string Flac = "flac";

    /// <summary>Opus audio.</summary>
    public const string Opus = "opus";

    /// <summary>Vorbis audio.</summary>
    public const string Vorbis = "vorbis";

    /// <summary>16-bit little-endian PCM audio.</summary>
    public const string PcmS16Le = "pcm_s16le";

    /// <summary>24-bit little-endian PCM audio.</summary>
    public const string PcmS24Le = "pcm_s24le";

    // ── Bitstream tags ──────────────────────────────────────────────────
    /// <summary>MP4 sample-entry tag for HEVC without Dolby Vision (<c>-tag:v</c>).</summary>
    public const string Hvc1Tag = "hvc1";

    // ── ffmpeg encoder names ────────────────────────────────────────────
    /// <summary>LAME MP3 encoder (<c>-c:a</c> value; distinct from the <see cref="Mp3"/> codec token).</summary>
    public const string LibMp3LameEncoder = "libmp3lame";

    /// <summary>
    /// Normalizes an external codec spelling (probe output, client capability strings)
    /// to its canonical token. Unknown tokens pass through lowercased.
    /// </summary>
    /// <param name="token">Raw codec token from an external source.</param>
    /// <returns>The canonical codec token.</returns>
    public static string Normalize(string token) {
        var value = token.Trim().ToLowerInvariant();
        return value switch {
            // prism-vocab: external — alias spellings seen in probes and client capability lists.
            "h265" or "hevc" or "h.265" => Hevc,
            "h264" or "avc" or "h.264" => H264,
            "ec-3" or "eac3" or "e-ac-3" => Eac3,
            "ac-3" or "ac3" => Ac3,
            _ => value,
        };
    }

    /// <summary>
    /// Whether the codec is a modern bandwidth-efficient video codec (HEVC, AV1, VP9),
    /// used to pick bitrate ladders and remux eligibility.
    /// </summary>
    /// <param name="codec">Codec token in any known spelling; may be null.</param>
    /// <returns><see langword="true"/> for HEVC/AV1/VP9 in any spelling.</returns>
    public static bool IsEfficientVideoCodec(string? codec) =>
        codec is not null && Normalize(codec) is Hevc or Av1 or Vp9;

    /// <summary>Whether the codec is HEVC in any known spelling.</summary>
    /// <param name="codec">Codec token in any known spelling; may be null.</param>
    /// <returns><see langword="true"/> for HEVC spellings.</returns>
    public static bool IsHevc(string? codec) =>
        codec is not null && Normalize(codec) == Hevc;
}
