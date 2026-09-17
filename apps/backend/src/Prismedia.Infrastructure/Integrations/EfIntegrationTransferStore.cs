using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Prismedia.Application.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Stores accepted operations independently of queue history, encrypting source intent and enforcing active ownership.</summary>
public sealed class EfIntegrationTransferStore(PrismediaDbContext db, TransferPlanProtector protector,
    IIntegrationTransferScheduler scheduler) : IIntegrationTransferStore {
    private static readonly JsonSerializerOptions Json = PluginProcessTransport.JsonOptions;

    /// <inheritdoc />
    public async Task<StoredIntegrationTransfer?> FindAsync(Guid operationId, CancellationToken cancellationToken) =>
        await db.IntegrationTransfers.AsNoTracking().SingleOrDefaultAsync(row => row.Id == operationId, cancellationToken) is { } row ? Map(row) : null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredIntegrationTransfer>> ListAsync(int limit, CancellationToken cancellationToken) =>
        (await db.IntegrationTransfers.AsNoTracking().OrderByDescending(row => row.CreatedAt).Take(Math.Clamp(limit, 1, 100))
            .ToArrayAsync(cancellationToken)).Select(Map).ToArray();

    /// <inheritdoc />
    public async Task<StoredIntegrationTransfer> CreateAsync(IntegrationTransfer transfer, IntegrationTransferPlan plan, CancellationToken cancellationToken) {
        Validate(transfer, plan);
        if (await FindAsync(transfer.State.OperationId, cancellationToken) is { } existing) return SameIntent(existing, transfer, plan);
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        await PluginLifecycleLease.LockConnectionAsync(db, transfer.State.ConnectionId, cancellationToken, requireReady: true);
        var now = DateTimeOffset.UtcNow;
        var row = new IntegrationTransferRow {
            Id = transfer.State.OperationId, ConnectionId = transfer.State.ConnectionId, Revision = transfer.State.Revision,
            Phase = transfer.State.Phase, StateJson = JsonSerializer.Serialize(transfer.State, Json),
            ProtectedPlan = protector.Protect(transfer.State.ConnectionId, transfer.State.OperationId, JsonSerializer.Serialize(plan, Json)),
            ActiveOwnershipKey = plan.OwnershipKey, CreatedAt = now, UpdatedAt = now
        };
        db.IntegrationTransfers.Add(row);
        try {
            await db.SaveChangesAsync(cancellationToken);
            await scheduler.EnqueueAsync(row.Id, plan.Title, cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return Map(row);
        } catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            if (await FindAsync(transfer.State.OperationId, cancellationToken) is { } concurrent) return SameIntent(concurrent, transfer, plan);
            throw new IntegrationTransferConflictException("Another active acquisition already owns this selected content.");
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(IntegrationTransfer transfer, long expectedRevision, string? error, CancellationToken cancellationToken) {
        var row = await db.IntegrationTransfers.SingleOrDefaultAsync(item => item.Id == transfer.State.OperationId, cancellationToken)
            ?? throw new IntegrationTransferNotFoundException();
        if (row.Revision != expectedRevision || transfer.State.Revision != expectedRevision + 1 || row.ConnectionId != transfer.State.ConnectionId)
            throw new IntegrationTransferConflictException("This transfer changed while the operation was running. Reload it before retrying.");
        row.Revision = transfer.State.Revision;
        row.Phase = transfer.State.Phase;
        row.StateJson = JsonSerializer.Serialize(transfer.State, Json);
        row.LastError = error is { Length: > 4096 } ? error[..4096] : error;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        if (transfer.State.Phase is IntegrationTransferPhase.Completed or IntegrationTransferPhase.Failed or IntegrationTransferPhase.Cancelled) row.ActiveOwnershipKey = null;
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new IntegrationTransferConflictException("This transfer changed while saving its progress."); }
    }

    /// <inheritdoc />
    public async Task RecordErrorAsync(Guid operationId, long expectedRevision, string error, CancellationToken cancellationToken) {
        var row = await db.IntegrationTransfers.SingleOrDefaultAsync(item => item.Id == operationId && item.Revision == expectedRevision, cancellationToken);
        if (row is null) return;
        row.LastError = error.Length > 4096 ? error[..4096] : error;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { /* A newer progress revision owns the status. */ }
    }

    /// <inheritdoc />
    public async Task EnqueueRetryAsync(Guid operationId, CancellationToken cancellationToken) {
        var work = await FindAsync(operationId, cancellationToken) ?? throw new IntegrationTransferNotFoundException();
        if (work.Transfer.State.Phase is IntegrationTransferPhase.Completed or IntegrationTransferPhase.Cancelled or IntegrationTransferPhase.Failed)
            throw new ArgumentException("This transfer is terminal. Create a new explicit acquisition if another copy is needed.");
        await scheduler.EnqueueAsync(operationId, work.Plan.Title, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveAndEnqueueAsync(IntegrationTransfer transfer, long expectedRevision, CancellationToken cancellationToken) {
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        await SaveAsync(transfer, expectedRevision, null, cancellationToken);
        if (transfer.State.Phase != IntegrationTransferPhase.Cancelled) await EnqueueRetryAsync(transfer.State.OperationId, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    private StoredIntegrationTransfer Map(IntegrationTransferRow row) {
        var state = JsonSerializer.Deserialize<IntegrationTransferState>(row.StateJson, Json)
            ?? throw new InvalidDataException("Stored integration transfer state is invalid.");
        if (state.OperationId != row.Id || state.ConnectionId != row.ConnectionId || state.Revision != row.Revision || state.Phase != row.Phase)
            throw new InvalidDataException("Stored integration transfer identity is inconsistent.");
        var plan = JsonSerializer.Deserialize<IntegrationTransferPlan>(protector.Unprotect(row.ConnectionId, row.Id, row.ProtectedPlan), Json)
            ?? throw new InvalidDataException("Stored integration transfer intent is invalid.");
        return new(new(state), plan, row.CreatedAt, row.UpdatedAt, row.LastError);
    }

    private static StoredIntegrationTransfer SameIntent(StoredIntegrationTransfer existing, IntegrationTransfer transfer, IntegrationTransferPlan plan) {
        if (existing.Transfer.State.ConnectionId != transfer.State.ConnectionId || existing.Plan.RequestFingerprint != plan.RequestFingerprint)
            throw new IntegrationTransferConflictException("This operation ID has already accepted a different request.");
        return existing;
    }

    private static void Validate(IntegrationTransfer transfer, IntegrationTransferPlan plan) {
        if (transfer.State.Revision != 1 || string.IsNullOrWhiteSpace(plan.Title) || plan.Title.Length > 512 || !Enum.IsDefined(plan.EntityKind)
            || plan.LibraryRootId == Guid.Empty || string.IsNullOrWhiteSpace(plan.LibraryPath) || !Path.IsPathFullyQualified(plan.LibraryPath)
            || string.IsNullOrWhiteSpace(plan.OwnershipKey) || plan.OwnershipKey.Length > 256
            || plan.RequestFingerprint is not { Length: 64 } || !plan.RequestFingerprint.All(Uri.IsHexDigit)
            || (plan.Source is null) == (plan.Executor is null)
            || (transfer.State.Mode switch {
                IntegrationTransferMode.SourceDownload or IntegrationTransferMode.SourceRequest => plan.Source is null || plan.Executor is not null,
                IntegrationTransferMode.RemoteExecutor => plan.Executor is null || plan.Source is not null,
                _ => true
            }))
            throw new ArgumentException("A transfer needs one valid finite intent, destination, and request identity.");
    }
}
