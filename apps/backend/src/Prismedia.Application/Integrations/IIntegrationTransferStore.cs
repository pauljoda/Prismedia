using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Durable direct-source selection. Transient delivery URLs and credentials are resolved immediately before transfer.</summary>
public sealed record SourceTransferPlan(SourceSelection Selection, string OfferId);

/// <summary>Accepted finite transfer intent. Sensitive source locators and executor URLs require encrypted persistence.</summary>
public sealed record IntegrationTransferPlan(string Title, EntityKind EntityKind, Guid LibraryRootId, string LibraryPath,
    string OwnershipKey, string RequestFingerprint, SourceTransferPlan? Source = null, SubmitTransferInput? Executor = null);

/// <summary>Loaded work and its immutable accepted intent, independent of queue history retention.</summary>
public sealed record StoredIntegrationTransfer(IntegrationTransfer Transfer, IntegrationTransferPlan Plan,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? LastError = null);

/// <summary>Durable transfer boundary with optimistic concurrency and active fulfillment ownership.</summary>
public interface IIntegrationTransferStore {
    /// <summary>Loads one operation's full server-only intent and progress.</summary>
    Task<StoredIntegrationTransfer?> FindAsync(Guid operationId, CancellationToken cancellationToken);
    /// <summary>Lists recent operations for administrator status views; callers project only public fields.</summary>
    Task<IReadOnlyList<StoredIntegrationTransfer>> ListAsync(int limit, CancellationToken cancellationToken);
    /// <summary>Creates the intent and its first queue run atomically, or returns the identical previously accepted operation.</summary>
    Task<StoredIntegrationTransfer> CreateAsync(IntegrationTransfer transfer, IntegrationTransferPlan plan, CancellationToken cancellationToken);
    /// <summary>Persists the next domain revision and optional safe error without changing the original intent.</summary>
    Task SaveAsync(IntegrationTransfer transfer, long expectedRevision, string? error, CancellationToken cancellationToken);
    /// <summary>Records a safe attempt failure only while the saved domain revision remains current.</summary>
    Task RecordErrorAsync(Guid operationId, long expectedRevision, string error, CancellationToken cancellationToken);
    /// <summary>Queues an explicit retry of unfinished work while retaining operation, byte, and import evidence.</summary>
    Task EnqueueRetryAsync(Guid operationId, CancellationToken cancellationToken);
}

/// <summary>The operation key was reused with another intent, ownership is occupied, or the saved revision changed.</summary>
public sealed class IntegrationTransferConflictException(string message) : Exception(message);

/// <summary>The requested durable transfer no longer exists.</summary>
public sealed class IntegrationTransferNotFoundException() : Exception("The integration transfer was not found.");

/// <summary>Publishes a transfer run using the same persistence transaction as accepted intent.</summary>
public interface IIntegrationTransferScheduler {
    /// <summary>Schedules the stable operation; repeated dispatch must deduplicate an active run.</summary>
    Task EnqueueAsync(Guid operationId, string title, CancellationToken cancellationToken);
}

/// <summary>The persistent key ring cannot decrypt an already accepted transfer intent.</summary>
public sealed class IntegrationTransferPlanUnavailableException() : Exception("The saved transfer intent could not be decrypted. Restore the persistent encryption keys before retrying.");
