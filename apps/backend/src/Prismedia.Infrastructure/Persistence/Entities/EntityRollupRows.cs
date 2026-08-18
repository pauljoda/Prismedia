namespace Prismedia.Infrastructure.Persistence.Entities;

/// <summary>
/// Trigger-maintained rollup row for one Entity: the inherited context every count projection
/// keys on, plus per-entity structural facts the card read path previously derived live.
/// Maintained entirely by the PostgreSQL functions in <c>EntityRollupProjectionSql</c>; the
/// application only reads it.
/// </summary>
public sealed class EntityRollupRow {
    public Guid EntityId { get; set; }

    /// <summary>
    /// The library root whose visibility governs this Entity: its own assignment when present,
    /// otherwise inherited from the nearest rooted ancestor; null for rootless taxonomy.
    /// </summary>
    public Guid? EffectiveLibraryRootId { get; set; }

    /// <summary>True when the Entity or any ancestor is flagged NSFW (the inherited NSFW wall).</summary>
    public bool EffectiveIsNsfw { get; set; }

    /// <summary>Number of direct structural children, excluding wanted placeholders.</summary>
    public int DirectChildCount { get; set; }

    /// <summary>Newest creation timestamp in the Entity's structural subtree (excluding itself).</summary>
    public DateTimeOffset? LatestDescendantCreatedAt { get; set; }

    /// <summary>Last successful refresh of this persisted projection.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Trigger-maintained count of structural descendants of one kind beneath one Entity, keyed by
/// the descendants' effective library root (<c>Guid.Empty</c> when rootless) so a viewer's chip
/// sums only the roots they may see. <c>CountNsfw</c> counts the subset hidden by the NSFW wall.
/// </summary>
public sealed class EntityDescendantCountRow {
    public Guid EntityId { get; set; }
    public string DescendantKindCode { get; set; } = string.Empty;
    public Guid LibraryRootId { get; set; }
    public int CountTotal { get; set; }
    public int CountNsfw { get; set; }
}

/// <summary>
/// Trigger-maintained count of distinct entities of one kind that reference one target Entity
/// through relationship links (the person/tag/studio usage chips and reference ordering), keyed
/// by the sources' effective library root with the same semantics as descendant counts.
/// </summary>
public sealed class EntityReferenceCountRow {
    public Guid EntityId { get; set; }
    public string SourceKindCode { get; set; } = string.Empty;
    public Guid LibraryRootId { get; set; }
    public int CountTotal { get; set; }
    public int CountNsfw { get; set; }
}

/// <summary>
/// Trigger-maintained count of one collection's members, keyed by the members' effective
/// library root with the same semantics as descendant counts.
/// </summary>
public sealed class EntityCollectionMemberCountRow {
    public Guid EntityId { get; set; }
    public Guid LibraryRootId { get; set; }
    public int CountTotal { get; set; }
    public int CountNsfw { get; set; }
}
