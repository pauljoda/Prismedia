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

/// <summary>Durable wait attached to a job graph.</summary>
public sealed record JobGraphSignalSnapshot(
    Guid Id,
    Guid GraphId,
    string Key,
    JobGraphSignalKind Kind,
    string? CorrelationId,
    string? Message,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? CancelledAt);

/// <summary>One dependency edge in graph detail.</summary>
public sealed record JobGraphDependencySnapshot(Guid PredecessorRunId, Guid SuccessorRunId);

/// <summary>Complete diagnostic view of one graph.</summary>
public sealed record JobGraphDetailSnapshot(
    JobGraphSnapshot Graph,
    IReadOnlyList<JobRunSnapshot> Nodes,
    IReadOnlyList<JobGraphDependencySnapshot> Dependencies,
    IReadOnlyList<JobGraphSignalSnapshot> Signals);

/// <summary>Application port for creating and expanding durable job graphs.</summary>
public interface IJobGraphService {
    Task<JobGraphSnapshot> StartAsync(StartJobGraphRequest request, CancellationToken cancellationToken);

    Task<JobRunSnapshot> AppendNodeAsync(
        Guid graphId,
        GraphJobNodeRequest request,
        CancellationToken cancellationToken);

    Task<JobGraphSignalSnapshot> OpenSignalAsync(
        Guid graphId,
        string key,
        JobGraphSignalKind kind,
        string? correlationId,
        string? message,
        CancellationToken cancellationToken);

    Task<JobGraphSignalSnapshot> ResolveSignalAsync(
        Guid graphId,
        string key,
        IReadOnlyList<GraphJobNodeRequest> continuationNodes,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<JobGraphSnapshot>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Lists graphs while excluding Entity-backed work hidden by the active NSFW policy.</summary>
    Task<IReadOnlyList<JobGraphSnapshot>> ListAsync(
        bool hideNsfw,
        CancellationToken cancellationToken) =>
        ListAsync(cancellationToken);

    Task<JobGraphDetailSnapshot?> GetAsync(Guid graphId, CancellationToken cancellationToken);

    /// <summary>Gets a graph only when its Entity-backed work is visible under the active NSFW policy.</summary>
    Task<JobGraphDetailSnapshot?> GetAsync(
        Guid graphId,
        bool hideNsfw,
        CancellationToken cancellationToken) =>
        GetAsync(graphId, cancellationToken);

    Task<bool> CancelAsync(Guid graphId, CancellationToken cancellationToken);
}
