using Prismedia.Application.Requests;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>
/// Prepares one request kind's reviewed wanted work for connected-manager fulfillment. Implementations are
/// discovered by request kind, so the reviewed intake never branches on particular kinds.
/// </summary>
public interface IManagedWantedWorkPreparer {
    #region Variables

    /// <summary>Request kind this preparer reviews and materializes.</summary>
    RequestMediaKind Kind { get; }

    #endregion

    #region Abstract Methods

    /// <summary>Validates a complete review and derives exact manager lookup evidence without writing.</summary>
    Task<ReviewedWantedPlan> ReviewForManagerAsync(ReviewedRequestCommitRequest request, bool managerOrigin, CancellationToken token);

    /// <summary>Materializes the reviewed wanted work and reports which targets still lack local files.</summary>
    Task<ManagedWantedWork> PrepareForManagerAsync(ReviewedRequestCommitRequest request, bool managerOrigin, CancellationToken token);

    #endregion
}
