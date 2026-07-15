using System.Globalization;

namespace Prismedia.Contracts.Media;

/// <summary>
/// Canonical subtitle format names used at sidecar, ffmpeg, persistence, and playback boundaries.
/// </summary>
public static class SubtitleFormats {
    /// <summary>SubRip sidecar format.</summary>
    public const string Srt = "srt";

    /// <summary>WebVTT sidecar and normalized storage format.</summary>
    public const string Vtt = "vtt";

    /// <summary>Advanced SubStation Alpha styled subtitle format.</summary>
    public const string Ass = "ass";

    /// <summary>SubStation Alpha styled subtitle format.</summary>
    public const string Ssa = "ssa";

    /// <summary>ffmpeg WebVTT encoder and muxer name.</summary>
    public const string WebVttCodec = "webvtt";

    /// <summary>Fallback when an external subtitle codec cannot be identified.</summary>
    public const string Unknown = "unknown";

    /// <summary>Whether the format contains ASS/SSA styling that Prismedia preserves for rendering.</summary>
    public static bool IsStyled(string? format) =>
        string.Equals(format, Ass, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(format, Ssa, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the format is supported for adjacent subtitle sidecar import.</summary>
    public static bool IsSupportedSidecar(string? format) =>
        string.Equals(format, Srt, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(format, Vtt, StringComparison.OrdinalIgnoreCase) ||
        IsStyled(format);
}

/// <summary>Canonical dotted filename extensions for supported subtitle sidecars.</summary>
public static class SubtitleFileExtensions {
    public const string Srt = ".srt";
    public const string Vtt = ".vtt";
    public const string Ass = ".ass";
    public const string Ssa = ".ssa";

    /// <summary>Case-insensitive sidecar extension allowlist.</summary>
    public static IReadOnlySet<string> Supported { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Srt, Vtt, Ass, Ssa };

    /// <summary>Returns the canonical extension for a supported subtitle format.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The supplied format is unsupported.</exception>
    public static string ForFormat(string format) => format.ToLowerInvariant() switch {
        SubtitleFormats.Srt => Srt,
        SubtitleFormats.Vtt => Vtt,
        SubtitleFormats.Ass => Ass,
        SubtitleFormats.Ssa => Ssa,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported subtitle format."),
    };
}

/// <summary>Canonical language codes used by subtitle discovery and playback.</summary>
public static class SubtitleLanguages {
    /// <summary>BCP-47 code used when a subtitle language cannot be determined.</summary>
    public const string Undetermined = "und";
}

/// <summary>Canonical stable identities for managed subtitle sources.</summary>
public static class SubtitleSourceKeys {
    /// <summary>Identity of an embedded subtitle stream by its media stream index.</summary>
    public static string EmbeddedStream(int streamIndex) =>
        $"stream:{streamIndex.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Identity used when persisting a capability-provided track by its stable id.</summary>
    public static string Capability(Guid subtitleId) => $"capability:{subtitleId:N}";

    /// <summary>Stable identity for one provider-owned subtitle file.</summary>
    public static string Provider(string providerCode, string remoteId) =>
        $"provider:{providerCode}:{remoteId}";
}
