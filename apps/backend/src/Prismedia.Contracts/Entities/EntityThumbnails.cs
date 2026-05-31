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

/// <summary>Lightweight entity shape for grids, thumbnail strips, and relationship previews.</summary>
public sealed record EntityThumbnail(
    Guid Id,
    string Kind,
    string Title,
    Guid? ParentEntityId,
    int? SortOrder,
    string? CoverUrl,
    string? CoverThumbUrl,
    string HoverKind,
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
    public string? ParentKind { get; init; }

    /// <summary>
    /// Fraction watched (videos) or read (books) in the range 0..1 for a thumbnail progress
    /// meter, or <c>null</c> when the entity has no meaningful progress to show. A completed
    /// item reads 1.0.
    /// </summary>
    public double? Progress { get; init; }
}

/// <summary>API-facing grouped entities for child and relationship collections.</summary>
/// <param name="Kind">Entity kind code represented by the group.</param>
/// <param name="Label">Human-readable group label, such as Episodes, Tags, or Cast.</param>
/// <param name="Entities">Entities in deterministic display order.</param>
public sealed record EntityGroup(string Kind, string Label, IReadOnlyList<EntityThumbnail> Entities) {
    /// <summary>
    /// Relationship code represented by this group, such as <c>cast</c>, <c>studio</c>,
    /// or <c>tags</c>. Structural child groups leave this unset because their grouping
    /// is already fully described by <see cref="Kind"/>.
    /// </summary>
    public string? Code { get; init; }
}

/// <summary>Batch thumbnail request body.</summary>
/// <param name="Ids">Entity identifiers to resolve.</param>
public sealed record EntityThumbnailBatchRequest(IReadOnlyList<Guid> Ids);

/// <summary>Batch thumbnail response body.</summary>
/// <param name="Items">Resolved thumbnails in requested order where possible.</param>
public sealed record EntityThumbnailBatchResponse(IReadOnlyList<EntityThumbnail> Items);
