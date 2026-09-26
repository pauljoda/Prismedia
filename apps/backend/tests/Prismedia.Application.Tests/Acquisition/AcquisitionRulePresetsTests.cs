using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class AcquisitionRulePresetsTests {
    [Theory]
    [InlineData("H.265 / HEVC", "Example 1080p HEVC", true)]
    [InlineData("H.265 / HEVC", "Example_2024_2160p_HEVC_TrueHD", true)]
    [InlineData("Dolby Vision", "Example_2024_REMUX_DV_HDR_HEVC", true)]
    [InlineData("Dolby Vision", "Example_2024_DVD_Rip", false)]
    [InlineData("Web download", "Example_2024_WEB_DL_H264", true)]
    [InlineData("H.265 / HEVC", "Example 1080p H.264", false)]
    [InlineData("English audio", "Example 1080p ENG", true)]
    [InlineData("English audio", "Example 1080p MULTi", false)]
    [InlineData("English audio", "Example 1080p JPN [ENG Subs]", false)]
    [InlineData("Unspecified multiple audio languages", "Example Dual Audio", true)]
    [InlineData("Unspecified multiple audio languages", "Example Multi-Sub", false)]
    [InlineData("HDR10", "Example HDR10+", true)]
    [InlineData("HDR10", "Example HDR", false)]
    [InlineData("Dolby Vision", "Example DV", true)]
    [InlineData("Dolby Vision", "Example DVD", false)]
    [InlineData("Web download", "Example WEB-DL", true)]
    [InlineData("M4B audiobook", "Author - Book (2020) [M4B]", true)]
    [InlineData("M4B audiobook", "Author - Book MP3", false)]
    [InlineData("Chapterized audiobook", "Author - Book (Chapterized) MP3 64k", true)]
    [InlineData("Chapterized audiobook", "Author - Book Chapter 1", false)]
    public void PresetsUseTheAdvancedMatcher(string name, string title, bool expected) {
        var preset = AcquisitionRulePresets.List().Single(preset => preset.Name == name);
        var rules = BookAcquisitionRules.Default with {
            Kind = EntityKind.Movie,
            CustomFormats = [new(preset.Name, preset.SuggestedScore,
                preset.Conditions.Select(condition => new CustomFormatCondition(
                    condition.Type, condition.Value, condition.Negate, condition.Required)).ToArray())]
        };
        Assert.Equal(expected ? preset.SuggestedScore : 0, CustomFormatEvaluation.Score(title, rules));
    }

    [Fact]
    public void EachProfileKindIsOfferedOnlyThePresetsThatFitIt() {
        var book = AcquisitionRulePresets.List(EntityKind.Book).Select(preset => preset.Name).ToArray();
        var movie = AcquisitionRulePresets.List(EntityKind.Movie).Select(preset => preset.Name).ToArray();
        var episode = AcquisitionRulePresets.List(EntityKind.VideoEpisode).Select(preset => preset.Name).ToArray();

        Assert.Contains("M4B audiobook", book);
        Assert.Contains("Chapterized audiobook", book);
        Assert.Contains("English audio", book);
        Assert.DoesNotContain("H.265 / HEVC", book);
        Assert.DoesNotContain("Lossless audio", book);
        Assert.Contains("H.265 / HEVC", movie);
        Assert.DoesNotContain("M4B audiobook", movie);
        Assert.Equal(AcquisitionRulePresets.List(EntityKind.VideoSeries).Select(preset => preset.Name), episode);
    }
}
