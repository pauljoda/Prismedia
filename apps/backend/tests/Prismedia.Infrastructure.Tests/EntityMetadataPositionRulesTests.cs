using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Tests;

public sealed class EntityMetadataPositionRulesTests {
    [Fact]
    public void NormalizeMapsProviderAliasesToCanonicalCodes() {
        var positions = EntityMetadataPositionRules.Normalize(new Dictionary<string, int> {
            [PluginPositionField.SeasonNumber] = 1,
            [PluginPositionField.EpisodeNumber] = 2,
            [PluginPositionField.AbsoluteEpisodeNumber] = 14,
            [PluginPositionField.TrackNumber] = 3,
            [PluginPositionField.SortOrder] = 9
        });

        Assert.Equal(1, positions[EntityPositionCodes.Season]);
        Assert.Equal(2, positions[EntityPositionCodes.Episode]);
        Assert.Equal(14, positions[EntityPositionCodes.AbsoluteEpisode]);
        Assert.Equal(3, positions[EntityPositionCodes.Track]);
        Assert.Equal(9, positions[EntityPositionCodes.Sort]);
        Assert.False(positions.ContainsKey(PluginPositionField.EpisodeNumber));
    }

    [Theory]
    [InlineData(EntityPositionCodes.Season, PluginPositionField.SeasonNumber)]
    [InlineData(EntityPositionCodes.Episode, PluginPositionField.EpisodeNumber)]
    [InlineData(EntityPositionCodes.AbsoluteEpisode, PluginPositionField.AbsoluteEpisodeNumber)]
    [InlineData(EntityPositionCodes.Volume, PluginPositionField.VolumeNumber)]
    [InlineData(EntityPositionCodes.Chapter, PluginPositionField.ChapterNumber)]
    [InlineData(EntityPositionCodes.Page, PluginPositionField.PageNumber)]
    [InlineData(EntityPositionCodes.Track, PluginPositionField.TrackNumber)]
    [InlineData(EntityPositionCodes.Sort, PluginPositionField.SortOrder)]
    public void PluginFieldMappingIsTheInverseOfTheCanonicalPositionBoundary(string code, string expectedField) {
        Assert.Equal(expectedField, EntityMetadataPositionRules.PluginFieldFor(code));
    }

    [Theory]
    [InlineData("video-season", PluginPositionField.SeasonNumber)]
    [InlineData("video-episode", PluginPositionField.SortOrder)]
    [InlineData("unknown-kind", PluginPositionField.SortOrder)]
    public void StructuralFallbackFieldComesFromTheDefinitionOrGenericSort(string kindCode, string expectedField) {
        Assert.Equal(expectedField, EntityMetadataPositionRules.StructuralFallbackPluginFieldFor(kindCode));
    }

    [Theory]
    [InlineData("video-season", "season", 3)]
    [InlineData("video-season", "sort", 4)]
    [InlineData("video-episode", "episode", 5)]
    [InlineData("video-episode", "absolute-episode", 6)]
    [InlineData("audio-track", "track", 7)]
    public void SortOrderUsesKindSpecificPositionPriority(string kindCode, string positionCode, int expected) {
        var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) {
            [positionCode] = expected
        };

        Assert.Equal(expected, EntityMetadataPositionRules.SortOrderFor(kindCode, positions));
    }

    [Fact]
    public void SortOrderPrefersEpisodeBeforeFallbackSortForEpisodes() {
        var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) {
            ["episode"] = 2,
            ["sort"] = 99
        };

        Assert.Equal(2, EntityMetadataPositionRules.SortOrderFor(EntityKind.VideoEpisode.ToCode(), positions));
    }
}
