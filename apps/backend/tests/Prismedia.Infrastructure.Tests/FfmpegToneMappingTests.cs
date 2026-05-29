using Prismedia.Application.Videos;
using Prismedia.Infrastructure.Media.Processing;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Regression guard for the HDR tone-mapping consolidation. The HLS streaming path and the thumbnail
/// path must classify a stream identically (via <see cref="VideoPlaybackRangePolicy" />) and build the
/// same filter chain (via <see cref="FfmpegToneMapping" />). Previously the thumbnail path re-implemented
/// detection inline and missed Dolby Vision profile 8 sources, producing wrong thumbnails for files that
/// streamed correctly.
/// </summary>
public sealed class FfmpegToneMappingTests {
    // (colorTransfer, colorPrimaries, dvProfile, rpuPresentFlag, hdr10PlusPresentFlag, expectedRangeType)
    public static TheoryData<string?, string?, int?, bool?, bool, string> ClassificationCases() => new() {
        { null, null, null, null, false, "SDR" },
        { "bt709", "bt709", null, null, false, "SDR" },
        { "smpte2084", "bt2020", null, null, false, "HDR10" },
        { null, "bt2020", null, null, false, "HDR10" },
        { "arib-std-b67", "bt2020", null, null, false, "HLG" },
        { "smpte2084", "bt2020", null, null, true, "HDR10Plus" },
        // Dolby Vision profile 5 — classic single-layer DoVi.
        { null, null, 5, true, false, "DOVI" },
        // Dolby Vision profile 8 with an HDR10-compatible base layer and NO standard HDR color tags.
        // This is the case the old thumbnail detector missed (it only looked at color tags + DV P5/compat-0).
        { null, null, 8, null, false, "DOVI" },
    };

    [Theory]
    [MemberData(nameof(ClassificationCases))]
    public void ClassifiesDynamicRangeFromRawFields(
        string? colorTransfer,
        string? colorPrimaries,
        int? dvProfile,
        bool? rpuPresentFlag,
        bool hdr10PlusPresentFlag,
        string expectedRangeType) {
        var range = VideoPlaybackRangePolicy.Classify(
            colorTransfer, colorPrimaries, dvProfile, rpuPresentFlag, hdr10PlusPresentFlag);

        Assert.Equal(expectedRangeType, range.VideoRangeType);
    }

    [Fact]
    public void DolbyVisionProfile8IsDetectedAsNeedingToneMapping() {
        // The exact regression: a DoVi P8 file with no smpte2084/bt2020 color tags must not classify as SDR.
        var range = VideoPlaybackRangePolicy.Classify(
            colorTransfer: null,
            colorPrimaries: null,
            dvProfile: 8,
            rpuPresentFlag: null,
            hdr10PlusPresentFlag: false);

        Assert.NotEqual("SDR", range.VideoRangeType);
    }

    [Theory]
    [InlineData(5, null)]    // Dolby Vision profile 5
    [InlineData(null, 0)]    // base layer with no HDR-compatible signal
    [InlineData(8, 0)]       // profile 8, non-compatible base layer
    public void UsesDolbyVisionChainForDolbyVisionSources(int? dvProfile, int? dvBlSignalCompatibilityId) {
        var filter = FfmpegToneMapping.BuildFilter("smpte2084", dvProfile, dvBlSignalCompatibilityId, "scale=960:-2");

        Assert.Contains("tonemapx=tonemap=bt2390", filter);
        Assert.DoesNotContain("tonemap=hable", filter);
    }

    [Theory]
    [InlineData(null, null)]  // plain HDR10
    [InlineData(8, 1)]        // profile 8 with HDR10-compatible base layer -> HDR10 chain
    public void UsesHdr10ChainForNonDolbyVisionSources(int? dvProfile, int? dvBlSignalCompatibilityId) {
        var filter = FfmpegToneMapping.BuildFilter("smpte2084", dvProfile, dvBlSignalCompatibilityId, "scale=960:-2");

        Assert.Contains("tonemap=tonemap=hable", filter);
        Assert.DoesNotContain("tonemapx", filter);
    }

    [Fact]
    public void PreservesHlgInputTransferParameters() {
        var filter = FfmpegToneMapping.BuildFilter("arib-std-b67", null, null, "scale=960:-2");

        Assert.Contains("color_trc=arib-std-b67", filter);
    }

    [Fact]
    public void DefaultsInputTransferToPqWhenNotHlg() {
        var filter = FfmpegToneMapping.BuildFilter("smpte2084", null, null, "scale=960:-2");

        Assert.Contains("color_trc=smpte2084", filter);
    }

    [Fact]
    public void AppendsTrailingFormatOnlyWhenRequested() {
        var withFormat = FfmpegToneMapping.BuildFilter("smpte2084", null, null, "scale=960:-2", trailingFormat: "yuv420p");
        var withoutFormat = FfmpegToneMapping.BuildFilter("smpte2084", null, null, "scale=960:-2");

        Assert.EndsWith(",format=yuv420p", withFormat);
        Assert.EndsWith("scale=960:-2", withoutFormat);
    }
}
