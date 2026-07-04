using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Detects a release's video quality (combined source × resolution ladder position) from its title
/// tokens. Pure title truth — no payload probing — mirroring the other release-token detectors.
/// </summary>
public static class VideoQualityDetection {
    /// <summary>The ladder position a release title declares, or <see cref="VideoQuality.Unknown"/>.</summary>
    public static VideoQuality Detect(string title) {
        var source = DetectSource(title);
        var resolution = DetectResolution(title);
        return (source, resolution) switch {
            (Source.Remux, Resolution.R2160) => VideoQuality.Remux2160p,
            (Source.Remux, _) => VideoQuality.Remux1080p,
            (Source.Bluray, Resolution.R2160) => VideoQuality.Bluray2160p,
            (Source.Bluray, Resolution.R720) => VideoQuality.Bluray720p,
            (Source.Bluray, _) => VideoQuality.Bluray1080p,
            (Source.Webdl, Resolution.R2160) => VideoQuality.Webdl2160p,
            (Source.Webdl, Resolution.R720) => VideoQuality.Webdl720p,
            (Source.Webdl, Resolution.R1080) => VideoQuality.Webdl1080p,
            (Source.Webrip, Resolution.R2160) => VideoQuality.Webrip2160p,
            (Source.Webrip, Resolution.R720) => VideoQuality.Webrip720p,
            (Source.Webrip, Resolution.R1080) => VideoQuality.Webrip1080p,
            (Source.Hdtv, Resolution.R2160) => VideoQuality.Hdtv2160p,
            (Source.Hdtv, Resolution.R720) => VideoQuality.Hdtv720p,
            (Source.Hdtv, Resolution.R1080) => VideoQuality.Hdtv1080p,
            (Source.Dvd, _) => VideoQuality.Dvd,
            // A bare resolution with no source token still places on the ladder (as its weakest source).
            (Source.None, Resolution.R2160) => VideoQuality.Hdtv2160p,
            (Source.None, Resolution.R1080) => VideoQuality.Hdtv1080p,
            (Source.None, Resolution.R720) => VideoQuality.Hdtv720p,
            (Source.None, Resolution.R480) => VideoQuality.Sdtv,
            (Source.Webdl or Source.Webrip or Source.Hdtv, Resolution.R480) => VideoQuality.Sdtv,
            _ => VideoQuality.Unknown
        };
    }

    private enum Source { None, Dvd, Hdtv, Webrip, Webdl, Bluray, Remux }
    private enum Resolution { None, R480, R720, R1080, R2160 }

    private static Source DetectSource(string title) =>
        Has(title, "remux") ? Source.Remux :
        Has(title, "bluray", "blu-ray", "bdrip", "brrip", "bd25", "bd50") ? Source.Bluray :
        Has(title, "web-dl", "webdl", "web dl") ? Source.Webdl :
        Has(title, "webrip", "web-rip") ? Source.Webrip :
        Has(title, "web") ? Source.Webdl :
        Has(title, "hdtv", "pdtv") ? Source.Hdtv :
        Has(title, "dvdrip", "dvd") ? Source.Dvd :
        Source.None;

    private static Resolution DetectResolution(string title) =>
        Has(title, "2160p", "4k", "uhd") ? Resolution.R2160 :
        Has(title, "1080p", "1080i") ? Resolution.R1080 :
        Has(title, "720p") ? Resolution.R720 :
        Has(title, "480p", "480i", "sdtv") ? Resolution.R480 :
        Resolution.None;

    private static bool Has(string title, params string[] tokens) =>
        tokens.Any(token => title.Contains(token, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Detects a music release's codec quality tier from its title tokens.</summary>
public static class AudioQualityDetection {
    public static AudioQuality Detect(string title) =>
        Has(title, "24bit", "24-bit", "24/96", "24/192", "hi-res", "hires") ? AudioQuality.LosslessHiRes :
        Has(title, "flac", "alac", "lossless") ? AudioQuality.Lossless :
        Has(title, "320", "v0") ? AudioQuality.LossyHigh :
        Has(title, "mp3", "aac", "opus", "ogg", "256", "192") ? AudioQuality.Lossy :
        AudioQuality.Unknown;

    private static bool Has(string title, params string[] tokens) =>
        tokens.Any(token => title.Contains(token, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Kind-aware helpers over the media quality ladders: which catalog a kind speaks, code↔position
/// translation, and the allowed/cutoff comparisons the specifications and the upgrade loop share.
/// </summary>
public static class MediaQualityLadder {
    /// <summary>True for kinds ranked on the video ladder (movies and both TV units).</summary>
    public static bool IsVideoKind(EntityKind kind) =>
        kind is EntityKind.Movie or EntityKind.Video or EntityKind.VideoSeason or EntityKind.VideoSeries;

    /// <summary>True for kinds ranked on the audio ladder.</summary>
    public static bool IsAudioKind(EntityKind kind) =>
        kind is EntityKind.AudioLibrary or EntityKind.AudioTrack or EntityKind.MusicArtist;

    /// <summary>
    /// True for the media kinds whose owned copy is a single file the upgrade loop can atomically swap in
    /// place — a movie and a single TV episode (<see cref="EntityKind.Video"/>). Multi-file units
    /// (<see cref="EntityKind.VideoSeason"/> season packs, <see cref="EntityKind.AudioLibrary"/> albums)
    /// fulfill on import instead: a single-file replace can't safely swap a whole pack. Books have their
    /// own upgrade path (source/format tiers) and are never routed here.
    /// </summary>
    public static bool IsUpgradeCapableKind(EntityKind kind) =>
        kind is EntityKind.Movie or EntityKind.Video;

    /// <summary>A release title's ladder position for a kind, as (code, ordinal). Ordinal 0 = unknown.</summary>
    public static (string Code, int Position) Detect(EntityKind kind, string title) {
        if (IsVideoKind(kind)) {
            var quality = VideoQualityDetection.Detect(title);
            return (quality.ToCode(), (int)quality);
        }

        if (IsAudioKind(kind)) {
            var quality = AudioQualityDetection.Detect(title);
            return (quality.ToCode(), (int)quality);
        }

        return (VideoQuality.Unknown.ToCode(), 0);
    }

    /// <summary>The ladder ordinal of a quality code for a kind, or 0 when the code is unknown/foreign.</summary>
    public static int PositionOf(EntityKind kind, string? code) {
        if (string.IsNullOrWhiteSpace(code)) {
            return 0;
        }

        if (IsVideoKind(kind)) {
            return TryDecode<VideoQuality>(code, out var video) ? (int)video : 0;
        }

        if (IsAudioKind(kind)) {
            return TryDecode<AudioQuality>(code, out var audio) ? (int)audio : 0;
        }

        return 0;
    }

    private static bool TryDecode<TEnum>(string code, out TEnum value) where TEnum : struct, Enum {
        foreach (var candidate in Enum.GetValues<TEnum>()) {
            if (string.Equals(candidate.ToCode(), code, StringComparison.Ordinal)) {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }
}
