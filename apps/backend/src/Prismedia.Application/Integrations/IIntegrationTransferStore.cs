using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Durable transfer boundary with optimistic concurrency and active fulfillment ownership.</summary>
public interface IIntegrationTransferStore {
    #region Abstract Methods

    /// <summary>Loads one operation's full server-only intent and progress.</summary>
    Task<StoredIntegrationTransfer?> FindAsync(Guid operationId, CancellationToken cancellationToken);

    /// <summary>Lists recent operations for administrator status views; callers project only public fields.</summary>
    Task<IReadOnlyList<StoredIntegrationTransfer>> ListAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Creates the intent and its first queue run atomically, or returns the identical previously accepted operation.</summary>
    Task<StoredIntegrationTransfer> CreateAsync(IntegrationTransfer transfer, IntegrationTransferPlan plan,
        CancellationToken cancellationToken);

    /// <summary>Persists the next domain revision and optional safe error without changing the original intent.</summary>
    Task SaveAsync(IntegrationTransfer transfer, long expectedRevision, string? error, CancellationToken cancellationToken);

    /// <summary>Records a safe attempt failure only while the saved domain revision remains current.</summary>
    Task RecordErrorAsync(Guid operationId, long expectedRevision, string error, CancellationToken cancellationToken);

    /// <summary>Queues an explicit retry of unfinished work while retaining operation, byte, and import evidence.</summary>
    Task EnqueueRetryAsync(Guid operationId, CancellationToken cancellationToken);

    #endregion

    #region Actions - Persistence

    /// <summary>Atomically saves a control intent and ensures its durable reconciliation run exists.</summary>
    Task SaveAndEnqueueAsync(IntegrationTransfer transfer, long expectedRevision, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This store does not support atomic control intents.");

    #endregion
}
