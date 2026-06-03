using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Tests;

public sealed class IdentifyQueryNormalizerTests {
    [Theory]
    [InlineData("The Grinch (1960)", "The Grinch")]
    [InlineData("The Grinch [1080p]", "The Grinch")]
    [InlineData("The Grinch {WEBRip}", "The Grinch")]
    [InlineData("Pokémon (2019) [1080p]", "Pokemon")]
    [InlineData("Amélie", "Amelie")]
    [InlineData("Spaced   Out", "Spaced Out")]
    [InlineData("  Trimmed  ", "Trimmed")]
    public void NormalizeForSearch_StripsGroupingTokensAndFoldsDiacritics(string input, string expected) {
        Assert.Equal(expected, IdentifyQueryNormalizer.NormalizeForSearch(input));
    }

    [Fact]
    public void NormalizeForSearch_KeepsOriginalWhenCleaningEmptiesTheQuery() {
        Assert.Equal("(1960)", IdentifyQueryNormalizer.NormalizeForSearch("(1960)"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeForSearch_PassesThroughNullOrBlank(string? input) {
        Assert.Equal(input, IdentifyQueryNormalizer.NormalizeForSearch(input));
    }
}
