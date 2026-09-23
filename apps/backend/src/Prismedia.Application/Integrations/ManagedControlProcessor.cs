using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Executes finite manager stages using durable dispatch fences; ambiguous writes are only observed, never automatically repeated.</summary>
public sealed class ManagedControlProcessor(IManagedControlStore store, IntegrationConnectionAccess access, IIntegrationManagerControlGateway gateway) {
    /// <summary>Advances at most one stage. Recovery scheduling and user refresh can safely call this after any crash.</summary>
    public async Task ProcessAsync(Guid id, CancellationToken token) {
        var work = await store.FindAsync(id, token) ?? throw new ArgumentException("The manager action no longer exists.");
        var action = work.Operation;
        if (!action.IsActive) return;
        var savedRevision = action.State.Revision;
        async Task SaveAsync(string? problem = null, bool beforeDispatch = false) {
            await store.SaveAsync(action, savedRevision, problem, beforeDispatch, token);
            savedRevision = action.State.Revision;
        }
        try {
            // Without an acknowledged command ID there is no safe correlation query and no safe retry of POST.
            if (action.State.Phase == ManagedControlPhase.SearchUncertain) {
                action.RequireReview();
                await SaveAsync("The search response was not retained. The manager may still be searching. Inspect it before closing this action or requesting another search.");
                return;
            }
            var plan = work.Plan; var request = plan.Request;
            var connection = await AuthorizeAsync(IntegrationOperation.ReconcileManaged);
            var command = action.State.Command is { } identity ? new ManagedCommandReference(identity.Id, identity.QueuedAt) : null;
            var observed = await gateway.ReconcileAsync(connection.Manifest.Id, connection.Context, new(plan.Scope, command), token);
            ManagedControlValidation.Validate(plan.Scope, observed);
            if (action.State.Phase == ManagedControlPhase.AwaitingCommand) {
                var snapshot = observed.Command;
                if (snapshot is null || snapshot.Reference != command) {
                    action.ObserveCommand(action.State.Command!, ManagedCommandStatus.Unknown);
                    await SaveAsync("The original command is absent or its identity changed. Its execution outcome remains unverified.");
                } else {
                    action.ObserveCommand(action.State.Command!, snapshot.Status);
                    await SaveAsync(CommandProblem(snapshot.Status));
                }
                return;
            }
            if (observed.Path != request.ExpectedPath) throw new ManagedControlConflictException("The manager folder changed since review. No additional mutation was sent.");
            if (action.State.Phase is ManagedControlPhase.PendingConfiguration or ManagedControlPhase.ConfigurationUncertain) {
                var desired = (request.Changes.ProfileId is null || observed.Item.ProfileId == request.Changes.ProfileId)
                    && (request.Changes.Monitored is null || (plan.Scope.Item.EntityKind == EntityKind.Book
                        ? observed.Item.Monitored == request.Changes.Monitored
                        : observed.Targets.All(target => target.Monitored == request.Changes.Monitored)));
                if (desired) { action.ConfirmConfiguration(); await SaveAsync(); return; }
                if (action.State.Phase == ManagedControlPhase.ConfigurationUncertain) {
                    action.RequireReview();
                    await SaveAsync("The settings write has an uncertain outcome and the requested values are not currently visible. Refresh to observe again; this action will not repeat the write.");
                    return;
                }
                if (request.Changes.ProfileId is not null && !observed.Capabilities.CanChangeProfile
                    || request.Changes.Monitored is not null && !observed.Capabilities.CanChangeMonitoring) {
                    action.Reject(); await SaveAsync("The manager cannot apply these settings to the exact reviewed scope."); return;
                }
                connection = await AuthorizeAsync(IntegrationOperation.ConfigureManaged);
                action.BeginConfiguration(); await SaveAsync(beforeDispatch: true);
                var outcome = await gateway.ConfigureAsync(connection.Manifest.Id, connection.Context,
                    new(id, plan.Scope, request.ExpectedPath, request.ExpectedProfileId, request.ExpectedMonitoring, request.Changes), token);
                if (outcome.Outcome == ManagedMutationOutcome.Applied) action.ConfirmConfiguration();
                else if (outcome.Outcome == ManagedMutationOutcome.Rejected) action.Reject();
                else throw new IntegrationInvocationException("The manager did not establish the settings outcome.");
                await SaveAsync(outcome.Outcome == ManagedMutationOutcome.Rejected ? "The manager refused the settings change. Refresh its current settings before creating another action." : null);
                return;
            }
            if (action.State.Phase == ManagedControlPhase.PendingSearch) {
                var profile = request.Changes.ProfileId ?? request.ExpectedProfileId;
                if (!observed.Capabilities.CanSearch || observed.Item.ProfileId != profile
                    || observed.Item.EntityKind is not (EntityKind.ComicSeries or EntityKind.Book)
                        && string.IsNullOrWhiteSpace(profile)) {
                    action.Reject(); await SaveAsync("Search was not sent because the reviewed profile or search capability changed."); return;
                }
                connection = await AuthorizeAsync(IntegrationOperation.RequestManaged);
                action.BeginSearch(); await SaveAsync(beforeDispatch: true);
                var outcome = await gateway.RequestAsync(connection.Manifest.Id, connection.Context, new(id, plan.Scope, request.ExpectedPath, profile), token);
                if (outcome.Outcome == ManagedMutationOutcome.Rejected) {
                    action.Reject(); await SaveAsync("The manager refused this search. Previously confirmed settings remain applied."); return;
                }
                if (outcome.Outcome != ManagedMutationOutcome.Accepted || outcome.Command is null) throw new IntegrationInvocationException("The search response has no acknowledged command identity.");
                action.AcceptCommand(new(outcome.Command.Reference.Id, outcome.Command.Reference.QueuedAt));
                await SaveAsync();
                action.ObserveCommand(action.State.Command!, outcome.Command.Status);
                await SaveAsync(CommandProblem(outcome.Command.Status));
            }

            Task<AuthorizedIntegrationConnection> AuthorizeAsync(IntegrationOperation operation) => access.RequireAsync(
                action.State.ConnectionId, PluginCapability.ExternalManager, operation, work.Plan.Scope.Item.EntityKind, token);
        } catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IntegrationInvocationException or ConnectionNotFoundException or ConnectionSecretUnavailableException
            or ArgumentException or ManagedControlConflictException or InvalidOperationException) {
            // A failed save may follow a concurrent cancel/close. Only the unchanged persisted revision can acquire this problem.
            var current = await store.FindAsync(id, token);
            if (current is null || current.Operation.State.Revision != savedRevision || !current.Operation.IsActive) return;
            current.Operation.RequireReview();
            try {
                await store.SaveAsync(current.Operation, savedRevision,
                    "This action needs review because its connection, reviewed scope, or remote response could not be verified. Refresh observes saved progress; uncertain writes are never automatically repeated.", false, token);
            } catch (ManagedControlConflictException) { /* Newer progress owns the result. */ }
        }
    }
    private static string? CommandProblem(ManagedCommandStatus status) => status switch {
        ManagedCommandStatus.Unknown => "The manager cannot establish the original command's outcome. No replacement search was sent.",
        ManagedCommandStatus.Failed => "The manager reported that the search failed. Previously confirmed settings remain applied.",
        ManagedCommandStatus.Cancelled => "The manager reported that the search was cancelled. Previously confirmed settings remain applied.",
        _ => null
    };
}
