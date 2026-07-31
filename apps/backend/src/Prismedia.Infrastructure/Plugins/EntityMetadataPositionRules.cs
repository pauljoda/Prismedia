using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Plugin protocol position field names. These spellings are external provider vocabulary and are
/// normalized to <see cref="EntityPositionCodes"/> before Prismedia persists or compares them.
/// </summary>
internal static class PluginPositionField {
    // prism-vocab: external
    public const string SeasonNumber = "seasonNumber";

    // prism-vocab: external
    public const string EpisodeNumber = "episodeNumber";

    // prism-vocab: external
    public const string AbsoluteEpisodeNumber = "absoluteEpisodeNumber";

    // prism-vocab: external
    public const string VolumeNumber = "volumeNumber";

    // prism-vocab: external
    public const string ChapterNumber = "chapterNumber";

    // prism-vocab: external
    public const string PageNumber = "pageNumber";

    // prism-vocab: external
    public const string TrackNumber = "trackNumber";

    // prism-vocab: external
    public const string SortOrder = "sortOrder";
}

internal static class EntityMetadataPositionRules {
    public static IReadOnlyDictionary<string, int> Normalize(IReadOnlyDictionary<string, int> positions) {
        var normalized = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, value) in positions) {
            normalized[NormalizeCode(code)] = value;
        }

        return normalized;
    }

    public static int? SortOrderFor(string kindCode, IReadOnlyDictionary<string, int> positions) {
        var precedence = kindCode.Equals(kindCode.Trim(), StringComparison.Ordinal) &&
            EntityKindRegistry.TryDescribe(kindCode, out var definition)
            ? definition.PositionSortOrderPrecedence
            : EntityKindDefinition.DefaultPositionSortOrderPrecedence;
        return PositionValue(positions, precedence);
    }

    private static int? PositionValue(IReadOnlyDictionary<string, int> positions, IReadOnlyList<string> codes) {
        foreach (var code in codes) {
            if (positions.TryGetValue(code, out var value)) {
                return value;
            }
        }

        return null;
    }

    private static string NormalizeCode(string code) => code.Trim() switch {
        var value when value.Equals(PluginPositionField.SeasonNumber, StringComparison.OrdinalIgnoreCase) => EntityPositionCodes.Season,
        var value when value.Equals(PluginPositionField.EpisodeNumber, StringComparison.OrdinalIgnoreCase) => EntityPositionCodes.Episode,
        var value when value.Equals(PluginPositionField.AbsoluteEpisodeNumber, StringComparison.OrdinalIgnoreCase) => EntityPositionCodes.AbsoluteEpisode,
        var value when value.Equals(PluginPositionField.VolumeNumber, StringComparison.OrdinalIgnoreCase) => EntityPositionCodes.Volume,
        var value when value.Equals(PluginPositionField.ChapterNumber, StringComparison.OrdinalIgnoreCase) => EntityPositionCodes.Chapter,
        var value when value.Equals(PluginPositionField.PageNumber, StringComparison.OrdinalIgnoreCase) => EntityPositionCodes.Page,
        var value when value.Equals(PluginPositionField.TrackNumber, StringComparison.OrdinalIgnoreCase) => EntityPositionCodes.Track,
        var value when value.Equals(PluginPositionField.SortOrder, StringComparison.OrdinalIgnoreCase) => EntityPositionCodes.Sort,
        var value => value
    };
}
