using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Recovers exact source preparation before handing ready content to the direct verified import pipeline.</summary>
public sealed class SourceAcquisitionProcessor(IIntegrationTransferStore store, IntegrationConnectionAccess access,
    IIntegrationSourceAcquisitionGateway gateway, SourceTransferProcessor sourceTransfers) {
    /// <summary>Observes before every idempotent request dispatch and persists every readiness boundary.</summary>
    public async Task ProcessAsync(Guid operationId, JobContext context, CancellationToken cancellationToken) {
        var work = await store.FindAsync(operationId, cancellationToken) ?? throw new IntegrationTransferNotFoundException();
        var transfer = work.Transfer;
        if (transfer.State.Phase is IntegrationTransferPhase.Completed or IntegrationTransferPhase.Cancelled) return;
        if (transfer.State.Mode != IntegrationTransferMode.SourceRequest || work.Plan.Source is not { } source
            || source.Publication is null || source.Offer is null)
            throw new InvalidOperationException("This processor requires an accepted exact source request.");
        var revision = transfer.State.Revision;

        async Task PersistAsync(string? error = null) {
            if (transfer.State.Revision == revision) return;
            await store.SaveAsync(transfer, revision, error, cancellationToken);
            revision = transfer.State.Revision;
        }

        try {
            if (transfer.State.Phase is IntegrationTransferPhase.PendingSubmission or IntegrationTransferPhase.SubmissionUncertain
                or IntegrationTransferPhase.AwaitingRemote or IntegrationTransferPhase.NeedsReview) {
                var observe = await access.RequireAsync(transfer.State.ConnectionId, PluginCapability.AcquisitionSource,
                    IntegrationOperation.ObserveSource, work.Plan.EntityKind, cancellationToken);
                var observation = await gateway.ObserveSourceAsync(observe.Manifest.Id, observe.Context,
                    new(source.Selection, source.OfferId), cancellationToken);
                SourceAcquisitionValidation.Validate(observation, source.Selection, source.OfferId, source.Publication, source.Offer);
                transfer.ObserveSource(observation.State, observation.Progress, observation.Problem);
                await PersistAsync();
                await ReportAsync(observation, context, cancellationToken);

                if (observation.State == SourceAcquisitionState.NotObserved) {
                    transfer.BeginSourceRequest();
                    await PersistAsync(); // The durable ambiguity fence precedes the only source mutation.
                    var request = await access.RequireAsync(transfer.State.ConnectionId, PluginCapability.AcquisitionSource,
                        IntegrationOperation.RequestSource, work.Plan.EntityKind, cancellationToken);
                    observation = await gateway.RequestSourceAsync(request.Manifest.Id, request.Context,
                        new(operationId, source.Selection, source.OfferId), cancellationToken);
                    SourceAcquisitionValidation.Validate(observation, source.Selection, source.OfferId, source.Publication, source.Offer);
                    transfer.ObserveSource(observation.State, observation.Progress, observation.Problem);
                    await PersistAsync();
                    await ReportAsync(observation, context, cancellationToken);
                }

                // Failed source work is source-owned. An explicit retry checks again without claiming to requeue it remotely.
                if (transfer.State.Phase == IntegrationTransferPhase.NeedsReview) return;
                if (transfer.State.Phase != IntegrationTransferPhase.Transferring)
                    throw new JobRetryLaterException("Waiting for the source to prepare the selected publication.", PollDelay(observation.NextPollAfter));
            }
        } catch (JobRetryLaterException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (IntegrationTransferConflictException) { throw; }
        catch (Exception error) when (error is IntegrationInvocationException or ConnectionNotFoundException
            or ConnectionSecretUnavailableException or ConnectionCapabilityUnavailableException) {
            const string message = "Source preparation could not be confirmed. The exact accepted request is retained and will be observed again before retrying.";
            await store.RecordErrorAsync(operationId, revision, message, cancellationToken);
            throw new JobRetryLaterException(message, TimeSpan.FromMinutes(1));
        }

        // Preparation ends at the persisted Ready/Transferring boundary. Resolution, verified byte
        // transfer, and local import own their failures and revision after this delegation begins.
        if (transfer.State.Phase is IntegrationTransferPhase.Transferring or IntegrationTransferPhase.Importing)
            await sourceTransfers.ProcessAsync(operationId, context, cancellationToken);
    }

    private static Task ReportAsync(Prismedia.Contracts.Integrations.SourceAcquisitionObservation observation,
        JobContext context, CancellationToken cancellationToken) {
        var progress = observation.Progress is { } sourceProgress
            ? 10 + (int)Math.Round(sourceProgress * 40, MidpointRounding.AwayFromZero)
            : observation.State == SourceAcquisitionState.Ready ? 50 : 10;
        var message = observation.State switch {
            SourceAcquisitionState.NotObserved => "Requesting selected publication",
            SourceAcquisitionState.Queued => "Selected publication is queued at the source",
            SourceAcquisitionState.Downloading => "Source is preparing selected publication",
            SourceAcquisitionState.Ready => "Selected publication is ready for transfer",
            SourceAcquisitionState.Failed => "Source preparation needs review",
            _ => "Observing selected publication"
        };
        return context.ReportProgressAsync(progress, message, cancellationToken);
    }

    private static TimeSpan PollDelay(DateTimeOffset? nextPollAfter) =>
        TimeSpan.FromSeconds(Math.Clamp((nextPollAfter - DateTimeOffset.UtcNow)?.TotalSeconds ?? 15, 5, 3600));
}
