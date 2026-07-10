using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Entities;

/// <summary>
/// Stable reference shared by every API-facing entity projection. It identifies a local entity
/// without implying that display metadata, capabilities, or relationship graphs were loaded.
/// </summary>
public interface IEntityRef {
    /// <summary>Global entity identifier.</summary>
    Guid Id { get; }

    /// <summary>Closed entity kind.</summary>
    EntityKind Kind { get; }
}

/// <summary>
/// Compact entity projection suitable for lists, grids, selectors, and relationship previews.
/// Implementations may add presentation fields while preserving this common structural summary.
/// </summary>
public interface IEntitySummary : IEntityRef {
    /// <summary>Primary display title.</summary>
    string Title { get; }

    /// <summary>Structural parent entity identifier, or null when the entity has no parent.</summary>
    Guid? ParentEntityId { get; }

    /// <summary>Optional structural order under the parent entity.</summary>
    int? SortOrder { get; }

    /// <summary>Whether this Entity or any structural descendant owns source media on disk.</summary>
    bool HasSourceMedia { get; }
}

/// <summary>
/// Fully projected entity document used by detail surfaces. It extends the summary with shared
/// capabilities plus structural and non-structural entity groups.
/// </summary>
public interface IEntityDocument : IEntitySummary {
    /// <summary>Shared capabilities projected for the entity.</summary>
    IReadOnlyList<EntityCapability> Capabilities { get; }

    /// <summary>Generic structural child groups keyed by entity kind.</summary>
    IReadOnlyList<EntityGroup> ChildrenByKind { get; }

    /// <summary>Generic non-structural relationship groups keyed by entity kind.</summary>
    IReadOnlyList<EntityGroup> Relationships { get; }
}
