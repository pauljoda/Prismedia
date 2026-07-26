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
/// <param name="Origin">Interactive or background scheduling pool for a root operation.</param>
/// <param name="NodeKey">Optional stable graph-local key for idempotent child expansion.</param>
/// <param name="Importance">Optional override for required versus best-effort graph behavior.</param>
/// <param name="ResourceClass">Optional override for the job type's CPU resource profile.</param>
/// <param name="ResourceKey">Optional shared external or entity resource key.</param>
/// <param name="GraphRootEntityKind">Optional top-level Entity kind for a root graph when the executable node targets another durable record.</param>
/// <param name="GraphRootEntityId">Optional top-level Entity id for a root graph when the executable node targets another durable record.</param>
public sealed record EnqueueJobRequest(
    JobType Type,
    string? PayloadJson = null,
    string? TargetEntityKind = null,
    string? TargetEntityId = null,
    string? TargetLabel = null,
    JobGraphOrigin Origin = JobGraphOrigin.Background,
    string? NodeKey = null,
    JobNodeImportance? Importance = null,
    JobResourceClass? ResourceClass = null,
    string? ResourceKey = null,
    string? GraphRootEntityKind = null,
    string? GraphRootEntityId = null) {
    /// <summary>
    /// Creates a queue request for a Prismedia entity target using the canonical entity-kind code.
    /// </summary>
    public static EnqueueJobRequest ForEntity(
        JobType type,
        EntityKind kind,
        string entityId,
        string? label,
        string? payloadJson = null,
        JobGraphOrigin origin = JobGraphOrigin.Background) =>
        new(
            type,
            payloadJson,
            kind.ToCode(),
            entityId,
            label,
            origin);
}
