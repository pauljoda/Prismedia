using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Entities;

/// <summary>Credit metadata exposed by detail routes that need character or role labels.</summary>
/// <param name="PersonId">Referenced person entity identifier.</param>
/// <param name="Role">Provider or domain role code, when known.</param>
/// <param name="Character">Character, credit subtitle, or contribution label, when known.</param>
public sealed record EntityCreditMetadata(Guid PersonId, string? Role, string? Character);

/// <summary>
/// Shared shape implemented by every entity card and kind-specific detail contract.
/// Carries the fields common to all entities so detail routes can be returned as a single
/// strongly typed contract instead of <see cref="object"/>.
/// </summary>
public interface IEntityCard {
    /// <summary>Global entity identifier.</summary>
    Guid Id { get; }

    /// <summary>Entity kind.</summary>
    EntityKind Kind { get; }

    /// <summary>Primary display title.</summary>
    string Title { get; }

    /// <summary>Structural parent entity identifier, or null for root and virtual collection children.</summary>
    Guid? ParentEntityId { get; }

    /// <summary>Optional structural order under the parent entity.</summary>
    int? SortOrder { get; }

    /// <summary>Shared capabilities already projected for the card.</summary>
    IReadOnlyList<EntityCapability> Capabilities { get; }

    /// <summary>Generic child groups keyed by entity kind.</summary>
    IReadOnlyList<EntityGroup> ChildrenByKind { get; }

    /// <summary>Generic non-structural relationship groups keyed by entity kind.</summary>
    IReadOnlyList<EntityGroup> Relationships { get; }
}

/// <summary>
/// Abstract base for every entity detail contract. Owns the cross-cutting envelope so
/// concrete <c>*Detail</c> records only declare their kind-specific extras.
/// Serializes flat — derived properties merge with the base properties on the wire.
/// </summary>
public abstract record EntityDetail : IEntityCard {
    /// <inheritdoc />
    public required Guid Id { get; init; }

    /// <inheritdoc />
    public required EntityKind Kind { get; init; }

    /// <inheritdoc />
    public required string Title { get; init; }

    /// <inheritdoc />
    public required Guid? ParentEntityId { get; init; }

    /// <inheritdoc />
    public required int? SortOrder { get; init; }

    /// <inheritdoc />
    public required IReadOnlyList<EntityCapability> Capabilities { get; init; }

    /// <inheritdoc />
    public required IReadOnlyList<EntityGroup> ChildrenByKind { get; init; }

    /// <inheritdoc />
    public required IReadOnlyList<EntityGroup> Relationships { get; init; }
}

/// <summary>
/// Normalized card/detail shape used across media, taxonomy, and collection routes
/// when a route returns the shared envelope with no kind-specific extras.
/// </summary>
public sealed record EntityCard : EntityDetail;

/// <summary>
/// Cursor-paged entity list response.
/// </summary>
/// <param name="Items">Current page of entity cards.</param>
/// <param name="NextCursor">Cursor for the next page, or null when complete.</param>
/// <param name="TotalCount">
/// Total number of entities matching the same filters (kind, query, NSFW) as this
/// response, ignoring the cursor. Allows the client to render accurate page-of-pages
/// indicators and seek-to-end affordances without re-counting after every load.
/// </param>
public sealed record EntityListResponse(
    IReadOnlyList<EntityThumbnail> Items,
    string? NextCursor,
    int TotalCount);
