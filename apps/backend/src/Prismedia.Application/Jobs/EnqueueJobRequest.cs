using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs;

/// <summary>
/// Request to enqueue a background job with optional target entity tracking and payload data.
/// </summary>
/// <param name="Type">Job type that determines which handler runs.</param>
/// <param name="PayloadJson">Optional JSON payload carried through to the handler.</param>
/// <param name="TargetEntityKind">Optional entity kind for display and deduplication (e.g. "video", "library-root").</param>
/// <param name="TargetEntityId">Optional entity identifier for display and deduplication.</param>
/// <param name="TargetLabel">Optional human-readable label shown on the dashboard.</param>
/// <param name="Priority">Higher values are claimed first. Defaults to zero.</param>
/// <param name="Lane">Optional queue lane for work that needs dedicated foreground worker selection.</param>
public sealed record EnqueueJobRequest(
    JobType Type,
    string? PayloadJson = null,
    string? TargetEntityKind = null,
    string? TargetEntityId = null,
    string? TargetLabel = null,
    int Priority = 0,
    JobRunLane? Lane = null) {
    /// <summary>
    /// Creates a queue request for a Prismedia entity target using the canonical entity-kind code.
    /// </summary>
    public static EnqueueJobRequest ForEntity(
        JobType type,
        EntityKind kind,
        string entityId,
        string? label,
        int priority = 0,
        string? payloadJson = null,
        JobRunLane? lane = null) =>
        new(
            type,
            payloadJson,
            kind.ToCode(),
            entityId,
            label,
            priority,
            lane);
}
