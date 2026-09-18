using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Runs one bounded fulfillment observation, preserving durable fences before manager effects.</summary>
public sealed class ManagedRequestProcessor(IManagedRequestStore store, IntegrationConnectionAccess access,
    IIntegrationManagerCreationGateway creation, ManagedLibraryService library,
    IManagedControlStore controlStore, ManagedControlService controls, IManagedTrackingStore tracking) {
    /// <summary>Consumes pending or stopped request work; completed requests delegate later refreshes to established holding tracking.</summary>
    public async Task<bool> ProcessAsync(Guid id, CancellationToken token) {
        var work = await store.FindAsync(id, token);
        if (work is null) return false;
        var holdingId = work.Plan.ExistingHoldingId ?? id;
        if (!work.Operation.IsActive) return work.Operation.State.Phase != ManagedRequestPhase.Completed;
        try {
            if (work.Operation.State.Phase is ManagedRequestPhase.PendingCreation or ManagedRequestPhase.CreationUncertain) {
                await ResolveCreationAsync(work, token);
                return true;
            }
            var state = work.Operation.State;
            ManagedItemSnapshot snapshot;
            try {
                snapshot = await library.GetAsync(state.ConnectionId,
                    new(work.Plan.Creation.Work.EntityKind, state.RemoteId!, work.Plan.Creation.Work.ExternalIds), token);
            } catch (IntegrationInvocationException error) when (state.Phase == ManagedRequestPhase.AwaitingFiles
                && error.Code != IntegrationErrorCode.ManagedItemNotFound) {
                var revision = work.Operation.State.Revision;
                work.Operation.RecordRetryableObservation();
                await store.SaveAsync(work.Operation, revision, error.Message, false, token);
                return true;
            }
            if (state.Phase == ManagedRequestPhase.RemoteRemoved) {
                var revision = work.Operation.State.Revision;
                work.Operation.RequireReview();
                await store.SaveAsync(work.Operation, revision,
                    "The removed remote identity exists again. Review it before restoring external fulfillment.", false, token);
                return true;
            }
            // Legacy reviewed rows are re-observed only so a typed, provider-confirmed absence can
            // transition them to RemoteRemoved. A successful read must not replay controls or import
            // files that the prior review fence deliberately stopped.
            if (state.ReviewRequired) return true;
            await store.ValidateHoldingAsync(work, snapshot, token);
            await EnsureControlsAsync(work, token);
            var materialized = await store.MaterializeAsync(work, snapshot, token);
            if (!materialized.Imported) {
                var revision = work.Operation.State.Revision;
                work.Operation.ContinueWaiting();
                await store.SaveAsync(work.Operation, revision, materialized.WaitingReason, false, token);
            }
        } catch (IntegrationInvocationException error) when (error.Code == IntegrationErrorCode.ManagedItemNotFound) {
            var holding = await tracking.FindAsync(holdingId, token);
            if (holding is null) {
                var revision = work.Operation.State.Revision;
                work.Operation.RequireReview();
                await store.SaveAsync(work.Operation, revision,
                    "The manager confirmed removal, but the retained holding association is missing. Review this request.", false, token);
            } else {
                await tracking.ConfirmRemovalAsync(holding,
                    "The connected manager no longer contains this holding. Local metadata and history were retained.", token);
            }
        } catch (Exception error) when (work.Operation.State.Phase == ManagedRequestPhase.RemoteRemoved
            && error is IntegrationInvocationException or ConnectionNotFoundException
                or ConnectionSecretUnavailableException or ConnectionCapabilityUnavailableException) {
            if (await tracking.FindAsync(holdingId, token) is { } holding)
                await tracking.RecordProblemAsync(holdingId, holding.Tracking.Revision, ManagedTrackingStatus.Removed,
                    "The connection could not be verified. The last confirmed removal and local data were retained.", token);
        } catch (Exception error) when (work.Operation.State.Phase == ManagedRequestPhase.AwaitingFiles
            && error is ConnectionNotFoundException or ConnectionSecretUnavailableException
                or ConnectionCapabilityUnavailableException) {
            var revision = work.Operation.State.Revision;
            work.Operation.RecordRetryableObservation();
            await store.SaveAsync(work.Operation, revision, error.Message, false, token);
        } catch (Exception error) when (error is IntegrationInvocationException or ConnectionNotFoundException
            or ConnectionSecretUnavailableException or ConnectionCapabilityUnavailableException or ArgumentException or ManagedControlConflictException) {
            var revision = work.Operation.State.Revision;
            work.Operation.RequireReview();
            await store.SaveAsync(work.Operation, revision, error.Message, false, token);
        }
        return true;
    }

    private async Task ResolveCreationAsync(StoredManagedRequest work, CancellationToken token) {
        var state = work.Operation.State;
        var connection = await access.RequireAsync(state.ConnectionId, PluginCapability.ExternalManager,
            IntegrationOperation.LookupManaged, work.Plan.Creation.Work.EntityKind, token);
        var lookup = await creation.LookupAsync(connection.Manifest.Id, connection.Context, work.Plan.Creation.Work, token);
        ManagedCreationEvidence.ValidateLookup(work.Plan.Creation.Work, lookup);
        if (lookup.Existing is { } existing) {
            await store.AcceptHoldingAsync(work, existing, lookup.Targets, token);
            return;
        }
        if (work.Plan.ExistingHoldingId is not null) {
            work.Operation.RequireReview();
            await store.SaveAsync(work.Operation, state.Revision,
                "The reviewed series holding is no longer visible. No replacement series was created.", false, token);
            return;
        }
        if (state.Phase == ManagedRequestPhase.CreationUncertain) {
            work.Operation.RequireReview();
            await store.SaveAsync(work.Operation, state.Revision,
                "Creation is uncertain and the exact identity is not visible yet. No second creation was sent; inspect the manager and refresh this request.", false, token);
            return;
        }
        connection = await access.RequireAsync(state.ConnectionId, PluginCapability.ExternalManager,
            IntegrationOperation.EnsureManaged, work.Plan.Creation.Work.EntityKind, token);
        work.Operation.BeginCreation();
        await store.SaveAsync(work.Operation, state.Revision, null, true, token);
        var result = await creation.EnsureAsync(connection.Manifest.Id, connection.Context, work.Plan.Creation, token);
        ManagedCreationEvidence.ValidateResult(work.Plan.Creation.Work, result);
        if (result.Outcome == ManagedMutationOutcome.Rejected) {
            var revision = work.Operation.State.Revision;
            work.Operation.RejectCreation();
            await store.SaveAsync(work.Operation, revision, result.Problem ?? "The manager rejected initial creation.", false, token);
        } else await store.AcceptHoldingAsync(work, result.Holding!, result.Targets, token);
    }

    private async Task EnsureControlsAsync(StoredManagedRequest work, CancellationToken token) {
        var state = work.Operation.State;
        var holdingId = work.Plan.ExistingHoldingId ?? state.OperationId;
        if (await controlStore.FindAsync(state.OperationId, token) is { } accepted) {
            if (accepted.Operation.State.HoldingId != holdingId || accepted.Operation.State.ConnectionId != state.ConnectionId)
                throw new ManagedControlConflictException("This request's manager-action identity is already in use.");
            return;
        }
        var scopeEntityIds = work.Plan.ExistingHoldingId is null ? null : work.Plan.Request.TargetEntityIds;
        var preview = scopeEntityIds is null
            ? await controls.PreviewAsync(state.ConnectionId, holdingId, token)
            : await controls.PreviewAsync(state.ConnectionId, holdingId, scopeEntityIds, token);
        if (preview.State.Item.ProfileId != work.Plan.Creation.ProfileId)
            throw new ManagedControlConflictException("The manager profile changed before fulfillment dispatch. Review its settings before continuing.");
        var request = new CreateManagedControlRequest(
            state.OperationId, preview.ScopeFingerprint, preview.State.Path, preview.State.Item.ProfileId!,
            preview.State.Targets.ToDictionary(target => target.Target.RemoteId, target => target.Monitored),
            InitialConfiguration(work.Plan.Request), work.Plan.Request.Search);
        if (scopeEntityIds is null) await controls.CreateAsync(state.ConnectionId, holdingId,
            request, token);
        else await controls.CreateAsync(state.ConnectionId, holdingId,
            request, scopeEntityIds, token);
    }

    /// <summary>
    /// Initial finite-series fulfillment preserves existing episode monitoring. A newly created
    /// series is already unmonitored by the adapter, and the durable control requests only the exact
    /// episode search.
    /// </summary>
    internal static ManagedConfigurationChange InitialConfiguration(CreateManagedRequestInput request) =>
        request.ReviewedWork.EntityKind == EntityKind.VideoSeries
            ? new()
            : new(Monitored: request.Monitored);
}
