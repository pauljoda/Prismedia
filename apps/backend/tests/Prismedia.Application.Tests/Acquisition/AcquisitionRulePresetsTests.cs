using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class AcquisitionRulePresetsTests {
    [Theory]
    [InlineData("H.265 / HEVC", "Example 1080p HEVC", true)]
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
}
