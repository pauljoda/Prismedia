namespace Prismedia.Contracts.Media;

/// <summary>
/// Canonical MIME type strings used across media serving, transcoding, and storage.
/// Centralized here as the single source of truth so the same spelling is reused
/// everywhere instead of being retyped inline.
/// </summary>
/// <remarks>
/// Extension-to-MIME resolution remains context-specific (for example <c>.ogg</c> is
/// <see cref="VideoOgg"/> for video sources but <see cref="AudioOgg"/> for audio
/// sources), so callers keep their own mapping switches and reference these constants
/// for the values rather than sharing a single resolver.
/// </remarks>
public static class MediaContentTypes {
    /// <summary>Fallback for unknown content.</summary>
    public const string OctetStream = "application/octet-stream";

    // ── Images ──────────────────────────────────────────────────────────
    /// <summary>JPEG image.</summary>
    public const string ImageJpeg = "image/jpeg";

    /// <summary>PNG image.</summary>
    public const string ImagePng = "image/png";

    /// <summary>WebP image.</summary>
    public const string ImageWebp = "image/webp";

    /// <summary>GIF image.</summary>
    public const string ImageGif = "image/gif";

    /// <summary>AVIF image.</summary>
    public const string ImageAvif = "image/avif";

    // ── Video ───────────────────────────────────────────────────────────
    /// <summary>MP4 / fragmented MP4 video.</summary>
    public const string VideoMp4 = "video/mp4";

    /// <summary>WebM video.</summary>
    public const string VideoWebm = "video/webm";

    /// <summary>Ogg video.</summary>
    public const string VideoOgg = "video/ogg";

    /// <summary>QuickTime video.</summary>
    public const string VideoQuicktime = "video/quicktime";

    /// <summary>Matroska video.</summary>
    public const string VideoMatroska = "video/x-matroska";

    /// <summary>AVI video.</summary>
    public const string VideoAvi = "video/x-msvideo";

    /// <summary>Windows Media video.</summary>
    public const string VideoWmv = "video/x-ms-wmv";

    /// <summary>Flash video.</summary>
    public const string VideoFlv = "video/x-flv";

    /// <summary>MPEG-2 transport stream (HLS segments).</summary>
    public const string VideoMp2t = "video/mp2t";

    // ── Audio ───────────────────────────────────────────────────────────
    /// <summary>MP3 audio.</summary>
    public const string AudioMpeg = "audio/mpeg";

    /// <summary>MP4/AAC audio.</summary>
    public const string AudioMp4 = "audio/mp4";

    /// <summary>Ogg audio.</summary>
    public const string AudioOgg = "audio/ogg";

    /// <summary>Opus audio.</summary>
    public const string AudioOpus = "audio/opus";

    /// <summary>FLAC audio.</summary>
    public const string AudioFlac = "audio/flac";

    /// <summary>WAV audio.</summary>
    public const string AudioWav = "audio/wav";

    /// <summary>AIFF audio.</summary>
    public const string AudioAiff = "audio/aiff";

    /// <summary>Windows Media audio.</summary>
    public const string AudioWma = "audio/x-ms-wma";

    // ── Text / streaming / markup ───────────────────────────────────────
    /// <summary>WebVTT subtitles.</summary>
    public const string Vtt = "text/vtt";

    /// <summary>WebVTT subtitles with UTF-8 charset.</summary>
    public const string VttUtf8 = "text/vtt; charset=utf-8";

    /// <summary>SubStation Alpha subtitles with UTF-8 charset.</summary>
    public const string SsaUtf8 = "text/x-ssa; charset=utf-8";

    /// <summary>HLS playlist (Apple MPEG-URL).</summary>
    public const string HlsPlaylist = "application/vnd.apple.mpegurl";

    /// <summary>HTML markup.</summary>
    public const string Html = "text/html";

    /// <summary>HTML markup with UTF-8 charset.</summary>
    public const string HtmlUtf8 = "text/html; charset=utf-8";

    // ── Books ───────────────────────────────────────────────────────────
    /// <summary>EPUB book container.</summary>
    public const string Epub = "application/epub+zip";

    /// <summary>PDF document.</summary>
    public const string Pdf = "application/pdf";
}
