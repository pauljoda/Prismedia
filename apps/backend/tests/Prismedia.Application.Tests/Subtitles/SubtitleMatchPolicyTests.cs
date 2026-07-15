using Prismedia.Application.Subtitles;

namespace Prismedia.Application.Tests.Subtitles;

public sealed class SubtitleMatchPolicyTests {
    [Fact]
    public void ExactHashProducesAutomaticQualityScore() {
        var evidence = new SubtitleMatchEvidence(
            HashMatched: true,
            ExternalIdMatched: true,
            EpisodeMatched: false,
            YearMatched: true,
            ReleaseMatched: false,
            TrustedUploader: false,
            AiTranslated: false,
            MachineTranslated: false,
            Rating: 8.5m);

        var assessment = SubtitleMatchPolicy.Assess(evidence);

        Assert.Equal(100, assessment.MatchConfidence);
        Assert.Contains("Exact file hash", assessment.Reasons);
        Assert.True(assessment.AutomaticEligible);
    }

    [Fact]
    public void TitleAndReleaseEvidenceNeverAutoDownloadsWithoutStrongIdentity() {
        var evidence = new SubtitleMatchEvidence(
            HashMatched: false,
            ExternalIdMatched: false,
            EpisodeMatched: false,
            YearMatched: true,
            ReleaseMatched: true,
            TrustedUploader: true,
            AiTranslated: false,
            MachineTranslated: false,
            Rating: 9m);

        var assessment = SubtitleMatchPolicy.Assess(evidence, minimumConfidence: 0);

        Assert.False(assessment.AutomaticEligible);
    }

    [Fact]
    public void EpisodeIdentityCanAutoDownloadWithoutHash() {
        var evidence = new SubtitleMatchEvidence(
            HashMatched: false,
            ExternalIdMatched: true,
            EpisodeMatched: true,
            YearMatched: true,
            ReleaseMatched: true,
            TrustedUploader: true,
            AiTranslated: false,
            MachineTranslated: false,
            Rating: 9m);

        var assessment = SubtitleMatchPolicy.Assess(evidence);

        Assert.Equal(95, assessment.MatchConfidence);
        Assert.True(assessment.AutomaticEligible);
    }

    [Fact]
    public void MachineTranslationReceivesLargerPenaltyThanAiTranslation() {
        var ai = SubtitleMatchPolicy.Assess(new SubtitleMatchEvidence(
            true, false, false, false, false, false, true, false, null)).QualityScore;
        var machine = SubtitleMatchPolicy.Assess(new SubtitleMatchEvidence(
            true, false, false, false, false, false, false, true, null)).QualityScore;

        Assert.Equal(30, ai);
        Assert.Equal(15, machine);
    }
}
