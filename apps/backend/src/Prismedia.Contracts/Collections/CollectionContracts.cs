using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Collections;

/// <summary>Request body for creating or fully updating a collection.</summary>
/// <param name="Title">User-facing collection title.</param>
/// <param name="Description">Optional markdown/plain-text description.</param>
/// <param name="Mode">Population mode code: manual, dynamic, or hybrid.</param>
/// <param name="RuleTreeJson">Optional dynamic rule tree JSON.</param>
/// <param name="CoverMode">Cover selection mode code: mosaic, custom, or item.</param>
/// <param name="CoverItemId">Optional entity used as the collection cover.</param>
/// <param name="IsNsfw">Whether the collection itself should be hidden in SFW mode.</param>
public sealed record CollectionWriteRequest(
    string Title,
    string? Description,
    CollectionMode? Mode,
    string? RuleTreeJson,
    CollectionCoverMode? CoverMode,
    Guid? CoverItemId,
    bool? IsNsfw);

/// <summary>Reference to an entity being added to a collection.</summary>
/// <param name="EntityType">Entity kind code for the item.</param>
/// <param name="EntityId">Entity identifier to add.</param>
public sealed record CollectionItemReference(EntityKind EntityType, Guid EntityId);

/// <summary>Request body for manually adding items to a collection.</summary>
/// <param name="Items">Entities to add in the order supplied by the user.</param>
public sealed record CollectionAddItemsRequest(IReadOnlyList<CollectionItemReference> Items);

/// <summary>Request body for removing collection item rows.</summary>
/// <param name="ItemIds">Collection item row identifiers to remove.</param>
public sealed record CollectionRemoveItemsRequest(IReadOnlyList<Guid> ItemIds);

/// <summary>Request body for reordering existing collection item rows.</summary>
/// <param name="ItemIds">Ordered collection item row identifiers. Omitted rows keep their relative order after these rows.</param>
public sealed record CollectionReorderItemsRequest(IReadOnlyList<Guid> ItemIds);

/// <summary>Count response for collection item mutations.</summary>
/// <param name="Count">Number of affected collection item rows.</param>
public sealed record CollectionItemMutationResponse(int Count);

/// <summary>Response body for deleting a collection.</summary>
/// <param name="Id">Deleted collection entity identifier.</param>
public sealed record CollectionDeleteResponse(Guid Id);

/// <summary>Request body for previewing a dynamic collection rule tree.</summary>
/// <param name="RuleTreeJson">Dynamic rule tree JSON to evaluate without persisting.</param>
public sealed record CollectionRulePreviewRequest(string RuleTreeJson);

/// <summary>Preview item returned from rule evaluation.</summary>
/// <param name="EntityType">Matched entity kind code.</param>
/// <param name="EntityId">Matched entity identifier.</param>
/// <param name="Entity">Thumbnail payload for display.</param>
public sealed record CollectionRulePreviewItem(EntityKind EntityType, Guid EntityId, EntityThumbnail Entity);

/// <summary>Response body for a dynamic rule preview.</summary>
/// <param name="Total">Total visible entities matched by the rule tree.</param>
/// <param name="ByType">Visible match counts by entity kind code.</param>
/// <param name="Sample">Small ordered sample of visible matched entities.</param>
public sealed record CollectionRulePreviewResponse(
    int Total,
    IReadOnlyDictionary<string, int> ByType,
    IReadOnlyList<CollectionRulePreviewItem> Sample);

/// <summary>Response body for a persisted dynamic collection refresh.</summary>
/// <param name="Refreshed">Whether refresh work ran.</param>
/// <param name="ItemCount">Total item count after refresh.</param>
public sealed record CollectionRefreshResponse(bool Refreshed, int ItemCount);

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
    EntityKind EntityType,
    Guid EntityId,
    CollectionItemSource Source,
    int SortOrder,
    DateTimeOffset AddedAt,
    EntityThumbnail Entity);

/// <summary>Ordered collection item response.</summary>
/// <param name="Items">Collection items in display order.</param>
public sealed record CollectionItemsResponse(IReadOnlyList<CollectionItemDetail> Items);

/// <summary>
/// API-facing collection detail shape with expanded collection members.
/// </summary>
public sealed record CollectionDetail : EntityDetail {
    /// <summary>Collection membership mode.</summary>
    public required CollectionMode? Mode { get; init; }

    /// <summary>Dynamic collection rule tree JSON, when present.</summary>
    public required string? RuleTreeJson { get; init; }

    /// <summary>Collection cover selection mode.</summary>
    public required CollectionCoverMode? CoverMode { get; init; }

    /// <summary>Entity selected as the collection cover item.</summary>
    public required Guid? CoverItemId { get; init; }

    /// <summary>Last dynamic refresh timestamp, when known.</summary>
    public required DateTimeOffset? LastRefreshedAt { get; init; }
}
