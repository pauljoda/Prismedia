using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class VideoPayloadProfileValidationTests {
    private static BookAcquisitionRules Rules => new([], ["en"], 0, null, null, [], [], [], []) { Kind = EntityKind.Movie };

    [Theory]
    [InlineData(1920, 804, 1080)]
    [InlineData(3840, 1608, 2160)]
    [InlineData(1280, 536, 720)]
    [InlineData(720, 576, 480)]
    public void LetterboxCroppingPreservesResolutionTier(int width, int height, int expected) {
        Assert.Equal(expected, VideoPayloadProfileValidation.ResolutionTier(Video(width, height)));
    }

    [Fact]
    public void MeasuredResolutionMustSupportTheReleaseClaim() {
        Assert.NotNull(VideoPayloadProfileValidation.Validate(Video(1280, 720), VideoQuality.Webdl1080p.ToCode(), Rules));
        Assert.Null(VideoPayloadProfileValidation.Validate(Video(1920, 804), VideoQuality.Webdl1080p.ToCode(), Rules));
    }

    [Fact]
    public void CurrentQualityPolicyIsRecheckedBeforePlacement() {
        Assert.NotNull(VideoPayloadProfileValidation.Validate(Video(1920, 804), VideoQuality.Webdl1080p.ToCode(),
            Rules with { AllowedQualities = [VideoQuality.Webdl2160p.ToCode()] }));
    }

    [Fact]
    public void ASecondaryEnglishAudioTrackSatisfiesTheProfile() {
        Assert.Null(VideoPayloadProfileValidation.ValidateProfile(null, ["tur", "eng"], Rules));
        Assert.NotNull(VideoPayloadProfileValidation.ValidateProfile(null, ["tur", "fra"], Rules));
    }

    [Fact]
    public void UnknownAudioCannotProveALanguageMismatch() {
        Assert.Null(VideoPayloadProfileValidation.ValidateProfile(null, ["tur", null], Rules));
        Assert.Null(VideoPayloadProfileValidation.ValidateProfile(null, [SubtitleLanguages.Undetermined], Rules));
        Assert.Null(VideoPayloadProfileValidation.ValidateProfile(null, [MediaLanguageCodes.Multiple], Rules));
        Assert.Null(VideoPayloadProfileValidation.ValidateProfile(null, null, Rules));
        Assert.NotNull(VideoPayloadProfileValidation.ValidateProfile(null, [], Rules));
    }

    [Fact]
    public void VideoLanguageTagsCannotSubstituteForAudio() {
        var video = Video(1920, 804) with { Streams = [
            Stream(StreamKind.Video, "eng"), Stream(StreamKind.Audio, "tur")
        ] };
        Assert.NotNull(VideoPayloadProfileValidation.Validate(video, null, Rules));
    }

    private static VideoProbeData Video(int width, int height) => new(7200, 1000, width, height, 24, null, null, null, null, null, null);
    private static MediaStreamProbeData Stream(StreamKind kind, string language) => new(
        0, kind.ToCode(), null, language, null, null, null, null, null, null, null, false, false);
}
