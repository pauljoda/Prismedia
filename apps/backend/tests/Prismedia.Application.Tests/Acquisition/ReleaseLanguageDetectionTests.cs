using Prismedia.Application.Acquisition;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class ReleaseLanguageDetectionTests {
    [Theory]
    [InlineData("en", "eng")]
    [InlineData("fr", "fra")]
    [InlineData("de", "ger")]
    [InlineData("es", "spa")]
    [InlineData("it", "ita")]
    [InlineData("pt", "por")]
    [InlineData("nl", "dut")]
    [InlineData("pl", "pol")]
    [InlineData("no", "nor")]
    [InlineData("fi", "fin")]
    [InlineData("cs", "ces")]
    [InlineData("tr", "tur")]
    public void IsoProfileAndStreamCodesAgree(string preferred, string declared) =>
        Assert.Equal(ReleaseLanguageDetection.Canonicalize(preferred), ReleaseLanguageDetection.Canonicalize(declared));

    [Fact]
    public void TwoLetterCodesInTitlesAreWordsButStructuredAttributesAreLanguages() {
        Assert.Empty(ReleaseLanguageDetection.Detect("No Country For Old Men 2007 EN Route", null));
        Assert.Contains(ReleaseLanguageDetection.Canonicalize("English"),
            ReleaseLanguageDetection.Detect("Film 2020", "EN"));
    }
}
