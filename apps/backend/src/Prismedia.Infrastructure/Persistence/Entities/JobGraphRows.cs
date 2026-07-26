using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Persistence.Entities;

/// <summary>Persistence row for one durable workflow and its logical scheduling lane.</summary>
public sealed class JobGraphRow {
    public Guid Id { get; set; }
    public JobGraphOrigin Origin { get; set; }
    public JobGraphStatus Status { get; set; } = JobGraphStatus.Queued;
    public string DisplayName { get; set; } = string.Empty;
    public Guid RootRunId { get; set; }
    public Guid? InitiatingUserId { get; set; }
    public string? RootEntityKind { get; set; }
    public string? RootEntityId { get; set; }
    public string? ActiveKey { get; set; }
    public bool CancellationRequested { get; set; }
    public DateTimeOffset? LastDispatchedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

/// <summary>Directed success dependency between two executable nodes in one graph.</summary>
public sealed class JobDependencyRow {
    public Guid GraphId { get; set; }
    public Guid PredecessorJobRunId { get; set; }
    public Guid SuccessorJobRunId { get; set; }
}

/// <summary>Durable wait that pauses graph completion without occupying a worker.</summary>
public sealed class JobGraphSignalRow {
    public Guid Id { get; set; }
    public Guid GraphId { get; set; }
    public string Key { get; set; } = string.Empty;
    public JobGraphSignalKind Kind { get; set; }
    public string? CorrelationId { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
}

/// <summary>Durable scheduling state for a declared external resource.</summary>
public sealed class JobResourceStateRow {
    public string Key { get; set; } = string.Empty;
    public int MaxConcurrency { get; set; }
    public int MinimumStartIntervalMs { get; set; }
    public DateTimeOffset NextAvailableAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Lease connecting a claimed job node to one shared external resource.</summary>
public sealed class JobResourceLeaseRow {
    public string ResourceKey { get; set; } = string.Empty;
    public Guid JobRunId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
