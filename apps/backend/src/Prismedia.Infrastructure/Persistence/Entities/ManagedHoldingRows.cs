using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Persistence.Entities;

/// <summary>Durable reviewed intent for one connected holding; remote IDs are scoped to its connection.</summary>
public sealed class ManagedHoldingRow {
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid LibraryRootId { get; set; }
    public EntityKind Kind { get; set; }
    public string RemoteId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ItemJson { get; set; } = "{}";
    public string SelectionsJson { get; set; } = "[]";
    /// <summary>Stable local target identities retained before and across source-file availability.</summary>
    public string TargetsJson { get; set; } = "[]";
    /// <summary>Last provider/identity configuration for which people enrichment reached a terminal result.</summary>
    public string? PeopleEnrichmentFingerprint { get; set; }
    /// <summary>When credits were found locally or safely applied from an exact external identity.</summary>
    public DateTimeOffset? PeopleEnrichmentCompletedAt { get; set; }
    public ManagedTrackingStatus Status { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public DateTimeOffset NextCheckAt { get; set; }
    public string? Problem { get; set; }
    /// <summary>Stable explicit handoff operation, retained after release for safe request replay.</summary>
    public Guid? ReleaseOperationId { get; set; }
    /// <summary>Reviewed scope and path, persisted before freezing host effects.</summary>
    public string? ReleaseRequestJson { get; set; }
    /// <summary>Completion of the verified handoff, never set by disabling a connection.</summary>
    public DateTimeOffset? ReleasedAt { get; set; }
    /// <summary>Historical file associations retained after active bindings have been removed.</summary>
    public string ReleasedBindingsJson { get; set; } = "[]";
}

/// <summary>One stable external target attached to an existing local entity and its retained file row.</summary>
public sealed class ManagedSourceBindingRow {
    public Guid Id { get; set; }
    public Guid HoldingId { get; set; }
    public string RemoteTargetId { get; set; } = string.Empty;
    public EntityKind Kind { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public int? AbsoluteNumber { get; set; }
    public string? IssueLabel { get; set; }
    public Guid EntityId { get; set; }
    public Guid SourceFileId { get; set; }
    public string RemoteFileId { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset WrittenAt { get; set; }
    public bool IsAvailable { get; set; }
}
