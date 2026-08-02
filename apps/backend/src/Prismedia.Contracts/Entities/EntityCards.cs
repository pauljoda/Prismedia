using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Entities;

/// <summary>Relationship-edge metadata carried by the shared credits capability.</summary>
/// <param name="PersonId">Referenced person entity identifier.</param>
/// <param name="Role">Primary provider or domain role code, when known.</param>
/// <param name="Character">Primary character, credit subtitle, or contribution label, when known.</param>
/// <param name="Roles">
/// Every distinct role code linked to the person (e.g. director and writer). Contains
/// <paramref name="Role"/> first when one is known; editors must round-trip the full list so
/// secondary roles survive full-replace metadata saves.
/// </param>
/// <param name="Characters">
/// Every distinct character linked to the person. Contains <paramref name="Character"/> first
/// when one is known; editors must round-trip the full list.
/// </param>
public sealed record EntityCreditMetadata(
    Guid PersonId,
    string? Role,
    string? Character,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Characters);

/// <summary>
/// Backward-compatible name for the shared entity document contract used by existing application
/// and API consumers. New cross-cutting code should depend on <see cref="IEntityDocument"/> when it
/// needs the full detail envelope.
/// </summary>
public interface IEntityCard : IEntityDocument;

/// <summary>
/// Concrete shared Entity document returned by every Entity detail read. Additional
/// readable concepts are carried in <see cref="Capabilities"/>, never derived detail roots.
/// </summary>
public record EntityCard : IEntityCard {
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
    public bool HasSourceMedia { get; init; }

    /// <inheritdoc />
    public required IReadOnlyList<EntityCapability> Capabilities { get; init; }

    /// <inheritdoc />
    public required IReadOnlyList<EntityGroup> ChildrenByKind { get; init; }

    /// <inheritdoc />
    public required IReadOnlyList<EntityGroup> Relationships { get; init; }
}

/// <summary>
/// Query parameters for the shared entity list endpoint.
/// </summary>
public sealed record EntityListQuery {
    public string? Kind { get; init; }
    public string? Query { get; init; }
    public string? Cursor { get; init; }
    public bool? HideNsfw { get; init; }
    public int? Limit { get; init; }
    public Guid? ReferencedBy { get; init; }
    public string? RelationshipCode { get; init; }
    public EntityListSort? Sort { get; init; }
    public EntitySortDirection? SortDirection { get; init; }
    public int? Seed { get; init; }
    public bool? Favorite { get; init; }
    public bool? Organized { get; init; }
    public int? RatingMin { get; init; }
    public int? RatingMax { get; init; }
    public bool? Unrated { get; init; }
    public string? Status { get; init; }
    public string? BookType { get; init; }
    public string? BookFormat { get; init; }
    public bool? Nsfw { get; init; }
    public bool? HasFile { get; init; }
    public bool? Engaged { get; init; }
    public bool? Orphaned { get; init; }

    /// <summary>Filters by the latest acquisition lifecycle state linked to the entity.</summary>
    public AcquisitionStatus? AcquisitionStatus { get; init; }

    /// <summary>Filters to wanted placeholders (true), or excludes them (false). Null includes both.</summary>
    public bool? Wanted { get; init; }
}

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
