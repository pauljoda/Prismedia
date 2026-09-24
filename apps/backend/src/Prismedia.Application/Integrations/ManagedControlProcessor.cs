using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>
/// Executes finite manager stages using durable dispatch fences; ambiguous writes are only observed, never
/// automatically repeated. Illegal domain transitions are programming errors and are not swallowed.
/// </summary>
public sealed class ManagedControlProcessor(IManagedControlStore store, IntegrationConnectionAccess access, IIntegrationManagerControlGateway gateway) {
    #region Static Variables

    private const string ReviewProblem = "This action needs review because its connection, reviewed scope, or remote response could not be verified. "
        + "Refresh observes saved progress; uncertain writes are never automatically repeated.";

    #endregion

    #region Actions - Processing

    /// <summary>Advances at most one stage. Recovery scheduling and user refresh can safely call this after any crash.</summary>
    public async Task ProcessAsync(Guid id, CancellationToken token) {
        var work = await store.FindAsync(id, token) ?? throw new ArgumentException("The manager action no longer exists.");
        if (!work.Operation.IsActive) {
            return;
        }

        var stage = new Stage(store, work, token);
        try {
            if (work.Operation.State.Phase == ManagedControlPhase.SearchUncertain) {
                // Without an acknowledged command ID there is no safe correlation query and no safe retry of POST.
                work.Operation.RequireReview();
                await stage.SaveAsync("The search response was not retained. The manager may still be searching. "
                    + "Inspect it before closing this action or requesting another search.");
                return;
            }

            var observed = await ReconcileAsync(work, token);
            if (work.Operation.State.Phase == ManagedControlPhase.AwaitingCommand) {
                await ObserveCommandAsync(stage, observed);
                return;
            }

            if (observed.Path != work.Plan.Request.ExpectedPath) {
                throw new ManagedControlConflictException("The manager folder changed since review. No additional mutation was sent.");
            }

            if (work.Operation.Phase.AwaitsConfiguration) {
                await ConfigureAsync(stage, observed, token);
                return;
            }

            if (work.Operation.State.Phase == ManagedControlPhase.PendingSearch) {
                await SearchAsync(stage, observed, token);
            }
        } catch (OperationCanceledException) when (token.IsCancellationRequested) {
            throw;
        } catch (Exception error) when (error is IntegrationInvocationException or ConnectionNotFoundException
            or ConnectionSecretUnavailableException or ArgumentException or ManagedControlConflictException) {
            await RequireReviewAsync(id, stage.SavedRevision, token);
        }
    }

    private async Task<ManagedControlState> ReconcileAsync(StoredManagedControl work, CancellationToken token) {
        var connection = await AuthorizeAsync(work, IntegrationOperation.ReconcileManaged, token);
        var command = work.Operation.State.Command is { } identity
            ? new ManagedCommandReference(identity.Id, identity.QueuedAt)
            : null;
        var observed = await gateway.ReconcileAsync(connection.Manifest.Id, connection.Context, new(work.Plan.Scope, command), token);
        ManagedControlValidation.Validate(work.Plan.Scope, observed);
        return observed;
    }

    private static async Task ObserveCommandAsync(Stage stage, ManagedControlState observed) {
        var action = stage.Work.Operation;
        var accepted = action.State.Command!;
        var snapshot = observed.Command;
        if (snapshot is null || snapshot.Reference != new ManagedCommandReference(accepted.Id, accepted.QueuedAt)) {
            action.ObserveCommand(accepted, ManagedCommandStatus.Unknown);
            await stage.SaveAsync("The original command is absent or its identity changed. Its execution outcome remains unverified.");
            return;
        }

        action.ObserveCommand(accepted, snapshot.Status);
        await stage.SaveAsync(ManagedCommandStatusDefinition.For(snapshot.Status).Problem);
    }

    private async Task ConfigureAsync(Stage stage, ManagedControlState observed, CancellationToken token) {
        var action = stage.Work.Operation;
        var plan = stage.Work.Plan;
        var request = plan.Request;
        if (IsConfigured(plan, observed)) {
            action.ConfirmConfiguration();
            await stage.SaveAsync();
            return;
        }

        if (action.State.Phase == ManagedControlPhase.ConfigurationUncertain) {
            action.RequireReview();
            await stage.SaveAsync("The settings write has an uncertain outcome and the requested values are not currently visible. "
                + "Refresh to observe again; this action will not repeat the write.");
            return;
        }

        if (request.Changes.Monitored is not null && !observed.Capabilities.CanChangeMonitoring) {
            action.Reject();
            await stage.SaveAsync(observed.Capabilities.MonitoringUnavailableReason
                ?? "The manager cannot change monitoring for the exact reviewed scope.");
            return;
        }

        if (request.Changes.ProfileId is not null && !observed.Capabilities.CanChangeProfile) {
            action.Reject();
            await stage.SaveAsync("The manager cannot change the profile for the exact reviewed scope.");
            return;
        }

        var connection = await AuthorizeAsync(stage.Work, IntegrationOperation.ConfigureManaged, token);
        action.BeginConfiguration();
        await stage.SaveAsync(beforeDispatch: true);
        var outcome = await gateway.ConfigureAsync(connection.Manifest.Id, connection.Context,
            new(action.State.OperationId, plan.Scope, request.ExpectedPath, request.ExpectedProfileId, request.ExpectedMonitoring,
                request.Changes), token);
        if (outcome.Outcome == ManagedMutationOutcome.Applied) {
            action.ConfirmConfiguration();
            await stage.SaveAsync();
            return;
        }

        if (outcome.Outcome != ManagedMutationOutcome.Rejected) {
            throw new IntegrationInvocationException("The manager did not establish the settings outcome.");
        }

        action.Reject();
        await stage.SaveAsync(outcome.Problem
            ?? "The manager refused the settings change. Refresh its current settings before creating another action.");
    }

    private async Task SearchAsync(Stage stage, ManagedControlState observed, CancellationToken token) {
        var action = stage.Work.Operation;
        var plan = stage.Work.Plan;
        var request = plan.Request;
        var profile = request.Changes.ProfileId ?? request.ExpectedProfileId;
        var profileRequired = ManagedFulfillmentPolicy.For(observed.Item.EntityKind).UsesProfile;
        if (!observed.Capabilities.CanSearch || observed.Item.ProfileId != profile || profileRequired && string.IsNullOrWhiteSpace(profile)) {
            action.Reject();
            await stage.SaveAsync("Search was not sent because the reviewed profile or search capability changed.");
            return;
        }

        var connection = await AuthorizeAsync(stage.Work, IntegrationOperation.RequestManaged, token);
        action.BeginSearch();
        await stage.SaveAsync(beforeDispatch: true);
        var outcome = await gateway.RequestAsync(connection.Manifest.Id, connection.Context,
            new(action.State.OperationId, plan.Scope, request.ExpectedPath, profile), token);
        if (outcome.Outcome == ManagedMutationOutcome.Rejected) {
            action.Reject();
            await stage.SaveAsync(outcome.Problem ?? "The manager refused this search. Previously confirmed settings remain applied.");
            return;
        }

        if (outcome.Outcome != ManagedMutationOutcome.Accepted || outcome.Command is null) {
            throw new IntegrationInvocationException("The search response has no acknowledged command identity.");
        }

        action.AcceptCommand(new(outcome.Command.Reference.Id, outcome.Command.Reference.QueuedAt));
        await stage.SaveAsync();
        action.ObserveCommand(action.State.Command!, outcome.Command.Status);
        await stage.SaveAsync(ManagedCommandStatusDefinition.For(outcome.Command.Status).Problem);
    }

    private static bool IsConfigured(ManagedControlPlan plan, ManagedControlState observed) {
        var changes = plan.Request.Changes;
        var profileApplied = changes.ProfileId is null || observed.Item.ProfileId == changes.ProfileId;
        var monitoringApplied = changes.Monitored is null || (ManagedFulfillmentPolicy.For(plan.Scope.Item.EntityKind).MonitorsWholeItem
            ? observed.Item.Monitored == changes.Monitored
            : observed.Targets.All(target => target.Monitored == changes.Monitored));
        return profileApplied && monitoringApplied;
    }

    /// <summary>
    /// A failed save may follow a concurrent cancel or close, so only the unchanged persisted revision acquires
    /// the review fence.
    /// </summary>
    private async Task RequireReviewAsync(Guid id, long savedRevision, CancellationToken token) {
        var current = await store.FindAsync(id, token);
        if (current is null || current.Operation.State.Revision != savedRevision || !current.Operation.IsActive) {
            return;
        }

        current.Operation.RequireReview();
        try {
            await store.SaveAsync(current.Operation, savedRevision, ReviewProblem, false, token);
        } catch (ManagedControlConflictException) {
            // Newer progress owns the result.
        }
    }

    private Task<AuthorizedIntegrationConnection> AuthorizeAsync(StoredManagedControl work, IntegrationOperation operation,
        CancellationToken token) =>
        access.RequireAsync(work.Operation.State.ConnectionId, PluginCapability.ExternalManager, operation,
            work.Plan.Scope.Item.EntityKind, token);

    #endregion

    /// <summary>One processing pass: saves each transition against the last persisted revision.</summary>
    private sealed class Stage(IManagedControlStore store, StoredManagedControl work, CancellationToken token) {
        #region Variables

        /// <summary>The action being advanced.</summary>
        public StoredManagedControl Work { get; } = work;

        /// <summary>Revision most recently persisted by this pass.</summary>
        public long SavedRevision { get; private set; } = work.Operation.State.Revision;

        #endregion

        #region Actions - Persistence

        /// <summary>Persists the current transition, optionally revalidating the reviewed scope before dispatch.</summary>
        public async Task SaveAsync(string? problem = null, bool beforeDispatch = false) {
            await store.SaveAsync(Work.Operation, SavedRevision, problem, beforeDispatch, token);
            SavedRevision = Work.Operation.State.Revision;
        }

        #endregion
    }
}
