using Prismedia.Application.Jobs.Ports;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>Checks measured video facts before automatic placement or replacement; ambiguous evidence remains reviewable.</summary>
public static class VideoPayloadProfileValidation {
    /// <summary>Resolution tier measured from the long edge, allowing the normal letterbox crop of theatrical releases.</summary>
    public static int? ResolutionTier(VideoProbeData? video) => video is { Width: > 0, Height: > 0 }
        ? Math.Max(video.Width.Value, video.Height.Value) switch {
            >= 3_000 => 2160, >= 1_600 => 1080, >= 1_100 => 720, _ => 480
        } : null;

    /// <summary>Returns an explanation when a measured movie contradicts its claimed resolution or current profile.</summary>
    public static string? Validate(VideoProbeData video, string? claimedQuality, BookAcquisitionRules rules) {
        if (ResolutionTier(video) is { } measured
            && MediaQualityLadder.VideoResolutionTierOf(claimedQuality) is { } claimed && measured < claimed) {
            return "The movie file's measured resolution is lower than the release's claimed quality. The download was preserved for review.";
        }
        return ValidateProfile(claimedQuality, AudioLanguages(video), rules);
    }

    /// <summary>Returns audio-stream language evidence, preserving unknown tags and an unknown stream inventory.</summary>
    public static IReadOnlyList<string?>? AudioLanguages(VideoProbeData video) => video.Streams?
        .Where(stream => stream.Type == StreamKind.Audio.ToCode()).Select(stream => stream.Language).ToArray();

    /// <summary>
    /// Checks a release quality against the current profile and known audio against preferred languages.
    /// Untagged or undetermined audio cannot prove a language mismatch; an explicit manual choice bypasses this gate.
    /// </summary>
    public static string? ValidateProfile(string? claimedQuality, IReadOnlyList<string?>? audioLanguages, BookAcquisitionRules rules) {
        if (claimedQuality is not null && rules.AllowedQualities.Count > 0
            && !rules.AllowedQualities.Contains(claimedQuality)) {
            return "The downloaded video's quality is no longer allowed by the current profile. The download was preserved for review.";
        }
        if (audioLanguages is null || rules.PreferredLanguages.Count == 0) {
            return null;
        }
        if (audioLanguages.Count == 0) {
            return "The downloaded video has no detected audio stream. The download was preserved for review.";
        }
        if (audioLanguages.Any(language => string.IsNullOrWhiteSpace(language)
            || string.Equals(language, SubtitleLanguages.Undetermined, StringComparison.OrdinalIgnoreCase)
            || string.Equals(language, MediaLanguageCodes.Multiple, StringComparison.OrdinalIgnoreCase))) {
            return null;
        }
        var known = audioLanguages.Select(language => ReleaseLanguageDetection.Canonicalize(language!)).ToHashSet();
        return rules.PreferredLanguages.Any(language => known.Contains(ReleaseLanguageDetection.Canonicalize(language)))
            ? null : "None of the downloaded video's known audio languages matches the current profile. The download was preserved for review.";
    }
}
