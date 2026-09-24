using Prismedia.Application.Requests;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>
/// Prepares one request kind's reviewed wanted work for connected-manager fulfillment. Implementations are
/// discovered by request kind, so the reviewed intake never branches on particular kinds.
/// </summary>
public interface IManagedWantedWorkPreparer {
    /// <summary>Request kind this preparer reviews and materializes.</summary>
    RequestMediaKind Kind { get; }

    /// <summary>Validates a complete review and derives exact manager lookup evidence without writing.</summary>
    Task<ReviewedWantedPlan> ReviewForManagerAsync(ReviewedRequestCommitRequest request, bool managerOrigin, CancellationToken token);

    /// <summary>Materializes the reviewed wanted work and reports which targets still lack local files.</summary>
    Task<ManagedWantedWork> PrepareForManagerAsync(ReviewedRequestCommitRequest request, bool managerOrigin, CancellationToken token);
}

/// <summary>A materialized wanted work and the explicit targets that still need fulfillment.</summary>
/// <param name="EntityId">The wanted work's local identity.</param>
/// <param name="MissingTargetEntityIds">Explicit targets without local files, or null when the work is requested as a whole.</param>
/// <param name="HasEveryFile">Whether nothing is left for a manager to deliver.</param>
public sealed record ManagedWantedWork(Guid EntityId, IReadOnlyList<Guid>? MissingTargetEntityIds, bool HasEveryFile);
