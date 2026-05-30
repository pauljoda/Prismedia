using Prismedia.Application.Security;

namespace Prismedia.Infrastructure.Tests;

public sealed class SecurityServiceTests {
    [Fact]
    public void HumanApiKeyWordListIsLargeShortAndAscii() {
        Assert.True(HumanApiKeyPassphraseGenerator.Words.Count >= 2048);
        Assert.All(HumanApiKeyPassphraseGenerator.Words, word => {
            Assert.InRange(word.Length, 3, 5);
            Assert.Matches("^[a-z]+$", word);
        });
    }

    [Fact]
    public void HumanApiKeyGenerationReturnsThreeShortWords() {
        var key = HumanApiKeyPassphraseGenerator.Generate();

        Assert.Matches("^[a-z]{3,5}-[a-z]{3,5}-[a-z]{3,5}$", key);
    }

    [Fact]
    public void ApiKeyNormalizationAcceptsWhitespaceAndCase() {
        var normalized = PrismediaSecurityService.NormalizeApiKey("  Fox Lima_ALPHA  ");

        Assert.Equal("fox-lima-alpha", normalized);
    }
}
