using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Plugins;

internal static class EntityMetadataPositionRules {
    public static IReadOnlyDictionary<string, int> Normalize(IReadOnlyDictionary<string, int> positions) {
        var normalized = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, value) in positions) {
            normalized[NormalizeCode(code)] = value;
        }

        return normalized;
    }

    public static int? SortOrderFor(string kindCode, IReadOnlyDictionary<string, int> positions) {
        if (kindCode.Equals(EntityKindRegistry.VideoSeason.Code, StringComparison.OrdinalIgnoreCase)) {
            return PositionValue(positions, EntityPositionCodes.Season, EntityPositionCodes.Sort);
        }

        if (kindCode.Equals(EntityKindRegistry.Video.Code, StringComparison.OrdinalIgnoreCase)) {
            return PositionValue(positions, EntityPositionCodes.Episode, EntityPositionCodes.AbsoluteEpisode, EntityPositionCodes.Sort);
        }

        return PositionValue(positions, EntityPositionCodes.Track, EntityPositionCodes.Page, EntityPositionCodes.Chapter, EntityPositionCodes.Volume, EntityPositionCodes.Sort);
    }

    private static int? PositionValue(IReadOnlyDictionary<string, int> positions, params string[] codes) {
        foreach (var code in codes) {
            if (positions.TryGetValue(code, out var value)) {
                return value;
            }
        }

        return null;
    }

    private static string NormalizeCode(string code) => code.Trim() switch {
        var value when value.Equals("seasonNumber", StringComparison.OrdinalIgnoreCase) => EntityPositionCodes.Season,
        var value when value.Equals("episodeNumber", StringComparison.OrdinalIgnoreCase) => EntityPositionCodes.Episode,
        var value when value.Equals("absoluteEpisodeNumber", StringComparison.OrdinalIgnoreCase) => EntityPositionCodes.AbsoluteEpisode,
        var value when value.Equals("volumeNumber", StringComparison.OrdinalIgnoreCase) => EntityPositionCodes.Volume,
        var value when value.Equals("chapterNumber", StringComparison.OrdinalIgnoreCase) => EntityPositionCodes.Chapter,
        var value when value.Equals("pageNumber", StringComparison.OrdinalIgnoreCase) => EntityPositionCodes.Page,
        var value when value.Equals("trackNumber", StringComparison.OrdinalIgnoreCase) => EntityPositionCodes.Track,
        var value when value.Equals("sortOrder", StringComparison.OrdinalIgnoreCase) => EntityPositionCodes.Sort,
        var value => value
    };
}
