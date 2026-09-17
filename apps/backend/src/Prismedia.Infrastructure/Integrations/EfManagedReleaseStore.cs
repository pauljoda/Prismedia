using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Integrations;
using Prismedia.Application.Jobs;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Freezes a holding before remote observations, then releases its owner atomically without changing library bytes.</summary>
public sealed class EfManagedReleaseStore(PrismediaDbContext db, IManagedTrackingStore tracking, IManagedControlStore controls,
    IEntityLifecycleMutationLease lifecycle) : IManagedReleaseStore {
    private static readonly JsonSerializerOptions Json = PluginProcessTransport.JsonOptions;

    /// <inheritdoc />
    public async Task<ManagedReleaseWork?> FindAsync(Guid holdingId, CancellationToken token) {
        var row = await db.ManagedHoldings.AsNoTracking().SingleOrDefaultAsync(row => row.Id == holdingId, token);
        if (row?.ReleaseOperationId is null) return null;
        var request = JsonSerializer.Deserialize<ReleaseManagedHoldingRequest>(row.ReleaseRequestJson!, Json);
        if (request is null || request.OperationId != row.ReleaseOperationId
            || row.Status is not (ManagedTrackingStatus.ReleasePending or ManagedTrackingStatus.Released))
            throw new InvalidDataException("The retained ownership release has inconsistent identity or state.");
        return new((await tracking.FindAsync(holdingId, token))!.Tracking, request);
    }

    /// <inheritdoc />
    public async Task RequireSettledControlsAsync(Guid holdingId, CancellationToken token) {
        var unresolved = JsonSerializer.Serialize(new { Phase = ManagedControlPhase.ClosedUnverified }, Json);
        if (await db.ManagedControls.AnyAsync(row => row.HoldingId == holdingId
            && (row.ActiveHoldingId != null || EF.Functions.JsonContains(row.StateJson, unresolved)), token))
            throw new ManagedControlConflictException("This holding has an unfinished or unverified manager action. Resolve its outcome before releasing ownership.");
    }

    /// <inheritdoc />
    public async Task<ManagedTrackingResponse> BeginAsync(Guid connectionId, Guid holdingId, ReleaseManagedHoldingRequest request, CancellationToken token) {
        var holding = await RequireHoldingAsync(connectionId, holdingId, token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await WithOwnersAsync(holding, async ct => {
            var row = await LockAsync(holdingId, ct);
            if (row.ReleaseOperationId is not null) {
                if (row.ReleaseOperationId != request.OperationId || JsonSerializer.Deserialize<ReleaseManagedHoldingRequest>(row.ReleaseRequestJson!, Json) != request)
                    throw Conflict();
                return;
            }
            // Routine observations can advance revision without changing the reviewed ownership scope.
            // The scope fingerprint and fresh remote path/monitoring checks fence meaningful changes.
            if (request.OperationId == Guid.Empty || request.ExpectedRevision < 1 || row.Revision < request.ExpectedRevision) throw Conflict();
            var owned = await controls.RequireScopeAsync(connectionId, holdingId, ct);
            if (owned.Fingerprint != request.ScopeFingerprint) throw Conflict();
            await RequireSettledControlsAsync(holdingId, ct);
            await PauseRequestAsync(holdingId, false, ct);
            var now = DateTimeOffset.UtcNow;
            var serialized = JsonSerializer.Serialize(request, Json);
            await db.ManagedHoldings.Where(value => value.Id == holdingId && value.Revision == row.Revision)
                .ExecuteUpdateAsync(set => set.SetProperty(value => value.ReleaseOperationId, request.OperationId)
                    .SetProperty(value => value.ReleaseRequestJson, serialized).SetProperty(value => value.Status, ManagedTrackingStatus.ReleasePending)
                    .SetProperty(value => value.Revision, row.Revision + 1).SetProperty(value => value.Problem, (string?)null)
                    .SetProperty(value => value.NextCheckAt, now), ct);
            await tracking.QueueAsync(connectionId, holdingId, ct);
        }, token);
        await transaction.CommitAsync(token);
        return (await tracking.FindAsync(holdingId, token))!.Tracking;
    }

    /// <inheritdoc />
    public async Task CompleteAsync(ManagedReleaseWork work, ManagedReleaseObservation evidence, CancellationToken token) {
        ManagedReleaseService.ValidateEvidence(work, evidence);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await WithOwnersAsync(work.Holding, async ct => {
            var row = await LockAsync(work.Holding.Id, ct);
            if (row.ReleaseOperationId != work.Request.OperationId || row.ConnectionId != work.Holding.ConnectionId) throw Conflict();
            if (row.Status == ManagedTrackingStatus.Released) return;
            if (row.Status != ManagedTrackingStatus.ReleasePending || row.Revision != work.Holding.Revision) throw Conflict();
            var current = (await FindAsync(row.Id, ct))!;
            ManagedReleaseService.ValidateEvidence(current, evidence);
            await RequireSettledControlsAsync(row.Id, ct);
            var entityIds = current.Holding.Targets.Select(target => target.EntityId).Distinct().ToArray();
            var owners = db.FulfillmentReservations.Where(owner => owner.OwnerId == row.Id && owner.ConnectionId == row.ConnectionId
                && owner.ReleasedAt == null && (owner.OwnerKind == FulfillmentOwnerKind.ConnectedLibrary || owner.OwnerKind == FulfillmentOwnerKind.ExternalManager));
            if (!(await owners.Select(owner => owner.EntityId).Distinct().ToArrayAsync(ct)).ToHashSet().SetEquals(entityIds)) throw Conflict();
            await PauseRequestAsync(row.Id, true, ct);
            var now = DateTimeOffset.UtcNow;
            var archived = JsonSerializer.Serialize(current.Holding.Bindings, Json);
            await db.ManagedSourceBindings.Where(binding => binding.HoldingId == row.Id).ExecuteDeleteAsync(ct);
            await owners.ExecuteUpdateAsync(set => set.SetProperty(owner => owner.ReleasedAt, now), ct);
            await db.ManagedHoldings.Where(value => value.Id == row.Id && value.Revision == row.Revision)
                .ExecuteUpdateAsync(set => set.SetProperty(value => value.Status, ManagedTrackingStatus.Released)
                    .SetProperty(value => value.Revision, row.Revision + 1).SetProperty(value => value.ReleasedAt, now)
                    .SetProperty(value => value.ReleasedBindingsJson, archived).SetProperty(value => value.LastCheckedAt, now)
                    .SetProperty(value => value.Problem, (string?)null), ct);
        }, token);
        await transaction.CommitAsync(token);
    }

    /// <inheritdoc />
    public async Task RecordProblemAsync(ManagedReleaseWork work, string problem, CancellationToken token) {
        var now = DateTimeOffset.UtcNow;
        var safe = problem[..Math.Min(problem.Length, 4096)];
        if (await db.ManagedHoldings.Where(row => row.Id == work.Holding.Id && row.Revision == work.Holding.Revision
                && row.Status == ManagedTrackingStatus.ReleasePending && row.ReleaseOperationId == work.Request.OperationId)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.Problem, safe).SetProperty(row => row.Revision, work.Holding.Revision + 1)
                .SetProperty(row => row.LastCheckedAt, now).SetProperty(row => row.NextCheckAt, now.AddMinutes(5)), token) != 1) throw Conflict();
    }

    private async Task<ManagedTrackingResponse> RequireHoldingAsync(Guid connectionId, Guid id, CancellationToken token) {
        var holding = (await tracking.FindAsync(id, token))?.Tracking;
        if (holding is null || holding.ConnectionId != connectionId || holding.Targets.Count == 0)
            throw new ManagedControlConflictException("This connection does not own a verified finite holding.");
        return holding;
    }
    private async Task WithOwnersAsync(ManagedTrackingResponse holding, Func<CancellationToken, Task> action, CancellationToken token) {
        var ids = holding.Targets.Select(target => target.EntityId).Distinct().ToArray();
        if (!await lifecycle.ExecuteManyAsync(ids, action, token)) throw new EntityLifecycleMutationConflictException(ids.FirstOrDefault());
    }
    private async Task<ManagedHoldingRow> LockAsync(Guid holdingId, CancellationToken token) {
        // Match materialization's lock order after the entity lifecycle lease: request, then holding.
        await db.ManagedRequests.FromSqlInterpolated($"SELECT * FROM managed_requests WHERE id = {holdingId} FOR UPDATE").AsNoTracking().ToArrayAsync(token);
        return (await db.ManagedHoldings.FromSqlInterpolated($"SELECT * FROM managed_holdings WHERE id = {holdingId} FOR UPDATE")
            .AsNoTracking().ToArrayAsync(token)).SingleOrDefault() ?? throw Conflict();
    }
    private async Task PauseRequestAsync(Guid holdingId, bool complete, CancellationToken token) {
        var row = await db.ManagedRequests.AsNoTracking().SingleOrDefaultAsync(row => row.Id == holdingId, token);
        if (row is null) return;
        var state = JsonSerializer.Deserialize<ManagedRequestState>(row.StateJson, Json)!;
        var operation = new ManagedRequestOperation(state);
        if (state.Revision != row.Revision || state.Phase != row.Phase) throw Conflict();
        if (complete) operation.ReleaseOwnership();
        else if (operation.IsActive) operation.RequireReview();
        else return;
        var serialized = JsonSerializer.Serialize(operation.State, Json);
        if (await db.ManagedRequests.Where(value => value.Id == row.Id && value.Revision == row.Revision)
            .ExecuteUpdateAsync(set => set.SetProperty(value => value.StateJson, serialized).SetProperty(value => value.Phase, operation.State.Phase)
                .SetProperty(value => value.Revision, operation.State.Revision).SetProperty(value => value.NextCheckAt, (DateTimeOffset?)null)
                .SetProperty(value => value.UpdatedAt, DateTimeOffset.UtcNow).SetProperty(value => value.Problem, (string?)null), token) != 1) throw Conflict();
    }
    private static ManagedControlConflictException Conflict() => new("This ownership handoff changed. Refresh its retained progress before continuing.");
}
