using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs;

/// <summary>
/// Application-layer view of a durable job run with typed queue classification values.
/// </summary>
/// <param name="Id">Job run identifier.</param>
/// <param name="Type">Typed operation that the worker should execute.</param>
/// <param name="Status">Typed lifecycle status for dashboard and worker decisions.</param>
/// <param name="Progress">Progress percentage from 0 through 100.</param>
/// <param name="Message">Optional status, completion, or failure message.</param>
/// <param name="PayloadJson">JSON payload carried through to the handler.</param>
/// <param name="TargetEntityKind">Entity kind for display and deduplication.</param>
/// <param name="TargetEntityId">Entity identifier for display and deduplication.</param>
/// <param name="TargetLabel">Human-readable label shown on the dashboard.</param>
/// <param name="CreatedAt">Time the job was created.</param>
/// <param name="StartedAt">Time the job started, when claimed.</param>
/// <param name="FinishedAt">Time the job finished, when complete or failed.</param>
/// <param name="Attempts">Number of times this run has been claimed, including the current attempt.</param>
/// <param name="MaxAttempts">Maximum attempts before the run is failed terminally rather than retried.</param>
public sealed record JobRunSnapshot(
    Guid Id,
    JobType Type,
    JobRunStatus Status,
    int Progress,
    string? Message,
    string PayloadJson,
    string? TargetEntityKind,
    string? TargetEntityId,
    string? TargetLabel,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int Attempts = 0,
    int MaxAttempts = 0) {
    /// <summary>
    /// True when the current attempt is the last the queue will run; a failure now is terminal
    /// (the run is failed, not requeued) rather than retried.
    /// </summary>
    public bool IsFinalAttempt => Attempts >= MaxAttempts;
}
