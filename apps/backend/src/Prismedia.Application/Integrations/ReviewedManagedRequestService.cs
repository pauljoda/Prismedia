using Prismedia.Application.Requests;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Reviews and atomically accepts metadata-led external-manager requests.</summary>
public sealed class ReviewedManagedRequestService(
    ManagedDiscoveryService discovery,
    IEnumerable<IManagedWantedWorkPreparer> preparers,
    IntegrationConnectionAccess access,
    IIntegrationManagerCreationGateway gateway,
    ManagedLibraryService library,
    ExternalLibraryService externalLibraries,
    IReviewedFulfillmentOwnershipReader fulfillmentOwnership,
    IManagedTrackingStore tracking,
    IManagedRequestStore requests,
    ManagedRequestService managedRequests,
    IReviewedManagedRequestCommitScope commitScope) {
    /// <summary>Loads canonical metadata, current provider choices, and existing holding evidence without writing.</summary>
    public async Task<ReviewedManagedRequest> ReviewAsync(
        Guid connectionId,
        ReviewManagedRequestInput input,
        CancellationToken token) {
        if (input?.Request is null) throw new RequestCommitValidationException("Submit a complete reviewed request.");
        var canonical = input.Request;
        if (input.ManagerDiscoveryRevision is { } discoveryRevision) {
            (_, canonical) = await discovery.CanonicalizeAsync(connectionId, discoveryRevision, canonical, token);
        }
        var plan = await Preparer(canonical.Kind).ReviewForManagerAsync(
            canonical,
            managerOrigin: input.ManagerDiscoveryRevision is not null,
            token);

        var authorized = await access.RequireAsync(
            connectionId,
            PluginCapability.ExternalManager,
            IntegrationOperation.LookupManaged,
            plan.Work.EntityKind,
            token);
        var lookup = await gateway.LookupAsync(authorized.Manifest.Id, authorized.Context, plan.Work, token);
        ManagedCreationEvidence.ValidateLookup(plan.Work, lookup);
        var options = await library.OptionsAsync(connectionId, new(plan.Work.EntityKind), token);
        var mount = SelectMount(
            await externalLibraries.ListSuitableAsync(connectionId, plan.Work.EntityKind, token),
            input.LibraryRootId,
            lookup.Existing);
        var existingFulfillments = await fulfillmentOwnership.ListAsync(plan.Work, token);
        var expansion = await ResolveExpansionAsync(
            connectionId, mount.LibraryRootId, plan.Work, lookup.Existing, existingFulfillments, token);
        return new(
            authorized.Connection.State.Revision,
            input.ManagerDiscoveryRevision,
            canonical,
            plan.Title,
            plan.Work,
            mount,
            options,
            lookup.Existing,
            existingFulfillments,
            expansion);
    }

    /// <summary>Commits reviewed metadata and durable fulfillment ownership as one local transaction.</summary>
    public async Task<ReviewedManagedRequestCommitResponse> CommitAsync(
        Guid connectionId,
        CommitReviewedManagedRequestInput input,
        CancellationToken token) {
        ValidateCommitInput(input);
        if (input.OperationId == Guid.Empty)
            throw new ArgumentException("Supply a stable operation ID for this manager request.");
        var reviewedFingerprint = ReviewedManagedRequestIdentity.Fingerprint(connectionId, input);
        if (await managedRequests.FindReviewedAsync(connectionId, input.OperationId, reviewedFingerprint, token) is { } replay)
            return new(replay.EntityId, replay.TargetEntityIds, replay);

        var review = await ReviewAsync(
            connectionId,
            new(input.LibraryRootId, input.Request, input.ManagerDiscoveryRevision),
            token);
        if (review.ConnectionRevision != input.ExpectedConnectionRevision)
            throw new ConnectionConflictException("The selected manager connection changed. Review its options again.");
        var policy = ManagedFulfillmentPolicy.For(review.Work.EntityKind);
        if (policy.RequiredMonitoring is { } monitoring && input.Monitored != monitoring || policy.RequiresSearch && !input.Search)
            throw new ArgumentException(policy.AppliesReviewedMonitoring
                ? "This request must be monitored and search the reviewed work."
                : "Selected targets are searched without turning on broad monitoring.");
        if (ExistingSourceResponse(review, policy) is { } owned) return owned;
        var expansion = review.Expansion;
        if (expansion is null && review.ExistingFulfillments.Count != 0)
            throw new FulfillmentOwnershipConflictException();
        if (!review.Options.Profiles.Any(profile => profile.Id == input.ProfileId))
            throw new ArgumentException("Choose an existing external profile.");
        if (review.Existing is { } existing && existing.Item.ProfileId != input.ProfileId)
            throw new ManagedRequestConflictException(
                "This work already exists with another profile. Review and use its current profile before changing it through linked controls.");
        foreach (var operation in new[] {
                     IntegrationOperation.EnsureManaged,
                     IntegrationOperation.ReconcileManaged,
                     IntegrationOperation.ConfigureManaged
                 })
            await access.RequireAsync(connectionId, PluginCapability.ExternalManager, operation, review.Work.EntityKind, token);
        if (input.Search)
            await access.RequireAsync(connectionId, PluginCapability.ExternalManager,
                IntegrationOperation.RequestManaged, review.Work.EntityKind, token);
        await access.RequireAsync(connectionId, PluginCapability.ConnectedLibrary,
            IntegrationOperation.GetLibraryItem, review.Work.EntityKind, token);

        return await commitScope.ExecuteAsync<ReviewedManagedRequestCommitResponse>(
            connectionId,
            input.ExpectedConnectionRevision,
            async ct => {
                var prepared = await Preparer(review.Request.Kind).PrepareForManagerAsync(
                    review.Request,
                    managerOrigin: input.ManagerDiscoveryRevision is not null,
                    ct);
                var entityId = prepared.EntityId;
                var targetEntityIds = prepared.MissingTargetEntityIds;
                if (expansion is { } existingHolding && targetEntityIds is not null) {
                    var retained = existingHolding.RetainedTargetEntityIds.ToHashSet();
                    targetEntityIds = targetEntityIds.Where(id => !retained.Contains(id)).ToArray();
                }

                if (prepared.HasEveryFile || targetEntityIds is { Count: 0 }) {
                    return new(entityId, targetEntityIds, ManagedRequest: null);
                }

                var target = await requests.RequireTargetAsync(
                    connectionId,
                    entityId,
                    input.LibraryRootId,
                    targetEntityIds,
                    bookRendition: null,
                    ct);
                var create = new CreateManagedRequestInput(
                    input.OperationId,
                    entityId,
                    input.LibraryRootId,
                    target.Work,
                    input.ProfileId,
                    input.Monitored,
                    input.Search,
                    targetEntityIds);
                ManagedRequestService.Validate(create);
                var preview = new ManagedRequestPreview(
                    target.EntityId,
                    target.Title,
                    target.Work,
                    target.Mount,
                    review.Options,
                    review.Existing,
                    targetEntityIds);
                var accepted = await managedRequests.AcceptAsync(
                    connectionId,
                    create,
                    preview,
                    reviewedFingerprint,
                    input.ExpectedConnectionRevision,
                    expansion?.HoldingId,
                    ct);
                return new(entityId, targetEntityIds, accepted);
            }, token);
    }

    private IManagedWantedWorkPreparer Preparer(RequestMediaKind kind) =>
        preparers.SingleOrDefault(preparer => preparer.Kind == kind)
        ?? throw new RequestCommitValidationException("This kind of reviewed request cannot be fulfilled by a connected manager.");

    private async Task<ManagedRequestExpansion?> ResolveExpansionAsync(
        Guid connectionId,
        Guid libraryRootId,
        ManagedLookupInput work,
        ManagedItemSnapshot? existing,
        IReadOnlyList<ReviewedFulfillmentOwnership> existingFulfillments,
        CancellationToken token) {
        if (!ManagedFulfillmentPolicy.For(work.EntityKind).AccumulatesTargets) return null;
        var holdings = (await tracking.ListAsync(connectionId, token))
            .Where(holding => holding.Item.EntityKind == work.EntityKind
                && holding.Status != ManagedTrackingStatus.Released
                && work.ExternalIds.All(pair =>
                    holding.Item.ExpectedExternalIds.GetValueOrDefault(pair.Key) == pair.Value))
            .ToArray();
        if (holdings.Length == 0) {
            return null;
        }
        if (holdings.Length != 1) return null;
        var holding = holdings[0];
        if (!ManagedTrackingStatusDefinition.For(holding.Status).IsEstablished
            || holding.LibraryRootId != libraryRootId
            || existing is null || existing.Item.RemoteId != holding.Item.RemoteId)
            return null;
        var ownerRequest = await requests.FindAsync(holding.Id, token);
        if (ownerRequest is not null && (ownerRequest.Operation.State.ReviewRequired
            || !ownerRequest.Operation.Phase.ProvidesHolding
            || ownerRequest.Plan.ExistingHoldingId is not null))
            return null;
        if (existingFulfillments.Any(owner => owner.OwnerKind != FulfillmentOwnerKind.ExternalManager
                || owner.ConnectionId != connectionId || owner.RequestId != holding.Id))
            return null;
        var selectedOwned = existingFulfillments
            .SelectMany(owner => owner.TargetEntityIds ?? [])
            .Distinct().Count();
        var selectedCount = work.Targets?.Count ?? 0;
        return new(
            holding.Id,
            holding.Targets.Select(target => target.EntityId).Distinct().ToArray(),
            selectedOwned,
            Math.Max(0, selectedCount - selectedOwned));
    }

    internal static void ValidateCommitInput(CommitReviewedManagedRequestInput? input) {
        if (input?.Request is null)
            throw new RequestCommitValidationException("Submit a complete reviewed request.");
        if (input.Request.SelectedProposalIds is null)
            throw new RequestCommitValidationException("Submit the reviewed proposal selection.");
    }

    /// <summary>
    /// A work requested as a whole is already satisfied by any local source; a work that accumulates targets is
    /// satisfied only when every reviewed target already has one.
    /// </summary>
    private static ReviewedManagedRequestCommitResponse? ExistingSourceResponse(ReviewedManagedRequest review,
        ManagedFulfillmentPolicy policy) {
        var locallyOwned = review.ExistingFulfillments
            .Where(ownership => ownership.HasLocalSource)
            .ToArray();
        if (!policy.AccumulatesTargets)
            return locallyOwned.Length != 0 ? new(locallyOwned[0].EntityId, TargetEntityIds: null, ManagedRequest: null) : null;
        if (review.Work.Targets is null) return null;
        var targetIds = locallyOwned
            .SelectMany(ownership => ownership.TargetEntityIds ?? [])
            .Distinct()
            .ToArray();
        return targetIds.Length == review.Work.Targets.Count
            ? new(locallyOwned[0].EntityId, targetIds, ManagedRequest: null)
            : null;
    }

    private static ExternalLibraryMount SelectMount(
        IReadOnlyList<ExternalLibraryMount> mounts,
        Guid? requestedRootId,
        ManagedItemSnapshot? existing) {
        if (requestedRootId is { } explicitRoot) {
            return mounts.SingleOrDefault(mount => mount.LibraryRootId == explicitRoot)
                ?? throw new ArgumentException("Choose an enabled library mapped to an accessible root on this connection.");
        }
        if (existing is not null) {
            var containing = mounts
                .Where(mount => RemoteLibraryPath.Parse(mount.RemotePath).IsAncestorOf(existing.Path))
                .OrderByDescending(mount => mount.RemotePath.Length)
                .FirstOrDefault();
            if (containing is not null) return containing;
        }
        return mounts.FirstOrDefault()
            ?? throw new ArgumentException("Map an accessible external library in Settings before requesting this work.");
    }
}
