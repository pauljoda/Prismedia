namespace Prismedia.Application.Subtitles;

/// <summary>Provider-neutral evidence used to rank a subtitle against local media.</summary>
public sealed record SubtitleMatchEvidence(
    bool HashMatched,
    bool ExternalIdMatched,
    bool EpisodeMatched,
    bool YearMatched,
    bool ReleaseMatched,
    bool TrustedUploader,
    bool AiTranslated,
    bool MachineTranslated,
    decimal? Rating,
    bool IdentityConflict = false,
    bool MultiFile = false);

/// <summary>Separate identity confidence and subtitle-quality ranking.</summary>
public sealed record SubtitleMatchAssessment(
    int MatchConfidence,
    int QualityScore,
    IReadOnlyList<string> Reasons,
    bool AutomaticEligible);

/// <summary>Deterministic ranking and automatic-download safety policy.</summary>
public static class SubtitleMatchPolicy {
    /// <summary>Ranks identity and quality independently on stable 0-100 scales.</summary>
    public static SubtitleMatchAssessment Assess(SubtitleMatchEvidence evidence, int minimumConfidence = 90) {
        var confidence = 0;
        var quality = 50;
        var reasons = new List<string>();

        if (evidence.IdentityConflict) {
            reasons.Add("Identity conflict");
        } else if (evidence.HashMatched) {
            confidence = 100;
            reasons.Add("Exact file hash");
        } else if (evidence.ExternalIdMatched && evidence.EpisodeMatched) {
            confidence = 95;
            reasons.Add("Metadata ID");
            reasons.Add("Season and episode");
        } else if (evidence.ExternalIdMatched && evidence.YearMatched) {
            confidence = 90;
            reasons.Add("Metadata ID");
            reasons.Add("Year");
        } else {
            confidence = evidence.ReleaseMatched && evidence.YearMatched ? 60 :
                evidence.YearMatched ? 40 :
                evidence.ReleaseMatched ? 30 : 10;
        }
        if (evidence.YearMatched && !reasons.Contains("Year")) {
            reasons.Add("Year");
        }
        if (evidence.ReleaseMatched) {
            reasons.Add("Release name");
            quality += 15;
        }
        if (evidence.TrustedUploader) {
            reasons.Add("Trusted uploader");
            quality += 15;
        }
        if (evidence.Rating is >= 8m) {
            reasons.Add("Highly rated");
            quality += 10;
        }
        if (evidence.AiTranslated) {
            reasons.Add("AI translated");
            quality -= 20;
        }
        if (evidence.MachineTranslated) {
            reasons.Add("Machine translated");
            quality -= 35;
        }
        if (evidence.MultiFile) {
            reasons.Add("Multiple subtitle files");
            quality -= 10;
        }

        var automaticEligible = !evidence.IdentityConflict &&
            !evidence.MultiFile &&
            confidence >= minimumConfidence &&
            (evidence.HashMatched || evidence.ExternalIdMatched && evidence.EpisodeMatched);
        return new SubtitleMatchAssessment(
            Math.Clamp(confidence, 0, 100),
            Math.Clamp(quality, 0, 100),
            reasons,
            automaticEligible);
    }
}
