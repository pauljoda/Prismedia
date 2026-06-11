using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Entities;

/// <summary>Compact metadata chip displayed by generic entity thumbnails.</summary>
/// <param name="Icon">Icon code from the shared thumbnail vocabulary.</param>
/// <param name="Label">Short display label.</param>
public sealed record EntityThumbnailMeta(string Icon, string Label);

/// <summary>One still image used by shared thumbnail hover scrub previews.</summary>
/// <param name="EntityId">Entity represented by this preview frame.</param>
/// <param name="Title">Display title for the represented entity.</param>
/// <param name="Path">Asset path for the preview still.</param>
public sealed record EntityThumbnailHoverImage(Guid EntityId, string Title, string Path);

/// <summary>
/// Number of distinct source entities of one kind that reference a taxonomy entity (for example,
/// the videos that credit a person or the galleries tagged with a tag). Lets list rows show a
/// "10 videos" style stat, and compatibility layers expose child counts, without loading each
/// entity's relationship graph.
/// </summary>
/// <param name="Kind">Stable entity kind code of the referencing source entities.</param>
/// <param name="Count">Distinct source entities of that kind referencing this entity.</param>
public sealed record EntityKindCount(EntityKind Kind, int Count);

/// <summary>Lightweight entity shape for grids, thumbnail strips, and relationship previews.</summary>
public sealed record EntityThumbnail(
    Guid Id,
    EntityKind Kind,
    string Title,
    Guid? ParentEntityId,
    int? SortOrder,
    string? CoverUrl,
    string? CoverThumbUrl,
    ThumbnailHoverKind HoverKind,
    string? HoverUrl,
    IReadOnlyList<EntityThumbnailHoverImage> HoverImages,
    IReadOnlyList<EntityThumbnailMeta> Meta,
    int? Rating,
    bool IsFavorite,
    bool IsNsfw,
    bool IsOrganized) {
    /// <summary>
    /// Structural parent entity kind code when the thumbnail has a parent. Used by
    /// clients to route child media through richer parent detail surfaces.
    /// </summary>
    public EntityKind? ParentKind { get; init; }

    /// <summary>
    /// When the entity was added to the library. Surfaced so compatibility layers (e.g. the Jellyfin
    /// surface) can populate the always-present <c>DateCreated</c> field that clients sort by.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// Fraction watched (videos) or read (books) in the range 0..1 for a thumbnail progress
    /// meter, or <c>null</c> when the entity has no meaningful progress to show. A completed
    /// item reads 1.0.
    /// </summary>
    public double? Progress { get; init; }

    /// <summary>
    /// Number of completed plays recorded for the entity, when it tracks playback (e.g. audio tracks
    /// and videos). Lets list rows show a play-count stat without fetching the full detail graph.
    /// </summary>
    public int? PlayCount { get; init; }

    /// <summary>
    /// Tag names applied to the entity, surfaced so list-level compatibility layers (the Jellyfin
    /// surface) can expose them as genres without fetching each entity's full detail graph.
    /// </summary>
    public IReadOnlyList<string>? Genres { get; init; }

    /// <summary>
    /// Counts of the source entities that reference this entity, grouped by their kind. Populated
    /// for taxonomy (person/studio/tag) cards so grids can show reference-count chips and
    /// compatibility layers can expose child counts; <c>null</c> for kinds with no inbound-reference
    /// concept.
    /// </summary>
    public IReadOnlyList<EntityKindCount>? ReferenceCounts { get; init; }
}

/// <summary>API-facing grouped entities for child and relationship collections.</summary>
/// <param name="Kind">Entity kind code represented by the group.</param>
/// <param name="Label">Human-readable group label, such as Episodes, Tags, or Cast.</param>
/// <param name="Entities">Entities in deterministic display order.</param>
public sealed record EntityGroup(EntityKind Kind, string Label, IReadOnlyList<EntityThumbnail> Entities) {
    /// <summary>
    /// Relationship code represented by this group, such as <c>cast</c>, <c>studio</c>,
    /// or <c>tags</c>. Structural child groups leave this unset because their grouping
    /// is already fully described by <see cref="Kind"/>.
    /// </summary>
    public RelationshipKind? Code { get; init; }
}

/// <summary>Batch thumbnail request body.</summary>
/// <param name="Ids">Entity identifiers to resolve.</param>
public sealed record EntityThumbnailBatchRequest(IReadOnlyList<Guid> Ids);

/// <summary>Batch thumbnail response body.</summary>
/// <param name="Items">Resolved thumbnails in requested order where possible.</param>
public sealed record EntityThumbnailBatchResponse(IReadOnlyList<EntityThumbnail> Items);
