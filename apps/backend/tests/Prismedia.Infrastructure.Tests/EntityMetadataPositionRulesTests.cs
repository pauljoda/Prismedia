using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Tests;

public sealed class EntityMetadataPositionRulesTests {
    [Fact]
    public void NormalizeMapsProviderAliasesToCanonicalCodes() {
        var positions = EntityMetadataPositionRules.Normalize(new Dictionary<string, int> {
            ["seasonNumber"] = 1,
            ["episodeNumber"] = 2,
            ["absoluteEpisodeNumber"] = 14,
            ["trackNumber"] = 3,
            ["sortOrder"] = 9
        });

        Assert.Equal(1, positions["season"]);
        Assert.Equal(2, positions["episode"]);
        Assert.Equal(14, positions["absolute-episode"]);
        Assert.Equal(3, positions["track"]);
        Assert.Equal(9, positions["sort"]);
        Assert.False(positions.ContainsKey("episodeNumber"));
    }

    [Theory]
    [InlineData("video-season", "season", 3)]
    [InlineData("video-season", "sort", 4)]
    [InlineData("video", "episode", 5)]
    [InlineData("video", "absolute-episode", 6)]
    [InlineData("audio-track", "track", 7)]
    [InlineData("book-page", "page", 8)]
    public void SortOrderUsesKindSpecificPositionPriority(string kindCode, string positionCode, int expected) {
        var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) {
            [positionCode] = expected
        };

        Assert.Equal(expected, EntityMetadataPositionRules.SortOrderFor(kindCode, positions));
    }

    [Fact]
    public void SortOrderPrefersEpisodeBeforeFallbackSortForVideos() {
        var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) {
            ["episode"] = 2,
            ["sort"] = 99
        };

        Assert.Equal(2, EntityMetadataPositionRules.SortOrderFor(EntityKindRegistry.Video.Code, positions));
    }
}
