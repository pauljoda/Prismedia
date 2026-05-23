namespace Prismedia.Contracts.Jobs;

/// <summary>
/// API-facing operation row used by the jobs dashboard.
/// </summary>
/// <param name="Id">Job run identifier.</param>
/// <param name="Type">Queue type code (e.g. "scan-library", "probe-video").</param>
/// <param name="Status">Current job status code.</param>
/// <param name="Progress">Progress percentage from 0 through 100.</param>
/// <param name="Message">Optional status, completion, or failure message.</param>
/// <param name="TargetKind">Entity kind for display (e.g. "library-root", "video").</param>
/// <param name="TargetId">Entity identifier for display.</param>
/// <param name="TargetLabel">Human-readable label shown on the dashboard.</param>
/// <param name="CreatedAt">Time the job was created.</param>
/// <param name="StartedAt">Time the job started, when claimed.</param>
/// <param name="FinishedAt">Time the job finished, when complete or failed.</param>
public sealed record JobRun(
    Guid Id,
    string Type,
    string Status,
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
/// <param name="Type">Job type code (e.g. "scan-library").</param>
/// <param name="Status">Job status code (e.g. "queued", "running").</param>
/// <param name="Count">Number of job runs with this type and status.</param>
public sealed record JobQueueCountDto(string Type, string Status, int Count);

/// <summary>
/// API response containing job runs for the operations dashboard.
/// </summary>
/// <param name="Items">Recent job runs (most recent first, capped for dashboard display).</param>
/// <param name="Counts">Aggregate counts per type and status across all job runs.</param>
public sealed record JobListResponse(IReadOnlyList<JobRun> Items, IReadOnlyList<JobQueueCountDto> Counts);

/// <summary>
/// API response returned after creating a new job run.
/// </summary>
/// <param name="Job">The created job run.</param>
public sealed record JobCreateResponse(JobRun Job);

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
/// <param name="Skipped">Number of entities skipped because a pending job already exists.</param>
public sealed record BulkJobResponse(int Enqueued, int Skipped);
