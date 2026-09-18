using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Links reviewed existing holdings and reconciles only their established local identities.</summary>
public sealed class ManagedTrackingService(
    ManagedLibraryService library,
    IManagedTrackingStore store,
    IExternalPeopleEnrichmentScheduler? peopleEnrichment = null) {
    /// <summary>Lists durable tracking without contacting the remote app.</summary>
    public Task<IReadOnlyList<ManagedTrackingResponse>> ListAsync(Guid connectionId, CancellationToken token) => store.ListAsync(connectionId, token);

    /// <summary>Suggests associations only from exact mapped paths and coordinates; ambiguous scopes remain reviewable.</summary>
    public async Task<ManagedTrackingPreview> PreviewAsync(Guid connectionId, ManagedItemInput input, CancellationToken token) {
        var remote = await library.GetAsync(connectionId, input, token);
        var observation = await store.ObserveAsync(connectionId, remote, token);
        var selections = observation.Files.SelectMany(file => file.Targets.Select(target => {
            var owners = observation.Sources.Where(owner => owner.LocalPath == file.LocalPath && owner.Kind == target.Kind
                && owner.SeasonNumber == target.SeasonNumber && owner.EpisodeNumber == target.EpisodeNumber
                && (target.AbsoluteNumber is null || owner.AbsoluteNumber == target.AbsoluteNumber)).ToArray();
            return owners.Length == 1 ? new ManagedBindingSelection(target.RemoteTargetId, owners[0].EntityId, owners[0].SourceFileId) : null;
        })).OfType<ManagedBindingSelection>().ToArray();
        var plan = ManagedSourceAdoption.Plan(observation.Files, selections, observation.Sources);
        return new(observation.LibraryRootId, selections, observation.Sources,
            observation.LibraryRootId is null ? "Map all files to one local library before linking this holding." : plan.ReviewReason);
    }

    /// <summary>Accepts explicit associations as durable intent; the worker repeats all checks before adopting them.</summary>
    public async Task<ManagedTrackingResponse> TrackAsync(Guid connectionId, TrackManagedHoldingRequest request, CancellationToken token) {
        if (request.OperationId == Guid.Empty || request.LibraryRootId == Guid.Empty || request.Selections is not { Count: > 0 and <= 10000 }
            || request.Selections.Any(item => item is null || item.EntityId == Guid.Empty || item.SourceFileId == Guid.Empty
                || string.IsNullOrWhiteSpace(item.RemoteTargetId) || item.RemoteTargetId.Length > 512))
            throw new ArgumentException("Select the existing local sources for this holding.");
        if (await store.FindAsync(request.OperationId, token) is { } accepted)
            return await store.CreateAsync(connectionId, request, accepted.Tracking.Title, token);
        var remote = await library.GetAsync(connectionId, request.Item, token);
        var observation = await store.ObserveAsync(connectionId, remote, token);
        if (observation.LibraryRootId != request.LibraryRootId) throw new ArgumentException("The selected files no longer belong to this mapped library.");
        var plan = ManagedSourceAdoption.Plan(observation.Files, request.Selections, observation.Sources);
        if (plan.ReviewReason is not null) throw new ArgumentException(plan.ReviewReason);
        return await store.CreateAsync(connectionId, request, remote.Item.Title, token);
    }

    /// <summary>Queues one finite refresh under the same resource used by ordinary library scanning.</summary>
    public Task RefreshAsync(Guid connectionId, Guid id, CancellationToken token) => store.QueueAsync(connectionId, id, token);

    /// <summary>Observes remote state before applying an all-or-nothing domain decision to the same saved local owners.</summary>
    public async Task ReconcileAsync(Guid id, CancellationToken token) {
        var work = await store.FindAsync(id, token) ?? throw new ArgumentException("This tracked holding no longer exists.");
        if (work.Tracking.Status is ManagedTrackingStatus.ReleasePending or ManagedTrackingStatus.Released) return;
        try {
            var remote = await library.GetAsync(work.Tracking.ConnectionId, work.Tracking.Item, token);
            if (work.Tracking.Status == ManagedTrackingStatus.Removed) {
                await store.RecordReappearanceAsync(id, work.Tracking.Revision,
                    "The removed remote identity exists again. Review it before restoring this association.", token);
                return;
            }
            var observation = await store.ObserveAsync(work.Tracking.ConnectionId, remote, token);
            if (remote.Files.Count > 0 && observation.LibraryRootId != work.Tracking.LibraryRootId)
                throw new ArgumentException("The holding moved outside its established mapping. Review its library boundary.");
            if (work.Tracking.Bindings.Count == 0) {
                var plan = ManagedSourceAdoption.Plan(observation.Files, work.Selections, observation.Sources);
                if (plan.ReviewReason is not null) throw new ArgumentException(plan.ReviewReason);
                await store.ApplyAsync(work, observation, plan.Bindings, [], token);
            } else {
                var plan = ManagedSourceReconciliation.Plan(work.Tracking.Bindings, observation.Files);
                if (plan.ReviewReason is not null) throw new ArgumentException(plan.ReviewReason);
                await store.ApplyAsync(work, observation, null, plan.Changes, token);
            }
            if (peopleEnrichment is not null) {
                try {
                    await peopleEnrichment.ScheduleAsync(work.Tracking.Id, token);
                } catch (ArgumentException error) {
                    var current = await store.FindAsync(id, token);
                    if (current is not null) {
                        await store.RecordProblemAsync(
                            id,
                            current.Tracking.Revision,
                            ManagedTrackingStatus.NeedsReview,
                            error.Message,
                            token);
                    }
                }
            }
        } catch (IntegrationInvocationException error) when (error.Code == IntegrationErrorCode.ManagedItemNotFound) {
            await store.ConfirmRemovalAsync(work,
                "The connected manager no longer contains this holding. Local files, metadata, and history were retained.", token);
        } catch (Exception error) when (error is IntegrationInvocationException or ConnectionNotFoundException or ConnectionSecretUnavailableException or ConnectionCapabilityUnavailableException) {
            var status = work.Tracking.Status == ManagedTrackingStatus.Removed
                ? ManagedTrackingStatus.Removed
                : ManagedTrackingStatus.Stale;
            var problem = status == ManagedTrackingStatus.Removed
                ? "The connection could not be verified. The last confirmed removal and local data were retained."
                : "The connection could not be verified. Previous bindings are retained; no alternate acquisition was started.";
            await store.RecordProblemAsync(id, work.Tracking.Revision, status, problem, token);
        } catch (ArgumentException error) {
            await store.RecordProblemAsync(id, work.Tracking.Revision, ManagedTrackingStatus.NeedsReview, error.Message, token);
        }
    }
}
