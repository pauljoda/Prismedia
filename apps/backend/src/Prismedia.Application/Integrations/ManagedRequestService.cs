using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Accepts explicit finite wanted-work fulfillment through a chosen external manager and mapped library.</summary>
public sealed class ManagedRequestService(IManagedRequestStore store, IntegrationConnectionAccess access,
    IIntegrationManagerCreationGateway gateway, ManagedLibraryService library) {
    /// <summary>Shows exact identity and existing remote settings without taking ownership or creating a holding.</summary>
    public async Task<ManagedRequestPreview> PreviewAsync(Guid connectionId, PreviewManagedRequestInput input, CancellationToken token) {
        var target = await store.RequireTargetAsync(
            connectionId,
            input.EntityId,
            input.LibraryRootId,
            input.TargetEntityIds,
            token);
        var connection = await access.RequireAsync(connectionId, PluginCapability.ExternalManager, IntegrationOperation.LookupManaged, target.Work.EntityKind, token);
        var lookup = await gateway.LookupAsync(connection.Manifest.Id, connection.Context, target.Work, token);
        ManagedCreationEvidence.ValidateLookup(target.Work, lookup);
        var options = await library.OptionsAsync(connectionId, new(target.Work.EntityKind), token);
        if (!options.Roots.Any(root => root.Id == target.Mount.RemoteRootId && root.Path == target.Mount.RemotePath && root.Accessible != false))
            throw new ArgumentException("The mapped external root changed or became inaccessible. Review its connection before requesting this work.");
        return new(target.EntityId, target.Title, target.Work, target.Mount, options, lookup.Existing,
            target.Targets?.Select(item => item.EntityId).ToArray());
    }
    /// <summary>Commits the reviewed request and exclusive fulfillment owner before publishing any remote mutation.</summary>
    public async Task<ManagedRequestResponse> CreateAsync(Guid connectionId, CreateManagedRequestInput input, CancellationToken token) {
        Validate(input);
        var fingerprint = ManagedRequestIdentity.Fingerprint(input);
        if (await store.FindAsync(input.OperationId, token) is { } existing) {
            if (existing.Operation.State.ConnectionId != connectionId || existing.Plan.Fingerprint != fingerprint)
                throw new ManagedRequestConflictException("This operation ID already accepted a different managed request.");
            return Map(existing);
        }
        var preview = await PreviewAsync(
            connectionId,
            new(input.EntityId, input.LibraryRootId, input.TargetEntityIds),
            token);
        if (!ManagedRequestIdentity.SameWork(preview.Work, input.ReviewedWork))
            throw new ManagedRequestConflictException("The wanted item's metadata identity changed. Review the request again.");
        if (!preview.Options.Profiles.Any(profile => profile.Id == input.ProfileId)) throw new ArgumentException("Choose an existing external profile.");
        if (preview.Existing is { } holding && holding.Item.ProfileId != input.ProfileId)
            throw new ManagedRequestConflictException("This work already exists with another profile. Review and use its current profile before changing it through linked controls.");
        foreach (var operation in new[] { IntegrationOperation.EnsureManaged, IntegrationOperation.ReconcileManaged, IntegrationOperation.ConfigureManaged })
            await access.RequireAsync(connectionId, PluginCapability.ExternalManager, operation, preview.Work.EntityKind, token);
        if (input.Search) await access.RequireAsync(connectionId, PluginCapability.ExternalManager, IntegrationOperation.RequestManaged, preview.Work.EntityKind, token);
        await access.RequireAsync(connectionId, PluginCapability.ConnectedLibrary, IntegrationOperation.GetLibraryItem, preview.Work.EntityKind, token);
        var action = ManagedRequestOperation.Create(input.OperationId, connectionId, input.EntityId, input.LibraryRootId);
        var plan = new ManagedRequestPlan(input, new(input.OperationId, preview.Work, input.ProfileId, preview.Mount.RemoteRootId, preview.Mount.RemotePath), preview.Title, fingerprint);
        return Map(await store.CreateAsync(action, plan, token));
    }
    /// <summary>Lists durable intent independently of current connection health.</summary>
    public async Task<IReadOnlyList<ManagedRequestResponse>> ListAsync(Guid connectionId, CancellationToken token) =>
        (await store.ListAsync(connectionId, token)).Select(Map).ToArray();
    /// <summary>Requests a fresh observation; uncertain creation is never reset for redispatch.</summary>
    public async Task RefreshAsync(Guid connectionId, Guid id, CancellationToken token) {
        await RequireAsync(connectionId, id, token); await store.QueueAsync(id, token);
    }
    /// <summary>Cancels only undispatched or definitely rejected creation and releases its reserved owner atomically.</summary>
    public async Task<ManagedRequestResponse> CancelAsync(Guid connectionId, Guid id, long revision, CancellationToken token) {
        var work = await RequireAsync(connectionId, id, token);
        if (work.Operation.State.Revision != revision) throw new ManagedRequestConflictException("This request changed. Reload its progress before cancelling.");
        try { work.Operation.Cancel(); }
        catch (InvalidOperationException error) { throw new ManagedRequestConflictException(error.Message); }
        await store.SaveAsync(work.Operation, revision, null, false, token);
        return Map((await store.FindAsync(id, token))!);
    }
    private async Task<StoredManagedRequest> RequireAsync(Guid connectionId, Guid id, CancellationToken token) {
        var work = await store.FindAsync(id, token);
        if (work is null || work.Operation.State.ConnectionId != connectionId) throw new ArgumentException("This connection does not own the request.");
        return work;
    }
    internal static void Validate(CreateManagedRequestInput input) {
        if (input.OperationId == Guid.Empty || input.EntityId == Guid.Empty || input.LibraryRootId == Guid.Empty
            || input.ReviewedWork is not { EntityKind: EntityKind.Movie or EntityKind.VideoSeries, ExternalIds.Count: > 0 and <= 64 }
            || input.ReviewedWork.ExternalIds.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 128
                || string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Length > 2048)
            || string.IsNullOrWhiteSpace(input.ProfileId) || input.ProfileId.Length > 512
            || input.ReviewedWork.EntityKind == EntityKind.Movie
                && ((input.TargetEntityIds?.Count ?? 0) != 0 || (input.ReviewedWork.Targets?.Count ?? 0) != 0)
            || input.ReviewedWork.EntityKind == EntityKind.VideoSeries
                && (input.TargetEntityIds is not { Count: > 0 }
                    || input.TargetEntityIds.Any(id => id == Guid.Empty)
                    || input.TargetEntityIds.Distinct().Count() != input.TargetEntityIds.Count
                    || input.ReviewedWork.Targets is not { Count: > 0 }
                    || input.ReviewedWork.Targets.Count != input.TargetEntityIds.Count
                    || input.Monitored
                    || !input.Search))
            throw new ArgumentException("Select a reviewed wanted movie or finite series episode set, mapped root, and external profile.");
    }
    private static ManagedRequestResponse Map(StoredManagedRequest work) {
        var state = work.Operation.State;
        return new(state.OperationId, state.ConnectionId, state.EntityId, state.LibraryRootId, work.Plan.Title, state.Phase,
            state.Revision, state.RemoteId, work.Plan.Request.Monitored, work.Plan.Request.Search, state.ReviewRequired,
            work.Operation.CanCancel, work.CreatedAt, work.UpdatedAt, work.Problem,
            work.Plan.Request.TargetEntityIds);
    }
}
