using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class ReleaseLanguageDetectionTests {
    [Theory]
    [InlineData("The French Connection", "The.French.Connection.1971.1080p.BluRay", null, true)]
    [InlineData("The French Connection", "The.French.Connection.1971.1080p.FRENCH.BluRay", null, false)]
    [InlineData("The French Connection", "The.French.Connection.1971.1080p.BluRay", "French", false)]
    [InlineData("English Teacher", "English.Teacher.S01E01.1080p.GER", null, false)]
    public void WorkTitleWordsAreNotAudioDeclarations(string work, string title, string? attribute, bool accepted) {
        var release = new IndexerRelease(title, 1000, null, null, DownloadProtocol.Usenet,
            "https://download.test/item", null, null, null, attribute, null);
        var rules = BookAcquisitionRules.Default with { TargetTitle = work, PreferredLanguages = ["English"] };
        Assert.Equal(accepted ? null : ReleaseRejectionReason.LanguageMismatch,
            new LanguageSpecification().Evaluate(release, rules));
    }

    [Fact]
    public void LanguageScoresIgnoreFormalWorkAliasesButTitleRegexesKeepTheOriginalText() {
        const string title = "[Group] The.French.Connection.1971.1080p.BluRay";
        var rules = BookAcquisitionRules.Default with {
            Kind = EntityKind.Movie, TargetTitle = "Canonical title", TargetAlternativeTitles = ["The French Connection"],
            PreferredLanguages = ["French"],
            CustomFormats = [
                new("French audio", 100, [new(CustomFormatConditionType.Language, "French", false, true)]),
                new("Literal title", 25, [new(CustomFormatConditionType.ReleaseTitle, "French", false, true)])]
        };
        var release = new IndexerRelease(title, 1000, null, null, DownloadProtocol.Usenet,
            "https://download.test/item", null, null, null, null, null);
        Assert.Equal(25, CustomFormatEvaluation.Score(title, rules));
        Assert.Equal(123, ReleaseLanguageDetection.RankScore(release, rules, 123));
    }

    [Fact]
    public void KnownEpisodeTitleWordsDoNotOverrideAnIndependentAudioTag() {
        const string title = "Show.S02E03.French.Lesson.1080p.GER";
        var rules = BookAcquisitionRules.Default with {
            Kind = EntityKind.VideoEpisode, TargetTitle = "Show", SeasonNumber = 2, EpisodeNumber = 3,
            TargetEpisodeTitle = "French Lesson", PreferredLanguages = ["French"],
            CustomFormats = [new("French audio", 100, [new(CustomFormatConditionType.Language, "French", false, true)])]
        };
        var release = new IndexerRelease(title, 1000, null, null, DownloadProtocol.Usenet,
            "https://download.test/item", null, null, null, null, null);
        Assert.Equal(ReleaseRejectionReason.LanguageMismatch, new LanguageSpecification().Evaluate(release, rules));
        Assert.Equal(0, CustomFormatEvaluation.Score(title, rules));
    }

    [Theory]
    [InlineData("PT-BR", "Portuguese")]
    [InlineData("PT_BR", "Portuguese")]
    [InlineData("PTBR", "Portuguese")]
    [InlineData("PT-PT", "Portuguese")]
    [InlineData("EN-US", "English")]
    [InlineData("EN-GB", "English")]
    [InlineData("FR-CA", "French")]
    [InlineData("FR-FR", "French")]
    [InlineData("ES-MX", "Spanish")]
    [InlineData("ES-ES", "Spanish")]
    [InlineData("ES-419", "Spanish")]
    [InlineData("ZH-CN", "Chinese")]
    [InlineData("ZH-TW", "Chinese")]
    public void RegionalDeclarationsAgreeAcrossTitlesAttributesAndPreferences(string tag, string language) {
        var canonical = ReleaseLanguageDetection.Canonicalize(language);
        Assert.Equal(canonical, ReleaseLanguageDetection.Canonicalize(tag));
        Assert.Contains(canonical, ReleaseLanguageDetection.Detect($"Example.1980.Multi.{tag}-Group", null));
        Assert.Contains(canonical, ReleaseLanguageDetection.Detect("Example", tag));
        Assert.Equal(2, ReleaseLanguageDetection.PreferenceRank($"Example.Multi.{tag}", null, [language]));
    }

    [Theory]
    [InlineData("Example.1080p.PT-BR.Subs")]
    [InlineData("Example.1080p.Subtitles.PT_BR")]
    [InlineData("Example.1080p.[PTBR Subs]")]
    [InlineData("Example.1080p.EN-US+PT-BR.Subs")]
    [InlineData("Example.No-Go.1980.1080p")]
    [InlineData("Example.1980.PT-BRX")]
    public void RegionalTagsDoNotTurnSubtitlesOrPartialWordsIntoAudio(string title) =>
        Assert.Empty(ReleaseLanguageDetection.Detect(title, null));

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
