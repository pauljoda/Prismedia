using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Explicit manager controls for already linked, exclusively owned holdings.</summary>
public sealed class ManagedControlService(IManagedControlStore store, IntegrationConnectionAccess access,
    IIntegrationManagerControlGateway gateway, ManagedLibraryService library) {
    /// <summary>Reads current configuration for the server-derived finite owned scope.</summary>
    public async Task<ManagedControlPreview> PreviewAsync(Guid connectionId, Guid holdingId, CancellationToken token) {
        var owned = await store.RequireScopeAsync(connectionId, holdingId, token);
        return await PreviewAsync(connectionId, owned, token);
    }
    internal async Task<ManagedControlPreview> PreviewAsync(Guid connectionId, Guid holdingId,
        IReadOnlyList<Guid> entityIds, CancellationToken token) {
        var owned = await store.RequireScopeAsync(connectionId, holdingId, entityIds, token);
        return await PreviewAsync(connectionId, owned, token);
    }
    private async Task<ManagedControlPreview> PreviewAsync(Guid connectionId, OwnedManagedControlScope owned, CancellationToken token) {
        var connection = await access.RequireAsync(connectionId, PluginCapability.ExternalManager, IntegrationOperation.ReconcileManaged, owned.Scope.Item.EntityKind, token);
        var state = await gateway.ReconcileAsync(connection.Manifest.Id, connection.Context, new(owned.Scope), token);
        ManagedControlValidation.Validate(owned.Scope, state);
        return new(owned.Fingerprint, state, await library.OptionsAsync(connectionId, new(owned.Scope.Item.EntityKind), token));
    }
    /// <summary>Accepts one reviewed action and durable queue intent before any remote mutation.</summary>
    public async Task<ManagedControlActionResponse> CreateAsync(Guid connectionId, Guid holdingId, CreateManagedControlRequest request, CancellationToken token) {
        return await CreateAsync(connectionId, holdingId, request, null, token);
    }
    internal async Task<ManagedControlActionResponse> CreateAsync(Guid connectionId, Guid holdingId,
        CreateManagedControlRequest request, IReadOnlyList<Guid>? scopeEntityIds, CancellationToken token) {
        Validate(request);
        var fingerprint = ManagedControlIdentity.RequestFingerprint(request);
        if (await store.FindAsync(request.OperationId, token) is { } existing) {
            if (existing.Operation.State.ConnectionId != connectionId || existing.Operation.State.HoldingId != holdingId || existing.Plan.RequestFingerprint != fingerprint)
                throw new ManagedControlConflictException("This operation ID already accepted a different manager action.");
            return Map(existing);
        }
        var owned = scopeEntityIds is null
            ? await store.RequireScopeAsync(connectionId, holdingId, token)
            : await store.RequireScopeAsync(connectionId, holdingId, scopeEntityIds, token);
        if (owned.Fingerprint != request.ScopeFingerprint || !owned.Scope.Targets.Select(target => target.RemoteId).ToHashSet(StringComparer.Ordinal).SetEquals(request.ExpectedMonitoring.Keys))
            throw new ManagedControlConflictException("The reviewed target scope changed. Refresh the manager controls.");
        await access.RequireAsync(connectionId, PluginCapability.ExternalManager, IntegrationOperation.ReconcileManaged, owned.Scope.Item.EntityKind, token);
        var configure = request.Changes.ProfileId is not null || request.Changes.Monitored is not null;
        if (configure) await access.RequireAsync(connectionId, PluginCapability.ExternalManager, IntegrationOperation.ConfigureManaged, owned.Scope.Item.EntityKind, token);
        if (request.Search) await access.RequireAsync(connectionId, PluginCapability.ExternalManager, IntegrationOperation.RequestManaged, owned.Scope.Item.EntityKind, token);
        var action = ManagedControlOperation.Create(request.OperationId, connectionId, holdingId, configure, request.Search);
        return Map(await store.CreateAsync(action, new(owned.Scope, request, fingerprint, scopeEntityIds), token));
    }
    /// <summary>Reads retained results even while the connection is disabled or unavailable.</summary>
    public async Task<IReadOnlyList<ManagedControlActionResponse>> ListAsync(Guid connectionId, Guid holdingId, CancellationToken token) =>
        (await store.ListAsync(connectionId, holdingId, token)).Select(Map).ToArray();
    /// <summary>Queues observation without repeating an uncertain manager mutation.</summary>
    public async Task RefreshAsync(Guid connectionId, Guid holdingId, Guid id, CancellationToken token) {
        await RequireAsync(connectionId, holdingId, id, token);
        await store.QueueAsync(id, token);
    }
    /// <summary>Cancels only the next unsent stage, preserving previously confirmed configuration and fulfillment ownership.</summary>
    public Task<ManagedControlActionResponse> CancelAsync(Guid connectionId, Guid holdingId, Guid id, long revision, CancellationToken token) =>
        ChangeAsync(connectionId, holdingId, id, revision, operation => operation.Cancel(), token);
    /// <summary>Acknowledges unresolved execution without claiming success, rolling back settings, or stopping remote work.</summary>
    public Task<ManagedControlActionResponse> CloseUnverifiedAsync(Guid connectionId, Guid holdingId, Guid id, long revision, CancellationToken token) =>
        ChangeAsync(connectionId, holdingId, id, revision, operation => operation.CloseUnverified(), token);

    private async Task<ManagedControlActionResponse> ChangeAsync(Guid connectionId, Guid holdingId, Guid id, long revision,
        Action<ManagedControlOperation> change, CancellationToken token) {
        var work = await RequireAsync(connectionId, holdingId, id, token);
        if (work.Operation.State.Revision != revision) throw new ManagedControlConflictException("This action changed. Reload its progress first.");
        try { change(work.Operation); }
        catch (InvalidOperationException error) { throw new ManagedControlConflictException(error.Message); }
        await store.SaveAsync(work.Operation, revision, work.Problem, false, token);
        return Map((await store.FindAsync(id, token))!);
    }
    private async Task<StoredManagedControl> RequireAsync(Guid connectionId, Guid holdingId, Guid id, CancellationToken token) {
        var work = await store.FindAsync(id, token);
        if (work is null || work.Operation.State.ConnectionId != connectionId || work.Operation.State.HoldingId != holdingId)
            throw new ArgumentException("The manager action does not belong to this connected holding.");
        return work;
    }
    private static void Validate(CreateManagedControlRequest request) {
        if (request.OperationId == Guid.Empty || request.ScopeFingerprint is not { Length: 64 } || !request.ScopeFingerprint.All(Uri.IsHexDigit)
            || string.IsNullOrWhiteSpace(request.ExpectedPath) || request.ExpectedPath.Length > 8192
            || string.IsNullOrWhiteSpace(request.ExpectedProfileId) || request.ExpectedProfileId.Length > 512
            || request.ExpectedMonitoring is not { Count: > 0 and <= 10000 } || request.ExpectedMonitoring.Keys.Any(key => string.IsNullOrWhiteSpace(key) || key.Length > 512)
            || request.Changes is null || request.Changes.ProfileId is { } profile && (string.IsNullOrWhiteSpace(profile) || profile.Length > 512)
            || request.Changes.ProfileId is null && request.Changes.Monitored is null && !request.Search)
            throw new ArgumentException("Choose an explicit setting change or search from a fresh manager preview.");
    }
    private static ManagedControlActionResponse Map(StoredManagedControl work) {
        var operation = work.Operation; var state = operation.State;
        return new(state.OperationId, state.ConnectionId, state.HoldingId, state.Revision, state.Phase, work.Plan.Request.Changes,
            state.SearchRequested, state.ConfigurationConfirmed, state.Command is { } command
                ? new(new(command.Id, command.QueuedAt), state.CommandStatus ?? ManagedCommandStatus.Pending) : null,
            state.ReviewRequired, operation.CanCancel, operation.CanCloseUnverified, work.CreatedAt, work.UpdatedAt, work.Problem);
    }
}

/// <summary>Host-owned validation of manager observations before they influence a saved action.</summary>
public static class ManagedControlValidation {
    /// <summary>Requires exact pinned work identity, complete finite target coverage, and usable configuration.</summary>
    public static void Validate(ManagedControlScope scope, ManagedControlState state) {
        if (state?.Item is null || state.Capabilities is null || state.Item.EntityKind != scope.Item.EntityKind || state.Item.RemoteId != scope.Item.RemoteId
            || state.Item.ExternalIds is null || scope.Item.ExpectedExternalIds.Any(pair => !state.Item.ExternalIds.TryGetValue(pair.Key, out var value) || value != pair.Value)
            || string.IsNullOrWhiteSpace(state.Path) || state.Path.Length > 8192 || string.IsNullOrWhiteSpace(state.Item.ProfileId)
            || state.Targets is null || state.Targets.Count != scope.Targets.Count || state.Targets.Any(target => target?.Target is null)
            || !scope.Targets.OrderBy(target => target.RemoteId, StringComparer.Ordinal).SequenceEqual(state.Targets.Select(target => target.Target).OrderBy(target => target.RemoteId, StringComparer.Ordinal)))
            throw new IntegrationInvocationException("The manager returned a changed identity or incomplete configuration for the reviewed scope.");
    }
}
