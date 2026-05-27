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
            return PositionValue(positions, "season", "sort");
        }

        if (kindCode.Equals(EntityKindRegistry.Video.Code, StringComparison.OrdinalIgnoreCase)) {
            return PositionValue(positions, "episode", "absolute-episode", "sort");
        }

        return PositionValue(positions, "track", "page", "chapter", "volume", "sort");
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
        var value when value.Equals("seasonNumber", StringComparison.OrdinalIgnoreCase) => "season",
        var value when value.Equals("episodeNumber", StringComparison.OrdinalIgnoreCase) => "episode",
        var value when value.Equals("absoluteEpisodeNumber", StringComparison.OrdinalIgnoreCase) => "absolute-episode",
        var value when value.Equals("volumeNumber", StringComparison.OrdinalIgnoreCase) => "volume",
        var value when value.Equals("chapterNumber", StringComparison.OrdinalIgnoreCase) => "chapter",
        var value when value.Equals("pageNumber", StringComparison.OrdinalIgnoreCase) => "page",
        var value when value.Equals("trackNumber", StringComparison.OrdinalIgnoreCase) => "track",
        var value when value.Equals("sortOrder", StringComparison.OrdinalIgnoreCase) => "sort",
        var value => value
    };
}
