using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Runs one bounded fulfillment observation, preserving durable fences before manager effects.</summary>
public sealed class ManagedRequestProcessor(IManagedRequestStore store, IntegrationConnectionAccess access,
    IIntegrationManagerCreationGateway creation, ManagedLibraryService library,
    IManagedControlStore controlStore, ManagedControlService controls) {
    /// <summary>Consumes pending or stopped request work; completed requests delegate later refreshes to established holding tracking.</summary>
    public async Task<bool> ProcessAsync(Guid id, CancellationToken token) {
        var work = await store.FindAsync(id, token);
        if (work is null) return false;
        if (!work.Operation.IsActive) return work.Operation.State.Phase != ManagedRequestPhase.Completed;
        try {
            if (work.Operation.State.Phase is ManagedRequestPhase.PendingCreation or ManagedRequestPhase.CreationUncertain) {
                await ResolveCreationAsync(work, token);
                return true;
            }
            var state = work.Operation.State;
            var snapshot = await library.GetAsync(state.ConnectionId,
                new(work.Plan.Creation.Work.EntityKind, state.RemoteId!, work.Plan.Creation.Work.ExternalIds), token);
            await store.ValidateHoldingAsync(work, snapshot, token);
            await EnsureControlsAsync(work, token);
            var materialized = await store.MaterializeAsync(work, snapshot, token);
            if (!materialized.Imported) {
                var revision = work.Operation.State.Revision;
                work.Operation.ContinueWaiting();
                await store.SaveAsync(work.Operation, revision, materialized.WaitingReason, false, token);
            }
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
            await store.AcceptHoldingAsync(work, existing, token);
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
        } else await store.AcceptHoldingAsync(work, result.Holding!, token);
    }

    private async Task EnsureControlsAsync(StoredManagedRequest work, CancellationToken token) {
        var state = work.Operation.State;
        if (await controlStore.FindAsync(state.OperationId, token) is { } accepted) {
            if (accepted.Operation.State.HoldingId != state.OperationId || accepted.Operation.State.ConnectionId != state.ConnectionId)
                throw new ManagedControlConflictException("This request's manager-action identity is already in use.");
            return;
        }
        var preview = await controls.PreviewAsync(state.ConnectionId, state.OperationId, token);
        if (preview.State.Item.ProfileId != work.Plan.Creation.ProfileId)
            throw new ManagedControlConflictException("The manager profile changed before fulfillment dispatch. Review its settings before continuing.");
        await controls.CreateAsync(state.ConnectionId, state.OperationId,
            new(state.OperationId, preview.ScopeFingerprint, preview.State.Path, preview.State.Item.ProfileId!,
                preview.State.Targets.ToDictionary(target => target.Target.RemoteId, target => target.Monitored),
                new(Monitored: work.Plan.Request.Monitored), work.Plan.Request.Search), token);
    }
}
