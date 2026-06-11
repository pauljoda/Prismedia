using Prismedia.Domain.Entities;

namespace Prismedia.Application.Plugins;

/// <summary>
/// Maps stored entity kind codes to the high-level media selector kinds exposed in the Auto Identify
/// settings (video, gallery, image, audio, book). Container roots such as a video series or audio
/// library map to the same selector as their media, so a top-level series is covered by the "video"
/// selection. Kinds absent from this map (intermediate seasons/volumes, taxonomy entities, etc.) are
/// never auto-identified directly — they are reached by cascading from their identified root.
/// </summary>
public static class AutoIdentifySelectorKinds {
    private static readonly IReadOnlyDictionary<string, string> ByEntityKind =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            [EntityKindRegistry.Video.Code] = "video",
            [EntityKindRegistry.Movie.Code] = "video",
            [EntityKindRegistry.VideoSeries.Code] = "video",
            [EntityKindRegistry.Gallery.Code] = "gallery",
            [EntityKindRegistry.Image.Code] = "image",
            [EntityKindRegistry.AudioTrack.Code] = "audio",
            [EntityKindRegistry.AudioLibrary.Code] = "audio",
            [EntityKindRegistry.Book.Code] = "book",
        };

    /// <summary>
    /// Resolves the media selector kind for an entity kind code.
    /// </summary>
    /// <param name="entityKind">Stable entity kind code.</param>
    /// <param name="selectorKind">Resolved selector kind when the entity kind is auto-identifiable.</param>
    /// <returns>True when the entity kind maps to a selectable media kind.</returns>
    public static bool TryMap(string entityKind, out string selectorKind) =>
        ByEntityKind.TryGetValue(entityKind, out selectorKind!);
}
