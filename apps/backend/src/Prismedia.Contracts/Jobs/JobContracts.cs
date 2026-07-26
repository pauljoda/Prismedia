using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Jobs;

/// <summary>
/// API-facing operation row used by the jobs dashboard.
/// </summary>
/// <param name="Id">Job run identifier.</param>
/// <param name="Type">Queue operation type.</param>
/// <param name="Status">Current job lifecycle status.</param>
/// <param name="Progress">Progress percentage from 0 through 100.</param>
/// <param name="Message">Optional status, completion, or failure message.</param>
/// <param name="TargetKind">Target kind for display (e.g. "library-root", "video"); not strictly an entity kind, so kept as a free code.</param>
/// <param name="TargetId">Entity identifier for display.</param>
/// <param name="TargetLabel">Human-readable label shown on the dashboard.</param>
/// <param name="CreatedAt">Time the job was created.</param>
/// <param name="StartedAt">Time the job started, when claimed.</param>
/// <param name="FinishedAt">Time the job finished, when complete or failed.</param>
public sealed record JobRun(
    Guid Id,
    JobType Type,
    JobRunStatus Status,
    int Progress,
    string? Message,
    string? TargetKind,
    string? TargetId,
    string? TargetLabel,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);

/// <summary>
/// Aggregate count of job runs sharing a type and status.
/// </summary>
/// <param name="Type">Job operation type.</param>
/// <param name="Status">Job lifecycle status.</param>
/// <param name="Count">Number of job runs with this type and status.</param>
public sealed record JobQueueCountDto(JobType Type, JobRunStatus Status, int Count);

/// <summary>
/// API response containing job runs for the operations dashboard.
/// </summary>
/// <param name="Items">Recent job runs (most recent first, capped for dashboard display).</param>
/// <param name="Counts">Aggregate counts per type and status across all job runs.</param>
public sealed record JobListResponse(IReadOnlyList<JobRun> Items, IReadOnlyList<JobQueueCountDto> Counts);

/// <summary>
/// API response returned after creating a new job run.
/// </summary>
/// <param name="Job">The created root node.</param>
/// <param name="Graph">Durable graph and logical lane created for the operation.</param>
public sealed record JobCreateResponse(JobRun Job, JobGraphReference Graph);

/// <summary>Stable reference returned when an operation creates a durable graph.</summary>
public sealed record JobGraphReference(
    Guid Id,
    JobGraphOrigin Origin,
    string? RootEntityKind,
    string? RootEntityId,
    JobRun InitialNode);

/// <summary>
/// API response returned after cancelling queued or running job runs.
/// </summary>
/// <param name="Cancelled">Number of job runs moved into the cancelled state.</param>
public sealed record JobCancelResponse(int Cancelled);

/// <summary>
/// API response returned after clearing failed job runs from the active failure list.
/// </summary>
/// <param name="Cleared">Number of failed job runs moved into the cancelled state.</param>
public sealed record JobFailureClearResponse(int Cleared);

/// <summary>
/// API response returned after a bulk job operation such as rebuild-previews or backfill-fingerprints.
/// </summary>
/// <param name="Enqueued">Number of jobs queued.</param>
/// <param name="Skipped">Number of entities skipped because a pending graph already exists.</param>
/// <param name="Graphs">One graph reference per enqueued top-level target.</param>
public sealed record BulkJobResponse(
    int Enqueued,
    int Skipped,
    IReadOnlyList<JobGraphReference> Graphs);

/// <summary>One graph/lane row for the jobs dashboard.</summary>
public sealed record JobGraphSummary(
    Guid Id,
    JobGraphOrigin Origin,
    JobGraphStatus Status,
    string DisplayName,
    string? RootEntityKind,
    string? RootEntityId,
    int Progress,
    int NodeCount,
    int CompletedNodeCount,
    int WarningCount,
    JobType? CurrentNodeType,
    string? WaitReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? FinishedAt);

/// <summary>Executable node shown inside expanded graph detail.</summary>
public sealed record JobGraphNode(
    Guid Id,
    string? NodeKey,
    Guid? ParentRunId,
    JobType Type,
    JobRunStatus Status,
    JobNodeImportance Importance,
    JobResourceClass ResourceClass,
    string? ResourceKey,
    int Progress,
    string? Message,
    string? TargetKind,
    string? TargetId,
    string? TargetLabel,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);

/// <summary>Dependency edge between graph nodes.</summary>
public sealed record JobGraphDependency(Guid PredecessorRunId, Guid SuccessorRunId);

/// <summary>Durable graph wait surfaced to the operations UI.</summary>
public sealed record JobGraphSignal(
    Guid Id,
    string Key,
    JobGraphSignalKind Kind,
    string? CorrelationId,
    string? Message,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? CancelledAt);

/// <summary>Graph summary list response.</summary>
public sealed record JobGraphListResponse(IReadOnlyList<JobGraphSummary> Items);

/// <summary>Expanded graph response containing nodes, dependencies, and waits.</summary>
public sealed record JobGraphDetailResponse(
    JobGraphSummary Graph,
    IReadOnlyList<JobGraphNode> Nodes,
    IReadOnlyList<JobGraphDependency> Dependencies,
    IReadOnlyList<JobGraphSignal> Signals);

/// <summary>Result of graph cancellation.</summary>
public sealed record JobGraphCancelResponse(bool Cancelled);
