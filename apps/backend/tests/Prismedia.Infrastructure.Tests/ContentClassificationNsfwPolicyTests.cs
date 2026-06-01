using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Tests;

public sealed class ContentClassificationNsfwPolicyTests {
    [Theory]
    [InlineData("R")]
    [InlineData("nc-17")]
    [InlineData("NC17")]
    [InlineData("X")]
    [InlineData("TV-MA")]
    [InlineData("18+")]
    [InlineData("R18+")]
    [InlineData("Adults Only")]
    [InlineData("Mature 17+")]
    [InlineData("pornographic")]
    [InlineData("Erotica")]
    [InlineData("Rx - Hentai")]
    public void MatureClassificationsAreNsfw(string classification) {
        Assert.True(ContentClassificationNsfwPolicy.IsMature(classification));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("G")]
    [InlineData("PG")]
    [InlineData("PG-13")]
    [InlineData("TV-14")]
    [InlineData("TV-PG")]
    [InlineData("Teen")]
    [InlineData("E")]
    [InlineData("safe")]
    [InlineData("suggestive")]
    public void NonMatureClassificationsAreNotNsfw(string? classification) {
        Assert.False(ContentClassificationNsfwPolicy.IsMature(classification));
    }
}
