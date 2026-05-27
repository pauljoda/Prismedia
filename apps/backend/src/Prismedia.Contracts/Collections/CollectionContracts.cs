using Prismedia.Contracts.Entities;

namespace Prismedia.Contracts.Collections;

/// <summary>Ordered item contained by a user collection.</summary>
/// <param name="Id">Collection item row identifier.</param>
/// <param name="CollectionId">Collection entity identifier that owns the item.</param>
/// <param name="EntityType">Kind code for the contained entity.</param>
/// <param name="EntityId">Contained entity identifier.</param>
/// <param name="Source">Whether the item was added manually or by a dynamic rule.</param>
/// <param name="SortOrder">Collection-local item order.</param>
/// <param name="AddedAt">Timestamp when the item was added to the collection.</param>
/// <param name="Entity">Thumbnail payload for rendering and navigation.</param>
public sealed record CollectionItemDetail(
    Guid Id,
    Guid CollectionId,
    string EntityType,
    Guid EntityId,
    string Source,
    int SortOrder,
    DateTimeOffset AddedAt,
    EntityThumbnail Entity);

/// <summary>Ordered collection item response.</summary>
/// <param name="Items">Collection items in playback/display order.</param>
public sealed record CollectionItemsResponse(IReadOnlyList<CollectionItemDetail> Items);

/// <summary>
/// API-facing collection detail shape with expanded collection members.
/// </summary>
public sealed record CollectionDetail : EntityDetail {
    /// <summary>Collection membership mode.</summary>
    public required string? Mode { get; init; }

    /// <summary>Dynamic collection rule tree JSON, when present.</summary>
    public required string? RuleTreeJson { get; init; }

    /// <summary>Collection cover selection mode.</summary>
    public required string? CoverMode { get; init; }

    /// <summary>Entity selected as the collection cover item.</summary>
    public required Guid? CoverItemId { get; init; }

    /// <summary>Duration for collection slideshow advancement.</summary>
    public required TimeSpan? SlideshowDuration { get; init; }

    /// <summary>Whether collection slideshows advance automatically.</summary>
    public required bool? SlideshowAutoAdvance { get; init; }

    /// <summary>Last dynamic refresh timestamp, when known.</summary>
    public required DateTimeOffset? LastRefreshedAt { get; init; }
}
