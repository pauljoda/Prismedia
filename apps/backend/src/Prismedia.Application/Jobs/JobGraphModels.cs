using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs;

/// <summary>One executable node requested as part of a durable job graph.</summary>
/// <param name="NodeKey">Stable graph-local key used to make expansion idempotent.</param>
/// <param name="Job">Typed job payload and target.</param>
/// <param name="ParentRunId">Optional display parent for the node.</param>
/// <param name="DependsOn">Required predecessor job identifiers.</param>
/// <param name="Importance">Whether a terminal failure fails the graph.</param>
/// <param name="ResourceClass">CPU resource profile used by the worker scheduler.</param>
/// <param name="ResourceKey">Optional shared external or entity resource key.</param>
public sealed record GraphJobNodeRequest(
    string NodeKey,
    EnqueueJobRequest Job,
    Guid? ParentRunId = null,
    IReadOnlyCollection<Guid>? DependsOn = null,
    JobNodeImportance Importance = JobNodeImportance.Required,
    JobResourceClass ResourceClass = JobResourceClass.Light,
    string? ResourceKey = null);

/// <summary>Request to create one durable workflow and its first executable node.</summary>
public sealed record StartJobGraphRequest(
    JobGraphOrigin Origin,
    string DisplayName,
    GraphJobNodeRequest Root,
    Guid? InitiatingUserId = null,
    string? RootEntityKind = null,
    string? RootEntityId = null,
    string? ActiveKey = null);

/// <summary>Application view of a persisted job graph.</summary>
public sealed record JobGraphSnapshot(
    Guid Id,
    Guid LaneId,
    JobGraphOrigin Origin,
    JobGraphStatus Status,
    string DisplayName,
    Guid RootRunId,
    Guid? InitiatingUserId,
    string? RootEntityKind,
    string? RootEntityId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? FinishedAt = null);

/// <summary>Application port for creating and expanding durable job graphs.</summary>
public interface IJobGraphService {
    Task<JobGraphSnapshot> StartAsync(StartJobGraphRequest request, CancellationToken cancellationToken);

    Task<JobRunSnapshot> AppendNodeAsync(
        Guid graphId,
        GraphJobNodeRequest request,
        CancellationToken cancellationToken);
}
