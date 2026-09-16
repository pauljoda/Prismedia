using Prismedia.Domain.Entities;
using Prismedia.Contracts.Plugins;

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
    public static IReadOnlyDictionary<string, int> Normalize(EntityMetadataPatch patch) {
        var values = new Dictionary<string, int>(Normalize(patch.Positions), StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries(patch)) values[NormalizeCode(entry.Code)] = entry.Value;
        return values;
    }

    public static IReadOnlyDictionary<string, string?> Labels(EntityMetadataPatch patch, bool allowClear = false) =>
        Entries(patch).Where(entry => entry.Label is not null && (allowClear || !string.IsNullOrWhiteSpace(entry.Label)))
            .ToDictionary(entry => NormalizeCode(entry.Code), entry => string.IsNullOrWhiteSpace(entry.Label) ? null : entry.Label.Trim(), StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<Prismedia.Contracts.Entities.EntityPosition> Entries(EntityMetadataPatch patch) {
        var entries = patch.PositionEntries ?? [];
        if (entries.Count > 16 || entries.Any(entry => entry is null || string.IsNullOrWhiteSpace(entry.Code)
            || entry.Code.Length > 64 || entry.Value < 0 || entry.Label?.Length > 128)
            || entries.Select(entry => NormalizeCode(entry.Code)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Count) {
            throw new ArgumentException("Position entries require unique codes, nonnegative ordering values, and labels of at most 128 characters.");
        }
        return entries;
    }

    /// <summary>Gets the plugin wire field for one canonical Prismedia position code.</summary>
    public static string PluginFieldFor(string positionCode) => positionCode switch {
        EntityPositionCodes.Season => PluginPositionField.SeasonNumber,
        EntityPositionCodes.Episode => PluginPositionField.EpisodeNumber,
        EntityPositionCodes.AbsoluteEpisode => PluginPositionField.AbsoluteEpisodeNumber,
        EntityPositionCodes.Volume => PluginPositionField.VolumeNumber,
        EntityPositionCodes.Chapter => PluginPositionField.ChapterNumber,
        EntityPositionCodes.Page => PluginPositionField.PageNumber,
        EntityPositionCodes.Track => PluginPositionField.TrackNumber,
        EntityPositionCodes.Sort => PluginPositionField.SortOrder,
        _ => positionCode
    };

    /// <summary>
    /// Gets the plugin field used when a structural traversal has only the persisted sibling sort
    /// order. The Entity-kind definition declares any semantic override; unknown kinds safely use
    /// the generic sort code rather than inferring from their sort precedence.
    /// </summary>
    public static string StructuralFallbackPluginFieldFor(string kindCode) {
        var positionCode = kindCode.Equals(kindCode.Trim(), StringComparison.Ordinal) &&
            EntityKindRegistry.TryDescribe(kindCode, out var definition)
            ? definition.StructuralFallbackPositionCode
            : EntityPositionCodes.Sort;
        return PluginFieldFor(positionCode);
    }

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
