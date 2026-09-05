using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;

namespace Prismedia.Domain.Tests;

public sealed class MediaResolutionPolicyTests {
    [Theory]
    [InlineData(7680, 3200, MediaResolutionTier.Uhd8K)]
    [InlineData(3840, 1600, MediaResolutionTier.Uhd4K)]
    [InlineData(2560, 1080, MediaResolutionTier.Qhd)]
    [InlineData(1920, 808, MediaResolutionTier.FullHd)]
    [InlineData(1440, 1080, MediaResolutionTier.FullHd)]
    [InlineData(1280, 544, MediaResolutionTier.Hd)]
    [InlineData(720, 576, MediaResolutionTier.Standard480)]
    [InlineData(320, 240, MediaResolutionTier.Sd)]
    [InlineData(null, 1080, MediaResolutionTier.FullHd)]
    [InlineData(1920, null, MediaResolutionTier.FullHd)]
    public void ClassifiesCroppedAndPartialDimensions(int? width, int? height, MediaResolutionTier expected) =>
        Assert.Equal(expected, MediaResolutionPolicy.Classify(width, height));

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, 0)]
    [InlineData(-1, null)]
    public void UnknownDimensionsDoNotBecomeStandardDefinition(int? width, int? height) =>
        Assert.Null(MediaResolutionPolicy.Classify(width, height));

    [Fact]
    public void ThresholdsCoverEveryTierInDescendingOrder() {
        Assert.Equal(Enum.GetValues<MediaResolutionTier>(), MediaResolutionPolicy.Tiers.Select(tier => tier.Tier));
        foreach (var tier in MediaResolutionPolicy.Tiers) {
            Assert.Equal(tier.Tier, MediaResolutionPolicy.Classify(tier.MinimumWidth, null));
            Assert.Equal(tier.Tier, MediaResolutionPolicy.Classify(null, tier.MinimumHeight));
        }
    }
}
