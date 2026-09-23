using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Prismedia.Application.Integrations;
using Prismedia.Application.Jobs;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Persists action intent and dispatch fences independently of transient manager and queue history.</summary>
public sealed class EfManagedControlStore(PrismediaDbContext db, IManagedTrackingStore tracking, IJobQueueService queue) : IManagedControlStore {
    private static readonly JsonSerializerOptions Json = PluginProcessTransport.JsonOptions;
    /// <inheritdoc />
    public async Task<OwnedManagedControlScope> RequireScopeAsync(Guid connectionId, Guid holdingId, CancellationToken token) {
        var holding = await RequireHoldingAsync(connectionId, holdingId, token);
        return ManagedControlIdentity.From(holding);
    }
    /// <inheritdoc />
    public async Task<OwnedManagedControlScope> RequireScopeAsync(Guid connectionId, Guid holdingId,
        IReadOnlyList<Guid> entityIds, CancellationToken token) {
        if (entityIds.Count == 0 || entityIds.Any(id => id == Guid.Empty)
            || entityIds.Distinct().Count() != entityIds.Count)
            throw new ManagedControlConflictException("Choose an exact non-empty append scope.");
        var holding = await RequireHoldingAsync(connectionId, holdingId, token);
        return ManagedControlIdentity.From(holding, entityIds);
    }
    private async Task<ManagedTrackingResponse> RequireHoldingAsync(Guid connectionId, Guid holdingId, CancellationToken token) {
        var holding = (await tracking.FindAsync(holdingId, token))?.Tracking;
        if (holding is null || holding.ConnectionId != connectionId || holding.Status is not (ManagedTrackingStatus.Tracking
                or ManagedTrackingStatus.WaitingForFiles or ManagedTrackingStatus.Removed)
            || holding.Targets.Count == 0)
            throw new ManagedControlConflictException("Refresh and verify this holding's tracked associations before changing its manager settings.");
        var entityIds = holding.Targets.Select(binding => binding.EntityId).Distinct().ToArray();
        var sourceTargets = holding.Bindings.SelectMany(file => file.Entities)
            .Select(binding => new ManagedTargetBinding(binding.Target, binding.EntityId))
            .OrderBy(binding => binding.Target.RemoteTargetId, StringComparer.Ordinal)
            .ToArray();
        var retainedTargets = holding.Targets
            .OrderBy(binding => binding.Target.RemoteTargetId, StringComparer.Ordinal)
            .ToArray();
        var retainedByRemoteId = retainedTargets.ToDictionary(
            binding => binding.Target.RemoteTargetId,
            StringComparer.Ordinal);
        if (entityIds.Length != holding.Targets.Count || holding.Targets.Select(binding => binding.Target.RemoteTargetId).Distinct(StringComparer.Ordinal).Count() != holding.Targets.Count
            || sourceTargets.Select(binding => binding.Target.RemoteTargetId).Distinct(StringComparer.Ordinal).Count() != sourceTargets.Length
            || sourceTargets.Any(binding => !retainedByRemoteId.TryGetValue(binding.Target.RemoteTargetId, out var retained)
                || retained != binding)
            || holding.Status == ManagedTrackingStatus.Tracking && !sourceTargets.SequenceEqual(retainedTargets))
            throw new ManagedControlConflictException("The holding's source associations no longer match its retained target identities.");
        var linkedEntityIds = holding.Bindings.SelectMany(file => file.Entities)
            .Select(binding => binding.EntityId).Distinct().ToArray();
        var reservationIds = await new ManagedReservationScopeResolver(db).ResolveAsync(entityIds, holding.Item, token);
        var reserved = await db.FulfillmentReservations.AsNoTracking().Where(row => row.OwnerId == holdingId
            && (row.OwnerKind == FulfillmentOwnerKind.ExternalManager
                || row.OwnerKind == FulfillmentOwnerKind.ConnectedLibrary)
            && row.ConnectionId == connectionId && row.ReleasedAt == null)
            .Select(row => new { row.EntityId, row.BookRendition, row.OwnerKind }).ToArrayAsync(token);
        if (!reserved.Where(row => row.BookRendition == holding.Item.BookRendition
                && (row.OwnerKind == FulfillmentOwnerKind.ExternalManager
                    || holding.Item.EntityKind == EntityKind.Book && linkedEntityIds.Length > 0
                    || linkedEntityIds.Contains(row.EntityId))).Select(row => row.EntityId)
                .ToHashSet().SetEquals(reservationIds)
            || linkedEntityIds.Except(entityIds).Any()
            || !await db.ExternalLibraryMounts.AnyAsync(row => row.ConnectionId == connectionId
            && row.LibraryRootId == holding.LibraryRootId, token))
            throw new ManagedControlConflictException("The holding no longer has its reviewed library mapping and fulfillment ownership.");
        return holding;
    }
    /// <inheritdoc />
    public async Task<StoredManagedControl?> FindAsync(Guid id, CancellationToken token) =>
        await db.ManagedControls.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token) is { } row ? Map(row) : null;
    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredManagedControl>> ListAsync(Guid connectionId, Guid holdingId, CancellationToken token) =>
        (await db.ManagedControls.AsNoTracking().Where(row => row.ConnectionId == connectionId && row.HoldingId == holdingId)
            .OrderByDescending(row => row.CreatedAt).Take(100).ToArrayAsync(token)).Select(Map).ToArray();
    /// <inheritdoc />
    public async Task<StoredManagedControl> CreateAsync(ManagedControlOperation operation, ManagedControlPlan plan, CancellationToken token) {
        if (await FindAsync(operation.State.OperationId, token) is { } existing) return Same(existing, operation, plan);
        if (operation.State.Revision != 1 || plan.Request.OperationId != operation.State.OperationId
            || plan.RequestFingerprint != ManagedControlIdentity.RequestFingerprint(plan.Request)) throw new ArgumentException("Invalid manager action intent.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await PluginLifecycleLease.LockConnectionAsync(db, operation.State.ConnectionId, token, requireReady: true);
        await ValidateScopeAsync(operation, plan, token);
        var now = DateTimeOffset.UtcNow;
        var row = new ManagedControlRow { Id = operation.State.OperationId, ConnectionId = operation.State.ConnectionId,
            HoldingId = operation.State.HoldingId, ActiveHoldingId = operation.State.HoldingId, Revision = 1,
            StateJson = JsonSerializer.Serialize(operation.State, Json), PlanJson = JsonSerializer.Serialize(plan, Json),
            CreatedAt = now, UpdatedAt = now, NextCheckAt = now.AddSeconds(30) };
        db.ManagedControls.Add(row);
        try {
            await db.SaveChangesAsync(token);
            await PublishAsync(row, token);
            await transaction.CommitAsync(token);
        } catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) {
            await transaction.RollbackAsync(token); db.ChangeTracker.Clear();
            if (await FindAsync(row.Id, token) is { } accepted) return Same(accepted, operation, plan);
            throw new ManagedControlConflictException("This holding already has an unfinished manager action. Review that action first.");
        }
        return Map(row);
    }
    /// <inheritdoc />
    public async Task SaveAsync(ManagedControlOperation operation, long expectedRevision, string? problem, bool beforeDispatch, CancellationToken token) {
        var stored = await FindAsync(operation.State.OperationId, token) ?? throw new ManagedControlConflictException("The manager action no longer exists.");
        if (stored.Operation.State.ConnectionId != operation.State.ConnectionId || stored.Operation.State.HoldingId != operation.State.HoldingId
            || stored.Operation.State.Revision != expectedRevision || operation.State.Revision != expectedRevision + 1) throw Conflict();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        if (beforeDispatch) await ValidateScopeAsync(operation, stored.Plan, token);
        var stateJson = JsonSerializer.Serialize(operation.State, Json);
        var now = DateTimeOffset.UtcNow;
        var next = operation.IsActive && !operation.State.ReviewRequired ? now.AddSeconds(30) : (DateTimeOffset?)null;
        var activeId = operation.IsActive ? operation.State.HoldingId : (Guid?)null;
        var safeProblem = problem is { Length: > 4096 } ? problem[..4096] : problem;
        var count = await db.ManagedControls.Where(row => row.Id == operation.State.OperationId && row.Revision == expectedRevision)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.Revision, operation.State.Revision)
                .SetProperty(row => row.StateJson, stateJson).SetProperty(row => row.ActiveHoldingId, activeId)
                .SetProperty(row => row.Problem, safeProblem).SetProperty(row => row.UpdatedAt, now).SetProperty(row => row.NextCheckAt, next), token);
        if (count != 1) throw Conflict();
        await transaction.CommitAsync(token);
    }
    /// <inheritdoc />
    public async Task QueueAsync(Guid id, CancellationToken token) {
        var row = await db.ManagedControls.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token)
            ?? throw new ManagedControlConflictException("The manager action no longer exists.");
        if (Map(row).Operation.IsActive) await PublishAsync(row, token);
    }
    /// <inheritdoc />
    public async Task QueueDueAsync(CancellationToken token) {
        var now = DateTimeOffset.UtcNow;
        var due = await db.ManagedControls.AsNoTracking().Where(row => row.NextCheckAt <= now && row.ActiveHoldingId != null)
            .Join(db.IntegrationConnections.Where(connection => connection.Enabled), row => row.ConnectionId, connection => connection.Id, (row, _) => row)
            .OrderBy(row => row.NextCheckAt).Take(25).ToArrayAsync(token);
        foreach (var row in due) {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            if (await db.ManagedControls.Where(item => item.Id == row.Id && item.Revision == row.Revision && item.NextCheckAt <= now)
                .ExecuteUpdateAsync(set => set.SetProperty(item => item.NextCheckAt, now.AddSeconds(30)), token) == 1)
                await PublishAsync(row, token);
            await transaction.CommitAsync(token);
        }
    }
    private async Task ValidateScopeAsync(ManagedControlOperation operation, ManagedControlPlan plan, CancellationToken token) {
        // Serializes validation with holding reconciliation. No network call occurs inside this transaction.
        await db.ManagedHoldings.FromSqlInterpolated($"SELECT * FROM managed_holdings WHERE id = {operation.State.HoldingId} FOR UPDATE").AsNoTracking().ToArrayAsync(token);
        var current = plan.ScopeEntityIds is { Count: > 0 } subset
            ? await RequireScopeAsync(operation.State.ConnectionId, operation.State.HoldingId, subset, token)
            : await RequireScopeAsync(operation.State.ConnectionId, operation.State.HoldingId, token);
        if (current.Fingerprint != plan.Request.ScopeFingerprint)
            throw new ManagedControlConflictException("The reviewed target associations changed. Refresh the holding before creating another action.");
    }
    private async Task PublishAsync(ManagedControlRow row, CancellationToken token) {
        var key = JobResourceKeys.ManagerConnection(row.ConnectionId);
        await queue.DeclareResourceAsync(key, 1, TimeSpan.Zero, token);
        if (!await queue.HasPendingAsync(JobType.ManagedControl, row.Id.ToString(), token))
            await queue.EnqueueAsync(new EnqueueJobRequest(JobType.ManagedControl, TargetEntityKind: JobTargetKinds.ManagedControl,
                TargetEntityId: row.Id.ToString(), TargetLabel: "Manager action", ResourceKey: key), token);
    }
    private static StoredManagedControl Map(ManagedControlRow row) {
        var state = JsonSerializer.Deserialize<ManagedControlOperationState>(row.StateJson, Json) ?? throw new InvalidDataException("Invalid manager action state.");
        var plan = JsonSerializer.Deserialize<ManagedControlPlan>(row.PlanJson, Json) ?? throw new InvalidDataException("Invalid manager action intent.");
        if (state.OperationId != row.Id || state.HoldingId != row.HoldingId || state.ConnectionId != row.ConnectionId || state.Revision != row.Revision)
            throw new InvalidDataException("Inconsistent manager action identity.");
        return new(new(state), plan, row.CreatedAt, row.UpdatedAt, row.Problem);
    }
    private static StoredManagedControl Same(StoredManagedControl existing, ManagedControlOperation operation, ManagedControlPlan plan) {
        if (existing.Operation.State.ConnectionId != operation.State.ConnectionId || existing.Operation.State.HoldingId != operation.State.HoldingId
            || existing.Plan.RequestFingerprint != plan.RequestFingerprint) throw new ManagedControlConflictException("This operation ID already accepted a different manager action.");
        return existing;
    }
    private static ManagedControlConflictException Conflict() => new("This manager action changed. Reload its progress before continuing.");
}
