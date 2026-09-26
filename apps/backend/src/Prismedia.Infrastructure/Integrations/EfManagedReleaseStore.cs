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
    #region Static Variables

    private static readonly JsonSerializerOptions Json = PluginProcessTransport.JsonOptions;

    #endregion

    #region Actions - Queries

    /// <inheritdoc />
    public async Task<ManagedReleaseWork?> FindAsync(Guid holdingId, CancellationToken token) {
        var row = await db.ManagedHoldings.AsNoTracking().SingleOrDefaultAsync(row => row.Id == holdingId, token);
        if (row?.ReleaseOperationId is null) {
            return null;
        }

        var request = JsonSerializer.Deserialize<ReleaseManagedHoldingRequest>(row.ReleaseRequestJson!, Json);
        if (request is null || request.OperationId != row.ReleaseOperationId
            || row.Status is not (ManagedTrackingStatus.ReleasePending or ManagedTrackingStatus.Released)) {
            throw new InvalidDataException("The retained ownership release has inconsistent identity or state.");
        }

        return new((await tracking.FindAsync(holdingId, token))!.Tracking, request);
    }

    /// <inheritdoc />
    public async Task RequireSettledControlsAsync(Guid holdingId, CancellationToken token) {
        if (await db.ManagedControls.AnyAsync(row => row.HoldingId == holdingId
            && (row.ActiveHoldingId != null || row.Phase == ManagedControlPhase.ClosedUnverified), token)) {
            throw new ManagedControlConflictException(
                "This holding has an unfinished or unverified manager action. Resolve its outcome before releasing ownership.");
        }
    }

    #endregion

    #region Actions - Release

    /// <inheritdoc />
    public async Task<ManagedTrackingResponse> BeginAsync(Guid connectionId, Guid holdingId, ReleaseManagedHoldingRequest request,
        CancellationToken token) {
        var holding = await RequireHoldingAsync(connectionId, holdingId, token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await PluginLifecycleLease.LockConnectionAsync(db, connectionId, token, requireReady: true);
        await WithOwnersAsync(holding, async ct => {
            var row = await LockAsync(holdingId, ct);
            if (row.ReleaseOperationId is not null) {
                if (row.ReleaseOperationId != request.OperationId
                    || JsonSerializer.Deserialize<ReleaseManagedHoldingRequest>(row.ReleaseRequestJson!, Json) != request) {
                    throw Conflict();
                }

                return;
            }

            // Routine observations can advance revision without changing the reviewed ownership scope.
            // The scope fingerprint and fresh remote path/monitoring checks fence meaningful changes.
            if (request.OperationId == Guid.Empty || request.ExpectedRevision < 1 || row.Revision < request.ExpectedRevision) {
                throw Conflict();
            }

            var owned = await controls.RequireScopeAsync(connectionId, holdingId, ct);
            if (owned.Fingerprint != request.ScopeFingerprint) {
                throw Conflict();
            }

            await RequireSettledControlsAsync(holdingId, ct);
            await PauseRequestsAsync(row, false, ct);
            var serialized = JsonSerializer.Serialize(request, Json);
            var releasing = row.ToDomain();
            releasing.BeginRelease(request.OperationId, DateTimeOffset.UtcNow);
            var next = releasing.State;
            await db.ManagedHoldings.Where(value => value.Id == holdingId && value.Revision == row.Revision)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(value => value.ReleaseOperationId, next.ReleaseOperationId)
                    .SetProperty(value => value.ReleaseRequestJson, serialized)
                    .SetProperty(value => value.Status, next.Status)
                    .SetProperty(value => value.Revision, next.Revision)
                    .SetProperty(value => value.Problem, next.Problem)
                    .SetProperty(value => value.NextCheckAt, next.NextCheckAt), ct);
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
            if (row.ReleaseOperationId != work.Request.OperationId || row.ConnectionId != work.Holding.ConnectionId) {
                throw Conflict();
            }

            if (row.Status == ManagedTrackingStatus.Released) {
                return;
            }

            if (row.Status != ManagedTrackingStatus.ReleasePending || row.Revision != work.Holding.Revision) {
                throw Conflict();
            }

            var current = (await FindAsync(row.Id, ct))!;
            ManagedReleaseService.ValidateEvidence(current, evidence);
            await RequireSettledControlsAsync(row.Id, ct);
            var entityIds = await ReservationIdsAsync(current.Holding, ct);
            var owners = db.FulfillmentReservations.Where(owner => owner.OwnerId == row.Id && owner.ConnectionId == row.ConnectionId
                && owner.ReleasedAt == null
                && (owner.OwnerKind == FulfillmentOwnerKind.ConnectedLibrary || owner.OwnerKind == FulfillmentOwnerKind.ExternalManager));
            if (!(await owners.Select(owner => owner.EntityId).Distinct().ToArrayAsync(ct)).ToHashSet().SetEquals(entityIds)) {
                throw Conflict();
            }

            await PauseRequestsAsync(row, true, ct);
            var now = DateTimeOffset.UtcNow;
            var archived = JsonSerializer.Serialize(current.Holding.Bindings, Json);
            var released = row.ToDomain();
            released.CompleteRelease(now);
            var next = released.State;
            await db.ManagedSourceBindings.Where(binding => binding.HoldingId == row.Id).ExecuteDeleteAsync(ct);
            await owners.ExecuteUpdateAsync(set => set.SetProperty(owner => owner.ReleasedAt, now), ct);
            await db.ManagedHoldings.Where(value => value.Id == row.Id && value.Revision == row.Revision)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(value => value.Status, next.Status)
                    .SetProperty(value => value.Revision, next.Revision)
                    .SetProperty(value => value.ReleasedAt, next.ReleasedAt)
                    .SetProperty(value => value.ReleasedBindingsJson, archived)
                    .SetProperty(value => value.LastCheckedAt, next.LastCheckedAt)
                    .SetProperty(value => value.Problem, next.Problem), ct);
        }, token);
        await transaction.CommitAsync(token);
    }

    /// <inheritdoc />
    public async Task RecordProblemAsync(ManagedReleaseWork work, string problem, CancellationToken token) {
        var row = await db.ManagedHoldings.AsNoTracking().SingleOrDefaultAsync(value => value.Id == work.Holding.Id, token);
        if (row is null || row.Revision != work.Holding.Revision || row.Status != ManagedTrackingStatus.ReleasePending
            || row.ReleaseOperationId != work.Request.OperationId) {
            throw Conflict();
        }

        var holding = row.ToDomain();
        holding.RecordReleaseProblem(problem, DateTimeOffset.UtcNow);
        var next = holding.State;
        if (await db.ManagedHoldings.Where(value => value.Id == work.Holding.Id && value.Revision == work.Holding.Revision
                && value.Status == ManagedTrackingStatus.ReleasePending && value.ReleaseOperationId == work.Request.OperationId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(value => value.Problem, next.Problem)
                .SetProperty(value => value.Revision, next.Revision)
                .SetProperty(value => value.LastCheckedAt, next.LastCheckedAt)
                .SetProperty(value => value.NextCheckAt, next.NextCheckAt), token) != 1) {
            throw Conflict();
        }
    }

    #endregion

    #region Actions - Ownership

    private async Task<ManagedTrackingResponse> RequireHoldingAsync(Guid connectionId, Guid id, CancellationToken token) {
        var holding = (await tracking.FindAsync(id, token))?.Tracking;
        if (holding is null || holding.ConnectionId != connectionId || holding.Targets.Count == 0) {
            throw new ManagedControlConflictException("This connection does not own a verified finite holding.");
        }

        return holding;
    }

    private async Task WithOwnersAsync(ManagedTrackingResponse holding, Func<CancellationToken, Task> action, CancellationToken token) {
        var reservationIds = await ReservationIdsAsync(holding, token);
        var ids = holding.Targets.Select(target => target.EntityId).Concat(reservationIds).Distinct().ToArray();
        if (!await lifecycle.ExecuteManyAsync(ids, async leaseToken => {
            if (!reservationIds.ToHashSet().SetEquals(await ReservationIdsAsync(holding, leaseToken))) {
                throw Conflict();
            }

            await action(leaseToken);
        }, token)) {
            throw new EntityLifecycleMutationConflictException(ids.FirstOrDefault());
        }
    }

    private Task<Guid[]> ReservationIdsAsync(ManagedTrackingResponse holding, CancellationToken token) =>
        new ManagedReservationScopeResolver(db).ResolveAsync(
            holding.Targets.Select(target => target.EntityId).ToArray(), holding.Item, token);

    private async Task<ManagedHoldingRow> LockAsync(Guid holdingId, CancellationToken token) {
        // Match materialization's lock order after the entity lifecycle lease: request, then holding.
        var original = (await db.ManagedRequests
            .FromSqlInterpolated($"SELECT * FROM managed_requests WHERE id = {holdingId} FOR UPDATE")
            .AsNoTracking().ToArrayAsync(token)).SingleOrDefault();
        if (original is not null) {
            await db.ManagedRequests
                .FromSqlInterpolated($"SELECT * FROM managed_requests WHERE connection_id = {original.ConnectionId} AND entity_id = {original.EntityId} ORDER BY id FOR UPDATE")
                .AsNoTracking().ToArrayAsync(token);
        }

        return (await db.ManagedHoldings.FromSqlInterpolated($"SELECT * FROM managed_holdings WHERE id = {holdingId} FOR UPDATE")
            .AsNoTracking().ToArrayAsync(token)).SingleOrDefault() ?? throw Conflict();
    }

    #endregion

    #region Actions - Requests

    private async Task PauseRequestsAsync(ManagedHoldingRow holding, bool complete, CancellationToken token) {
        var original = await db.ManagedRequests.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == holding.Id, token);
        if (original is null) {
            return;
        }

        var rows = await db.ManagedRequests
            .FromSqlInterpolated($"SELECT * FROM managed_requests WHERE connection_id = {holding.ConnectionId} AND entity_id = {original.EntityId} FOR UPDATE")
            .AsNoTracking().ToArrayAsync(token);
        var retainedIds = JsonSerializer.Deserialize<ManagedTargetBinding[]>(holding.TargetsJson, Json)!
            .Select(binding => binding.EntityId).ToHashSet();
        foreach (var row in rows.OrderBy(row => row.Id)) {
            var plan = JsonSerializer.Deserialize<ManagedRequestPlan>(row.PlanJson, Json)
                ?? throw new InvalidDataException("Invalid managed request intent.");
            if (row.Id != holding.Id && plan.ExistingHoldingId != holding.Id) {
                continue;
            }

            var state = JsonSerializer.Deserialize<ManagedRequestState>(row.StateJson, Json)!;
            if (state.Revision != row.Revision || state.Phase != row.Phase) {
                throw Conflict();
            }

            var operation = new ManagedRequestOperation(state);
            if (complete) {
                if (!ManagedRequestPhaseDefinition.For(state.Phase).HoldsRemoteIdentity) {
                    continue;
                }

                operation.ReleaseOwnership();
            } else if (plan.ExistingHoldingId == holding.Id && operation.CanCancel) {
                var targets = (plan.Request.TargetEntityIds ?? []).ToArray();
                if (targets.Any(retainedIds.Contains)) {
                    throw Conflict();
                }

                operation.Cancel();
                await db.FulfillmentReservations.Where(owner => owner.OwnerId == holding.Id
                        && owner.OwnerKind == FulfillmentOwnerKind.ExternalManager
                        && owner.ReleasedAt == null && targets.Contains(owner.EntityId))
                    .ExecuteUpdateAsync(set => set.SetProperty(owner => owner.ReleasedAt, DateTimeOffset.UtcNow), token);
            } else if (operation.IsActive) {
                operation.RequireReview();
            } else {
                continue;
            }

            var serialized = JsonSerializer.Serialize(operation.State, Json);
            if (await db.ManagedRequests.Where(value => value.Id == row.Id && value.Revision == row.Revision)
                .ExecuteUpdateAsync(set => set.SetProperty(value => value.StateJson, serialized)
                    .SetProperty(value => value.Phase, operation.State.Phase)
                    .SetProperty(value => value.ReviewRequired, operation.State.ReviewRequired)
                    .SetProperty(value => value.Revision, operation.State.Revision)
                    .SetProperty(value => value.NextCheckAt, (DateTimeOffset?)null)
                    .SetProperty(value => value.UpdatedAt, DateTimeOffset.UtcNow)
                    .SetProperty(value => value.Problem, (string?)null), token) != 1) {
                throw Conflict();
            }
        }
    }

    #endregion

    #region Actions - Conflicts

    private static ManagedControlConflictException Conflict() =>
        new("This ownership handoff changed. Refresh its retained progress before continuing.");

    #endregion
}
