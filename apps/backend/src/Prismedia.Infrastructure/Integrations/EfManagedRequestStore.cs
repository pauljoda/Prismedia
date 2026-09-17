using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Prismedia.Application.Entities;
using Prismedia.Application.Integrations;
using Prismedia.Application.Jobs;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Commits request ownership, creation fences, and exact source bindings using the same lifecycle and queue boundaries as library tracking.</summary>
public sealed partial class EfManagedRequestStore(PrismediaDbContext db, IExternalLibraryMountStore mounts,
    IJobQueueService queue, IEntityLifecycleMutationLease lifecycle) : IManagedRequestStore {
    private static readonly JsonSerializerOptions Json = PluginProcessTransport.JsonOptions;

    /// <inheritdoc />
    public async Task<ManagedRequestTarget> RequireTargetAsync(Guid connectionId, Guid entityId, Guid libraryRootId, CancellationToken token) {
        var movieCode = EntityKind.Movie.ToCode();
        var entity = await db.Entities.AsNoTracking().SingleOrDefaultAsync(row => row.Id == entityId && row.KindCode == movieCode, token);
        if (entity is not { IsWanted: true } || await db.EntityFiles.AnyAsync(file => file.EntityId == entityId
            && (file.Role == EntityFileRole.Source || file.Role == EntityFileRole.UnavailableSource), token))
            throw new ArgumentException("Choose a wanted movie without a retained source. Existing files can be linked through connected-library tracking.");
        var mount = (await mounts.ListAsync(connectionId, token)).SingleOrDefault(item => item.LibraryRootId == libraryRootId)
            ?? throw new ArgumentException("Choose a library mapped to this connection.");
        if (!await db.LibraryRoots.AnyAsync(root => root.Id == libraryRootId && root.Enabled && root.ScanVideos, token)
            || await db.EntityLibraryRoots.AnyAsync(root => root.EntityId == entityId && root.LibraryRootId != libraryRootId, token))
            throw new ArgumentException("Enable the mapped video library and resolve any previous library association first.");
        var identity = await db.EntityExternalIds.AsNoTracking().SingleOrDefaultAsync(row => row.EntityId == entityId && row.Provider == ExternalIdProviders.Tmdb, token);
        if (identity is null || string.IsNullOrWhiteSpace(identity.Value)) throw new ArgumentException("Identify this wanted movie with an exact TMDB identity first.");
        return new(entityId, entity.Title, new(EntityKind.Movie, new Dictionary<string, string> { [ExternalIdProviders.Tmdb] = identity.Value }), mount);
    }
    /// <inheritdoc />
    public async Task<StoredManagedRequest?> FindAsync(Guid id, CancellationToken token) =>
        await db.ManagedRequests.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token) is { } row ? Map(row) : null;
    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredManagedRequest>> ListAsync(Guid connectionId, CancellationToken token) =>
        (await db.ManagedRequests.AsNoTracking().Where(row => row.ConnectionId == connectionId).OrderByDescending(row => row.CreatedAt)
            .Take(100).ToArrayAsync(token)).Select(Map).ToArray();
    /// <inheritdoc />
    public async Task<StoredManagedRequest> CreateAsync(ManagedRequestOperation operation, ManagedRequestPlan plan, CancellationToken token) {
        if (await FindAsync(operation.State.OperationId, token) is { } existing) return Same(existing, operation, plan);
        var state = operation.State;
        if (state.Revision != 1 || state.Phase != ManagedRequestPhase.PendingCreation || plan.Request.OperationId != state.OperationId
            || plan.Request.EntityId != state.EntityId || plan.Request.LibraryRootId != state.LibraryRootId || plan.Creation.OperationId != state.OperationId
            || plan.Creation.ProfileId != plan.Request.ProfileId || !ManagedRequestIdentity.SameWork(plan.Creation.Work, plan.Request.ReviewedWork)
            || plan.Fingerprint != ManagedRequestIdentity.Fingerprint(plan.Request)) throw new ArgumentException("Invalid managed request intent.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var now = DateTimeOffset.UtcNow;
        var row = new ManagedRequestRow { Id = state.OperationId, ConnectionId = state.ConnectionId, EntityId = state.EntityId,
            LibraryRootId = state.LibraryRootId, Revision = state.Revision, Phase = state.Phase, StateJson = JsonSerializer.Serialize(state, Json),
            PlanJson = JsonSerializer.Serialize(plan, Json), CreatedAt = now, UpdatedAt = now, NextCheckAt = now.AddSeconds(30) };
        try {
            if (!await lifecycle.ExecuteAsync(state.EntityId, async ct => {
                await RequireBoundaryAsync(operation, plan, false, ct);
                if (await db.ManagedHoldings.AnyAsync(holding => holding.Id == row.Id, ct)
                    || await db.ManagedControls.AnyAsync(action => action.Id == row.Id, ct))
                    throw new ManagedRequestConflictException("This operation ID already belongs to another managed action.");
                db.ManagedRequests.Add(row);
                await new EfFulfillmentReservationStore(db).ReserveAsync(row.Id, FulfillmentOwnerKind.ExternalManager, row.ConnectionId, row.EntityId, null, ct);
                await db.SaveChangesAsync(ct);
                await PublishAsync(row, ct);
            }, token)) throw new EntityLifecycleMutationConflictException(state.EntityId);
            await transaction.CommitAsync(token);
        } catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) {
            await transaction.RollbackAsync(token); db.ChangeTracker.Clear();
            if (await FindAsync(row.Id, token) is { } accepted) return Same(accepted, operation, plan);
            throw new ManagedRequestConflictException("This request conflicts with an existing managed operation.");
        } catch (Exception error) when (FulfillmentOwnershipViolation.IsConflict(error)) {
            await transaction.RollbackAsync(token); db.ChangeTracker.Clear(); throw new FulfillmentOwnershipConflictException(error);
        }
        return Map(row);
    }
    /// <inheritdoc />
    public async Task SaveAsync(ManagedRequestOperation operation, long expectedRevision, string? problem, bool beforeDispatch, CancellationToken token) {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        if (!await lifecycle.ExecuteAsync(operation.State.EntityId, async ct => {
            var current = await LockAsync(operation.State.OperationId, expectedRevision, ct);
            if (beforeDispatch) await RequireBoundaryAsync(operation, current.Plan, true, ct);
            await UpdateAsync(operation, expectedRevision, problem, ct);
            if (operation.State.Phase == ManagedRequestPhase.Cancelled) {
                if (!current.Operation.CanCancel || await db.ManagedHoldings.AnyAsync(holding => holding.Id == operation.State.OperationId, ct))
                    throw new ManagedRequestConflictException("This request may have remote effects and cannot release ownership by cancellation.");
                await db.FulfillmentReservations.Where(owner => owner.OwnerId == operation.State.OperationId
                    && owner.OwnerKind == FulfillmentOwnerKind.ExternalManager && owner.ReleasedAt == null)
                    .ExecuteUpdateAsync(set => set.SetProperty(owner => owner.ReleasedAt, DateTimeOffset.UtcNow), ct);
            }
        }, token)) throw new EntityLifecycleMutationConflictException(operation.State.EntityId);
        await transaction.CommitAsync(token);
    }
    /// <inheritdoc />
    public async Task QueueAsync(Guid id, CancellationToken token) {
        var row = await db.ManagedRequests.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token)
            ?? throw new ManagedRequestConflictException("The request no longer exists.");
        if (Map(row).Operation.IsActive) await PublishAsync(row, token);
    }
    /// <inheritdoc />
    public async Task QueueDueAsync(CancellationToken token) {
        var now = DateTimeOffset.UtcNow;
        var due = await db.ManagedRequests.AsNoTracking().Where(row => row.NextCheckAt <= now)
            .Join(db.IntegrationConnections.Where(connection => connection.Enabled), row => row.ConnectionId, connection => connection.Id, (row, _) => row)
            .OrderBy(row => row.NextCheckAt).Take(25).ToArrayAsync(token);
        foreach (var row in due) {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            if (await db.ManagedRequests.Where(item => item.Id == row.Id && item.Revision == row.Revision && item.NextCheckAt <= now)
                .ExecuteUpdateAsync(set => set.SetProperty(item => item.NextCheckAt, now.AddSeconds(30)), token) == 1) await PublishAsync(row, token);
            await transaction.CommitAsync(token);
        }
    }
    private async Task<ManagedRequestTarget> RequireBoundaryAsync(ManagedRequestOperation operation, ManagedRequestPlan plan, bool requireOwner, CancellationToken token) {
        var state = operation.State;
        var target = await RequireTargetAsync(state.ConnectionId, state.EntityId, state.LibraryRootId, token);
        if (!ManagedRequestIdentity.SameWork(target.Work, plan.Creation.Work) || target.Mount.RemoteRootId != plan.Creation.RootId
            || target.Mount.RemotePath != plan.Creation.ExpectedRootPath) throw new ManagedRequestConflictException("The accepted wanted identity or library boundary changed.");
        if (requireOwner && !await db.FulfillmentReservations.AnyAsync(owner => owner.OwnerId == state.OperationId
            && owner.OwnerKind == FulfillmentOwnerKind.ExternalManager && owner.ConnectionId == state.ConnectionId && owner.EntityId == state.EntityId && owner.ReleasedAt == null, token))
            throw new ManagedRequestConflictException("The request no longer owns fulfillment for this wanted movie.");
        return target;
    }
    private async Task<StoredManagedRequest> LockAsync(Guid id, long revision, CancellationToken token) {
        var row = (await db.ManagedRequests.FromSqlInterpolated($"SELECT * FROM managed_requests WHERE id = {id} FOR UPDATE").AsNoTracking().ToArrayAsync(token)).SingleOrDefault();
        if (row is null || row.Revision != revision) throw Conflict();
        return Map(row);
    }
    private async Task UpdateAsync(ManagedRequestOperation operation, long revision, string? problem, CancellationToken token) {
        var state = operation.State;
        if (state.Revision != revision + 1) throw Conflict();
        var serialized = JsonSerializer.Serialize(state, Json);
        var now = DateTimeOffset.UtcNow;
        var next = operation.IsActive && !state.ReviewRequired ? now.AddSeconds(30) : (DateTimeOffset?)null;
        var safeProblem = problem is { Length: > 4096 } ? problem[..4096] : problem;
        if (await db.ManagedRequests.Where(row => row.Id == state.OperationId && row.Revision == revision && row.ConnectionId == state.ConnectionId
            && row.EntityId == state.EntityId && row.LibraryRootId == state.LibraryRootId)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.Revision, state.Revision).SetProperty(row => row.Phase, state.Phase)
                .SetProperty(row => row.StateJson, serialized).SetProperty(row => row.UpdatedAt, now).SetProperty(row => row.Problem, safeProblem)
                .SetProperty(row => row.NextCheckAt, next), token) != 1) throw Conflict();
    }
    private async Task PublishAsync(ManagedRequestRow row, CancellationToken token) {
        await queue.DeclareResourceAsync(JobResourceKeys.LibraryScan, 1, TimeSpan.Zero, token);
        if (!await queue.HasPendingAsync(JobType.ManagedLibraryReconcile, row.Id.ToString(), token))
            await queue.EnqueueAsync(new EnqueueJobRequest(JobType.ManagedLibraryReconcile, TargetEntityKind: JobTargetKinds.ManagedHolding,
                TargetEntityId: row.Id.ToString(), TargetLabel: Map(row).Plan.Title, ResourceKey: JobResourceKeys.LibraryScan), token);
    }
    private static StoredManagedRequest Map(ManagedRequestRow row) {
        var state = JsonSerializer.Deserialize<ManagedRequestState>(row.StateJson, Json) ?? throw new InvalidDataException("Invalid managed request state.");
        var plan = JsonSerializer.Deserialize<ManagedRequestPlan>(row.PlanJson, Json) ?? throw new InvalidDataException("Invalid managed request intent.");
        if (state.OperationId != row.Id || state.ConnectionId != row.ConnectionId || state.EntityId != row.EntityId
            || state.LibraryRootId != row.LibraryRootId || state.Revision != row.Revision || state.Phase != row.Phase)
            throw new InvalidDataException("Inconsistent managed request identity.");
        return new(new(state), plan, row.CreatedAt, row.UpdatedAt, row.Problem);
    }
    private static StoredManagedRequest Same(StoredManagedRequest existing, ManagedRequestOperation operation, ManagedRequestPlan plan) {
        if (existing.Operation.State.ConnectionId != operation.State.ConnectionId || existing.Plan.Fingerprint != plan.Fingerprint) throw Conflict();
        return existing;
    }
    private static ManagedRequestConflictException Conflict() => new("This managed request changed. Reload its progress before continuing.");
}
