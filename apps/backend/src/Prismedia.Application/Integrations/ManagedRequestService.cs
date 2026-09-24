using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Accepts explicit finite wanted-work fulfillment through a chosen external manager and mapped library.</summary>
public sealed class ManagedRequestService(IManagedRequestStore store, IntegrationConnectionAccess access,
    IIntegrationManagerCreationGateway gateway, ManagedLibraryService library, IManagedTrackingStore tracking) {
    /// <summary>Shows exact identity and existing remote settings without taking ownership or creating a holding.</summary>
    public async Task<ManagedRequestPreview> PreviewAsync(Guid connectionId, PreviewManagedRequestInput input, CancellationToken token) {
        var target = await store.RequireTargetAsync(
            connectionId,
            input.EntityId,
            input.LibraryRootId,
            input.TargetEntityIds,
            input.BookRendition,
            token);
        var connection = await access.RequireAsync(connectionId, PluginCapability.ExternalManager, IntegrationOperation.LookupManaged, target.Work.EntityKind, token);
        var lookup = await gateway.LookupAsync(connection.Manifest.Id, connection.Context, target.Work, token);
        ManagedCreationEvidence.ValidateLookup(target.Work, lookup);
        var options = await library.OptionsAsync(connectionId, new(target.Work.EntityKind, target.Work.BookRendition), token);
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
            new(input.EntityId, input.LibraryRootId, input.TargetEntityIds, input.ReviewedWork.BookRendition),
            token);
        if (!ManagedRequestIdentity.SameWork(preview.Work, input.ReviewedWork))
            throw new ManagedRequestConflictException("The wanted item's metadata identity changed. Review the request again.");
        var policy = ManagedFulfillmentPolicy.For(preview.Work.EntityKind);
        RequireReviewedHolding(policy, preview, input);
        foreach (var operation in new[] { IntegrationOperation.ReconcileManaged, IntegrationOperation.ConfigureManaged })
            await access.RequireAsync(connectionId, PluginCapability.ExternalManager, operation, preview.Work.EntityKind, token);
        if (preview.Existing is null && policy.CreatesHolding)
            await access.RequireAsync(connectionId, PluginCapability.ExternalManager, IntegrationOperation.EnsureManaged, preview.Work.EntityKind, token);
        if (input.Search) await access.RequireAsync(connectionId, PluginCapability.ExternalManager, IntegrationOperation.RequestManaged, preview.Work.EntityKind, token);
        await access.RequireAsync(connectionId, PluginCapability.ConnectedLibrary, IntegrationOperation.GetLibraryItem, preview.Work.EntityKind, token);
        return await AcceptAsync(connectionId, input, preview, reviewedCommitFingerprint: null,
            expectedConnectionRevision: null, existingHoldingId: null, token);
    }

    /// <summary>Returns a previously accepted reviewed commit without requiring current provider availability.</summary>
    internal async Task<ManagedRequestResponse?> FindReviewedAsync(
        Guid connectionId,
        Guid operationId,
        string reviewedCommitFingerprint,
        CancellationToken token) {
        if (await store.FindAsync(operationId, token) is not { } existing) return null;
        if (existing.Operation.State.ConnectionId != connectionId
            || !string.Equals(existing.Plan.ReviewedCommitFingerprint, reviewedCommitFingerprint, StringComparison.Ordinal))
            throw new ManagedRequestConflictException("This operation ID already accepted a different managed request.");
        return Map(existing);
    }

    /// <summary>Accepts an already preflighted request using only server-derived local entity identities.</summary>
    internal async Task<ManagedRequestResponse> AcceptAsync(
        Guid connectionId,
        CreateManagedRequestInput input,
        ManagedRequestPreview preview,
        string? reviewedCommitFingerprint,
        long? expectedConnectionRevision,
        Guid? existingHoldingId,
        CancellationToken token) {
        Validate(input);
        var fingerprint = ManagedRequestIdentity.Fingerprint(input);
        if (await store.FindAsync(input.OperationId, token) is { } existing) {
            if (existing.Operation.State.ConnectionId != connectionId || existing.Plan.Fingerprint != fingerprint
                || reviewedCommitFingerprint is not null
                    && !string.Equals(existing.Plan.ReviewedCommitFingerprint, reviewedCommitFingerprint, StringComparison.Ordinal))
                throw new ManagedRequestConflictException("This operation ID already accepted a different managed request.");
            return Map(existing);
        }
        var target = await store.RequireTargetAsync(
            connectionId,
            input.EntityId,
            input.LibraryRootId,
            input.TargetEntityIds,
            input.ReviewedWork.BookRendition,
            token);
        if (!ManagedRequestIdentity.SameWork(target.Work, input.ReviewedWork)
            || target.Mount.Id != preview.Mount.Id
            || target.Mount.RemoteRootId != preview.Mount.RemoteRootId
            || target.Mount.RemotePath != preview.Mount.RemotePath)
            throw new ManagedRequestConflictException("The wanted identity or mapped library changed after review.");
        var policy = ManagedFulfillmentPolicy.For(preview.Work.EntityKind);
        RequireReviewedHolding(policy, preview, input);
        if (!policy.CreatesHolding && preview.Existing is { } linked)
            existingHoldingId = await ExistingHoldingAsync(connectionId, input.LibraryRootId, linked, token);
        var action = ManagedRequestOperation.Create(input.OperationId, connectionId, input.EntityId, input.LibraryRootId);
        var plan = new ManagedRequestPlan(
            input,
            new(input.OperationId, target.Work, input.ProfileId, target.Mount.RemoteRootId, target.Mount.RemotePath),
            target.Title,
            fingerprint,
            reviewedCommitFingerprint,
            expectedConnectionRevision,
            existingHoldingId);
        return Map(await store.CreateAsync(action, plan, token));
    }
    /// <summary>
    /// Requires an existing manager profile when the kind uses one, an existing remote holding when the kind's
    /// requests cannot create it, and the holding's current profile when it already exists.
    /// </summary>
    private static void RequireReviewedHolding(ManagedFulfillmentPolicy policy, ManagedRequestPreview preview,
        CreateManagedRequestInput input) {
        if (policy.UsesProfile && !preview.Options.Profiles.Any(profile => profile.Id == input.ProfileId)) {
            throw new ArgumentException("Choose an existing external profile.");
        }

        if (!policy.CreatesHolding && preview.Existing is null) {
            throw new ManagedRequestConflictException(
                "The connected manager must already hold this work. Refresh its metadata before requesting it.");
        }

        if (preview.Existing is { } holding && holding.Item.ProfileId != input.ProfileId) {
            throw new ManagedRequestConflictException("This work already exists with another profile. "
                + "Review and use its current profile before changing it through linked controls.");
        }
    }

    /// <summary>Finds the one established holding a request that cannot create its holding must extend.</summary>
    private async Task<Guid?> ExistingHoldingAsync(Guid connectionId, Guid libraryRootId,
        ManagedItemSnapshot snapshot, CancellationToken token) {
        var matches = (await tracking.ListAsync(connectionId, token)).Where(holding =>
            holding.LibraryRootId == libraryRootId
            && holding.Item.EntityKind == snapshot.Item.EntityKind
            && holding.Item.RemoteId == snapshot.Item.RemoteId
            && holding.Item.ExpectedExternalIds.All(pair => snapshot.Item.ExternalIds.GetValueOrDefault(pair.Key) == pair.Value)
            && ManagedTrackingStatusDefinition.For(holding.Status).IsEstablished
            && holding.ReleasedAt is null).ToArray();
        if (matches.Length > 1)
            throw new ManagedRequestConflictException("More than one linked holding matches this work. Review its associations first.");
        return matches.SingleOrDefault()?.Id;
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
    /// <summary>
    /// Validates the bounded request shape, then asks the requested kind's managed-fulfillment policy for its
    /// profile, target, monitoring, and search rules.
    /// </summary>
    /// <exception cref="ArgumentException">The request is malformed or breaks one of its kind's rules.</exception>
    internal static void Validate(CreateManagedRequestInput input) {
        if (input.OperationId == Guid.Empty || input.EntityId == Guid.Empty || input.LibraryRootId == Guid.Empty
            || input.ReviewedWork is not { ExternalIds.Count: > 0 and <= 64 }
            || input.ReviewedWork.ExternalIds.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 128
                || string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Length > 2048)
            || input.ProfileId is { Length: > 512 }) {
            throw new ArgumentException("Select reviewed wanted work, its exact target, and a mapped external library.");
        }

        var targetIds = input.TargetEntityIds ?? [];
        var reviewedTargets = input.ReviewedWork.Targets ?? [];
        if (targetIds.Any(id => id == Guid.Empty) || targetIds.Distinct().Count() != targetIds.Count
            || reviewedTargets.Count != targetIds.Count) {
            throw new ArgumentException("Each selected target needs one unique reviewed identity.");
        }

        var policy = ManagedFulfillmentPolicy.For(input.ReviewedWork.EntityKind);
        var target = policy.TargetFor(input.ReviewedWork.BookRendition);
        if (reviewedTargets.Any(reviewed => reviewed.EntityKind != target.Kind)) {
            throw new ArgumentException("A selected target does not belong to this kind of work.");
        }

        policy.RequireRequest(input.ProfileId, input.ReviewedWork.BookRendition, targetIds.Count,
            reviewedTargets.Select(reviewed => reviewed.IssueLabel).ToArray(), input.Monitored, input.Search);
    }

    internal static ManagedRequestResponse Map(StoredManagedRequest work) {
        var state = work.Operation.State;
        return new(state.OperationId, state.ConnectionId, state.EntityId, state.LibraryRootId, work.Plan.DisplayTitle(), state.Phase,
            state.Revision, state.RemoteId, work.Plan.Request.Monitored, work.Plan.Request.Search, state.ReviewRequired,
            work.Operation.CanCancel, work.CreatedAt, work.UpdatedAt, work.Problem,
            work.Plan.ExistingHoldingId ?? state.OperationId,
            work.Plan.Request.TargetEntityIds);
    }
}
