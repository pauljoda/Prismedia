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
    [Theory]
    [InlineData("Example.1080p.JPN.Audio.ENG.Subs", "japanese")]
    [InlineData("Example.1080p.JPN.[ENG Subs]", "japanese")]
    [InlineData("Example.1080p.GER.[Multi-Sub]", "german")]
    [InlineData("Example.1080p.GER.MultiSubs", "german")]
    [InlineData("Example.1080p.ENG.FRE.Subs", null)]
    [InlineData("Example.1080p.Sub.ENG", null)]
    [InlineData("Example.1080p.Dual.Audio", "multi")]
    [InlineData("Dual.2022.1080p", null)]
    [InlineData("Example.1080p.[JA+EN]", "english,japanese")]
    public void AudioDeclarationsExcludeSubtitleLabels(string title, string? expected) {
        var languages = ReleaseLanguageDetection.Detect(title, null).Order().ToArray();
        Assert.Equal(expected?.Split(',').Order().ToArray() ?? [], languages);
    }

    [Theory]
    [InlineData("ENG / FRE", "english,french")]
    [InlineData("en,de", "english,german")]
    public void StructuredLanguageListsKeepEachExplicitLanguage(string attribute, string expected) =>
        Assert.Equal(expected.Split(','), ReleaseLanguageDetection.Detect("Example", attribute).Order().ToArray());

    [Fact]
    public void SubtitleLabelDoesNotEraseIndependentAudioEvidence() =>
        Assert.Contains("english", ReleaseLanguageDetection.Detect("Example.ENG.Audio.[ENG Subs]", null));
}
