using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>
/// The one intake for connected-manager requests. A request names its work through exactly one source (a
/// reviewed metadata selection, an existing wanted Entity, or a target inside a holding the manager already
/// has) and asks for one or more fulfillment scopes (the whole work, or each independently fulfilled
/// rendition). The requested kind's <see cref="ManagedFulfillmentPolicy"/> decides what a scope needs; the
/// source's discovered preparer materializes the local work. Nothing here names a particular kind.
/// </summary>
public sealed class ReviewedManagedRequestService(
    ManagedDiscoveryService discovery,
    IEnumerable<IManagedWantedWorkPreparer> wantedPreparers,
    IEnumerable<IManagedConnectedTargetPreparer> connectedPreparers,
    IntegrationConnectionAccess access,
    IIntegrationManagerCreationGateway gateway,
    ManagedLibraryService library,
    ExternalLibraryService externalLibraries,
    IReviewedFulfillmentOwnershipReader fulfillmentOwnership,
    IManagedTrackingStore tracking,
    IManagedRequestStore requests,
    ManagedRequestService managedRequests,
    IReviewedManagedRequestCommitScope commitScope,
    ILogger<ReviewedManagedRequestService> logger) {
    #region Static Variables

    private const int MaximumProfileIdLength = 512;

    #endregion

    #region Actions - Review

    /// <summary>Loads the work, current manager choices, and ownership evidence for every scope without writing.</summary>
    public async Task<ReviewedManagedRequest> ReviewAsync(
        Guid connectionId,
        ReviewManagedRequestInput input,
        CancellationToken token) =>
        (await ReviewCoreAsync(connectionId, input, token)).Review;

    private async Task<(ReviewedManagedRequest Review, RequestedWork Work)> ReviewCoreAsync(
        Guid connectionId,
        ReviewManagedRequestInput input,
        CancellationToken token) {
        ValidateSource(input.EntityId, input.Request, input.Connected);
        if (input.Scopes is not { Count: > 0 }) {
            throw new ArgumentException("Choose at least one fulfillment scope.");
        }

        var work = await ResolveWorkAsync(connectionId, input, token);
        var policy = ManagedFulfillmentPolicy.For(work.Kind);
        RequireScopes(policy, input.Scopes, requireLibrary: work.EntityId is not null);
        var authorized = await access.RequireAsync(connectionId, PluginCapability.ExternalManager,
            IntegrationOperation.LookupManaged, work.Kind, token);
        var mounts = await externalLibraries.ListSuitableAsync(connectionId, work.Kind, token);
        var scopes = new List<ReviewedManagedRequestScope>(input.Scopes.Count);
        foreach (var scope in input.Scopes) {
            scopes.Add(await ReviewScopeAsync(connectionId, authorized, work, policy, mounts, scope, token));
        }

        var review = new ReviewedManagedRequest(
            authorized.Connection.State.Revision,
            work.ManagerDiscoveryRevision,
            work.Title,
            scopes,
            work.EntityId,
            work.Request,
            input.Connected,
            work.Targets);
        return (review, work);
    }

    private async Task<ReviewedManagedRequestScope> ReviewScopeAsync(
        Guid connectionId,
        AuthorizedIntegrationConnection authorized,
        RequestedWork work,
        ManagedFulfillmentPolicy policy,
        IReadOnlyList<ExternalLibraryMount> mounts,
        ManagedRequestScopeChoice scope,
        CancellationToken token) {
        ManagedLookupInput scopeWork;
        IReadOnlyList<Guid>? targetEntityIds = null;
        ExternalLibraryMount? storeMount = null;
        if (work.EntityId is { } entityId) {
            // An existing Entity's exact identity and mapped boundary are the request store's to derive.
            var target = await requests.RequireTargetAsync(connectionId, entityId, scope.LibraryRootId!.Value,
                work.TargetEntityIds, scope.Rendition, token);
            scopeWork = target.Work;
            targetEntityIds = target.Targets?.Select(item => item.EntityId).ToArray();
            storeMount = target.Mount;
        } else {
            scopeWork = policy.RequiresRendition ? work.Work with { BookRendition = scope.Rendition } : work.Work;
        }

        var lookup = await gateway.LookupAsync(authorized.Manifest.Id, authorized.Context, scopeWork, token);
        ManagedCreationEvidence.ValidateLookup(scopeWork, lookup);
        if (work.Snapshot is { } snapshot) {
            RequireLookupOfSnapshot(scopeWork, snapshot, lookup);
        }

        var options = await library.OptionsAsync(connectionId, new(work.Kind, scope.Rendition), token);
        var mount = storeMount ?? SelectMount(mounts, scope.LibraryRootId, lookup.Existing?.Path ?? work.Snapshot?.Path,
            requireContaining: work.Snapshot is not null);
        if (!options.Roots.Any(root => root.Id == mount.RemoteRootId && root.Path == mount.RemotePath && root.Accessible != false)) {
            throw new ArgumentException(
                "The mapped external root changed or became inaccessible. Review its connection before requesting this work.");
        }

        var existingFulfillments = await fulfillmentOwnership.ListAsync(scopeWork, token);
        var expansion = policy.AccumulatesTargets
            ? await ResolveExpansionAsync(connectionId, mount.LibraryRootId, scopeWork, lookup.Existing, existingFulfillments, token)
            : null;
        var retained = (await requests.ListAsync(connectionId, token))
            .Where(request => request.Operation.Phase.HoldsFulfillment
                && ManagedRequestIdentity.SameWork(request.Plan.Request.ReviewedWork, scopeWork))
            .ToArray();
        if (retained.Length > 1) {
            throw new ManagedRequestConflictException("More than one active request matches this work. Review request activity.");
        }

        return new(
            scope.Rendition,
            scopeWork,
            mount,
            options,
            lookup.Existing,
            existingFulfillments,
            expansion,
            retained.Length == 1 ? ManagedRequestService.Map(retained[0]) : null,
            targetEntityIds);
    }

    /// <summary>A connected source's lookup must resolve the very holding and targets the snapshot shows.</summary>
    private static void RequireLookupOfSnapshot(ManagedLookupInput work, ManagedItemSnapshot snapshot, ManagedLookupResult lookup) {
        var expectedTargets = work.Targets?.Count ?? 0;
        if (lookup.Existing?.Item.RemoteId != snapshot.Item.RemoteId
            || (lookup.Targets?.Count ?? 0) != expectedTargets) {
            throw new IntegrationInvocationException("The manager lookup no longer resolves this exact holding and target.");
        }

        ManagedCreationEvidence.ValidateComicTargets(work, snapshot, lookup.Targets);
    }

    private async Task<ManagedRequestExpansion?> ResolveExpansionAsync(
        Guid connectionId,
        Guid libraryRootId,
        ManagedLookupInput work,
        ManagedItemSnapshot? existing,
        IReadOnlyList<ReviewedFulfillmentOwnership> existingFulfillments,
        CancellationToken token) {
        var holdings = (await tracking.ListAsync(connectionId, token))
            .Where(holding => holding.Item.EntityKind == work.EntityKind
                && holding.Status != ManagedTrackingStatus.Released
                && work.ExternalIds.All(pair =>
                    holding.Item.ExpectedExternalIds.GetValueOrDefault(pair.Key) == pair.Value))
            .ToArray();
        if (holdings.Length != 1) {
            return null;
        }

        var holding = holdings[0];
        if (!ManagedTrackingStatusDefinition.For(holding.Status).IsEstablished
            || holding.LibraryRootId != libraryRootId
            || existing is null || existing.Item.RemoteId != holding.Item.RemoteId) {
            return null;
        }

        var ownerRequest = await requests.FindAsync(holding.Id, token);
        if (ownerRequest is not null && (ownerRequest.Operation.State.ReviewRequired
            || !ownerRequest.Operation.Phase.ProvidesHolding
            || ownerRequest.Plan.ExistingHoldingId is not null)) {
            return null;
        }

        if (existingFulfillments.Any(owner => owner.OwnerKind != FulfillmentOwnerKind.ExternalManager
                || owner.ConnectionId != connectionId || owner.RequestId != holding.Id)) {
            return null;
        }

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

    /// <summary>
    /// The mapped library a scope uses: the one named explicitly, else the deepest mapping containing the
    /// holding's remote path, else the first mapping. A connected holding's files are read in place, so its
    /// path must lie inside a mapping.
    /// </summary>
    private static ExternalLibraryMount SelectMount(
        IReadOnlyList<ExternalLibraryMount> mounts,
        Guid? requestedRootId,
        string? containingPath,
        bool requireContaining) {
        if (requestedRootId is { } explicitRoot) {
            return mounts.SingleOrDefault(mount => mount.LibraryRootId == explicitRoot)
                ?? throw new ArgumentException("Choose an enabled library mapped to an accessible root on this connection.");
        }

        if (containingPath is not null) {
            var containing = mounts
                .Where(mount => RemoteLibraryPath.Parse(mount.RemotePath).IsAncestorOf(containingPath))
                .OrderByDescending(mount => mount.RemotePath.Length)
                .ToArray();
            if (containing.Length > 1 && containing[0].RemotePath.Length == containing[1].RemotePath.Length) {
                throw new ArgumentException("Map this holding to one enabled local library before requesting it.");
            }

            if (containing.Length > 0) {
                return containing[0];
            }

            if (requireContaining) {
                throw new ArgumentException("Map the connected holding's folder to an enabled local library before requesting it.");
            }
        }

        return mounts.FirstOrDefault()
            ?? throw new ArgumentException("Map an accessible external library in Settings before requesting this work.");
    }

    #endregion

    #region Actions - Sources

    /// <summary>Resolves the work a request names from its one source, along with how to materialize it.</summary>
    private async Task<RequestedWork> ResolveWorkAsync(Guid connectionId, ReviewManagedRequestInput input, CancellationToken token) {
        if (input.EntityId is { } entityId) {
            if (input.Scopes.Any(scope => scope.LibraryRootId is null || scope.LibraryRootId == Guid.Empty)) {
                throw new ArgumentException("Choose a mapped library for every requested scope of an existing work.");
            }

            var first = input.Scopes[0];
            var target = await requests.RequireTargetAsync(connectionId, entityId, first.LibraryRootId!.Value,
                input.TargetEntityIds, first.Rendition, token);
            var targetIds = target.Targets?.Select(item => item.EntityId).ToArray();
            return new(target.Work.EntityKind, target.Title, target.Work with { BookRendition = null },
                Targets: null, Snapshot: null, entityId, targetIds, Request: null, ManagerDiscoveryRevision: null,
                _ => Task.FromResult(new ManagedWantedWork(entityId, targetIds, HasEveryFile: false)));
        }

        if (input.Connected is { } connected) {
            var snapshot = await library.GetAsync(connectionId, connected.Item, token);
            var preparer = connectedPreparers.SingleOrDefault(candidate => candidate.Kind == connected.Item.EntityKind)
                ?? throw new ArgumentException("Targets inside this kind of connected holding cannot be requested.");
            var reviewed = preparer.Review(snapshot, connected);
            return new(reviewed.Work.EntityKind, reviewed.Title, reviewed.Work, reviewed.Targets, snapshot,
                EntityId: null, TargetEntityIds: null, Request: null, ManagerDiscoveryRevision: null,
                ct => preparer.PrepareAsync(reviewed, ct));
        }

        var canonical = input.Request!;
        if (input.ManagerDiscoveryRevision is { } discoveryRevision) {
            (_, canonical) = await discovery.CanonicalizeAsync(connectionId, discoveryRevision, canonical, token);
        }

        var managerOrigin = input.ManagerDiscoveryRevision is not null;
        var wantedPreparer = wantedPreparers.SingleOrDefault(candidate => candidate.Kinds.Contains(canonical.Kind))
            ?? throw new RequestCommitValidationException("This kind of reviewed request cannot be fulfilled by a connected manager.");
        var plan = await wantedPreparer.ReviewForManagerAsync(canonical, managerOrigin, token);
        var request = canonical;
        return new(plan.Work.EntityKind, plan.Title, plan.Work, Targets: null, Snapshot: null,
            EntityId: null, TargetEntityIds: null, request, input.ManagerDiscoveryRevision,
            ct => wantedPreparer.PrepareForManagerAsync(request, managerOrigin, ct));
    }

    private static void ValidateSource(Guid? entityId, ReviewedRequestCommitRequest? request, ManagedConnectedTargetInput? connected) {
        var sources = (entityId is { } id && id != Guid.Empty ? 1 : 0) + (request is null ? 0 : 1) + (connected is null ? 0 : 1);
        if (sources != 1) {
            throw new RequestCommitValidationException("Submit a complete reviewed request.");
        }

        if (request is { SelectedProposalIds: null }) {
            throw new RequestCommitValidationException("Submit the reviewed proposal selection.");
        }

        if (connected is not null && !ManagedLibraryService.IsValidInput(connected.Item)) {
            throw new ArgumentException("Select an existing holding with its current identities.");
        }
    }

    /// <summary>Each requested scope must match the kind's rules: one scope per rendition, or one whole-work scope.</summary>
    internal static void RequireScopes(ManagedFulfillmentPolicy policy, IReadOnlyList<ManagedRequestScopeChoice> scopes, bool requireLibrary) {
        if (policy.RequiresRendition) {
            if (scopes.Count > policy.RenditionTargets.Count
                || scopes.Any(scope => scope.Rendition is null || !policy.AcceptsRendition(scope.Rendition))
                || scopes.Select(scope => scope.Rendition).Distinct().Count() != scopes.Count) {
                throw new ArgumentException("Choose each requested rendition once.");
            }
        } else if (scopes.Count != 1 || scopes[0].Rendition is not null) {
            throw new ArgumentException("This work is requested as a whole, in one scope.");
        }

        if (requireLibrary && scopes.Any(scope => scope.LibraryRootId is null || scope.LibraryRootId == Guid.Empty)) {
            throw new ArgumentException("Choose a mapped library for every requested scope.");
        }
    }

    #endregion

    #region Actions - Commit

    /// <summary>
    /// Commits the reviewed work and durable fulfillment ownership. One scope commits atomically with the
    /// work; several scopes share one materialized work and are accepted independently, so a scope that
    /// fails names itself and can be retried under the same operation while the others stay accepted.
    /// </summary>
    public async Task<ReviewedManagedRequestCommitResponse> CommitAsync(
        Guid connectionId,
        CommitReviewedManagedRequestInput input,
        CancellationToken token) {
        ValidateCommitInput(input);
        var fingerprint = ReviewedManagedRequestIdentity.Fingerprint(connectionId, input);
        var results = new ManagedRequestScopeResult?[input.Scopes.Count];
        Guid? entityId = null;
        for (var index = 0; index < input.Scopes.Count; index++) {
            var scope = input.Scopes[index];
            if (await managedRequests.FindReviewedAsync(connectionId, ScopeOperationId(input.OperationId, scope.Rendition),
                    fingerprint, token) is { } replay) {
                results[index] = new(scope.Rendition, replay.TargetEntityIds, replay);
                entityId ??= replay.EntityId;
            }
        }

        if (results.All(result => result is not null)) {
            return new(entityId!.Value, results!);
        }

        var (review, work) = await ReviewCoreAsync(connectionId,
            new(input.Scopes, input.EntityId, input.TargetEntityIds, input.Request, input.ManagerDiscoveryRevision, input.Connected),
            token);
        if (review.ConnectionRevision != input.ExpectedConnectionRevision) {
            throw new ConnectionConflictException("The selected manager connection changed. Review its options again.");
        }

        entityId ??= work.EntityId;
        var policy = ManagedFulfillmentPolicy.For(work.Kind);
        for (var index = 0; index < input.Scopes.Count; index++) {
            if (results[index] is not null) {
                continue;
            }

            var scope = input.Scopes[index];
            var reviewed = review.Scopes[index];
            policy.RequireRequest(input.ProfileId, scope.Rendition, reviewed.Work.Targets?.Count ?? 0,
                (reviewed.Work.Targets ?? []).Select(target => target.IssueLabel).ToArray(), input.Monitored, input.Search);
            if (ExistingSourceResult(scope, reviewed, policy) is { } owned) {
                results[index] = owned.Result;
                entityId ??= owned.EntityId;
                continue;
            }

            if (reviewed.ExistingRequest is { } retained) {
                if (retained.LibraryRootId != scope.LibraryRootId) {
                    throw new ManagedRequestConflictException("This work already has a manager request in another mapped library.");
                }

                results[index] = new(scope.Rendition, retained.TargetEntityIds, retained);
                entityId ??= retained.EntityId;
                continue;
            }

            if (reviewed.Expansion is null && reviewed.ExistingFulfillments.Count != 0) {
                throw new FulfillmentOwnershipConflictException();
            }

            await managedRequests.RequireAcceptanceCapabilitiesAsync(connectionId, work.Kind, policy, reviewed.Existing, input.Search, token);
        }

        var pending = Enumerable.Range(0, input.Scopes.Count).Where(index => results[index] is null).ToArray();
        if (pending.Length == 0) {
            return new(entityId!.Value, results!);
        }

        if (input.Scopes.Count == 1) {
            return await commitScope.ExecuteAsync(connectionId, input.ExpectedConnectionRevision, async ct => {
                var prepared = await work.Prepare(ct);
                var result = await AcceptScopeAsync(connectionId, input, input.Scopes[0], review.Scopes[0], prepared, fingerprint, ct);
                return new ReviewedManagedRequestCommitResponse(prepared.EntityId, [result]);
            }, token);
        }

        var materialized = await commitScope.ExecuteAsync(connectionId, input.ExpectedConnectionRevision, ct => work.Prepare(ct), token);
        foreach (var index in pending) {
            var scope = input.Scopes[index];
            try {
                results[index] = await commitScope.ExecuteAsync(connectionId, input.ExpectedConnectionRevision,
                    ct => AcceptScopeAsync(connectionId, input, scope, review.Scopes[index], materialized, fingerprint, ct), token);
            } catch (OperationCanceledException) when (token.IsCancellationRequested) {
                throw;
            } catch (Exception error) {
                logger.LogWarning(error, "Connected request scope {Rendition} of {EntityId} was not accepted",
                    scope.Rendition, materialized.EntityId);
                results[index] = new(scope.Rendition, null, null,
                    "This format was not accepted. Review its manager and mapped library, then retry this request.");
            }
        }

        return new(materialized.EntityId, results!);
    }

    private async Task<ManagedRequestScopeResult> AcceptScopeAsync(
        Guid connectionId,
        CommitReviewedManagedRequestInput input,
        ManagedRequestScopeChoice scope,
        ReviewedManagedRequestScope reviewed,
        ManagedWantedWork prepared,
        string fingerprint,
        CancellationToken token) {
        var targetEntityIds = reviewed.TargetEntityIds ?? prepared.MissingTargetEntityIds;
        if (reviewed.Expansion is { } expansion && targetEntityIds is not null) {
            var retained = expansion.RetainedTargetEntityIds.ToHashSet();
            targetEntityIds = targetEntityIds.Where(id => !retained.Contains(id)).ToArray();
        }

        if (prepared.HasEveryFile || targetEntityIds is { Count: 0 }) {
            return new(scope.Rendition, targetEntityIds, ManagedRequest: null);
        }

        var target = await requests.RequireTargetAsync(connectionId, prepared.EntityId, scope.LibraryRootId!.Value,
            targetEntityIds, scope.Rendition, token);
        if (!ManagedRequestIdentity.SameWork(target.Work with { Targets = null }, reviewed.Work with { Targets = null })) {
            throw new ManagedRequestConflictException("The wanted identity changed during review. Review the request again.");
        }

        var create = new CreateManagedRequestInput(
            ScopeOperationId(input.OperationId, scope.Rendition),
            prepared.EntityId,
            scope.LibraryRootId.Value,
            target.Work,
            input.ProfileId,
            input.Monitored,
            input.Search,
            targetEntityIds);
        ManagedRequestService.Validate(create);
        var preview = new ManagedRequestPreview(target.EntityId, target.Title, target.Work, target.Mount,
            reviewed.Options, reviewed.Existing, targetEntityIds);
        var accepted = await managedRequests.AcceptAsync(connectionId, create, preview, fingerprint,
            input.ExpectedConnectionRevision, reviewed.Expansion?.HoldingId, token);
        return new(scope.Rendition, targetEntityIds, accepted);
    }

    /// <summary>
    /// A work requested as a whole is already satisfied by any local source; a work that accumulates targets is
    /// satisfied only when every reviewed target already has one.
    /// </summary>
    private static (Guid EntityId, ManagedRequestScopeResult Result)? ExistingSourceResult(
        ManagedRequestScopeChoice scope,
        ReviewedManagedRequestScope reviewed,
        ManagedFulfillmentPolicy policy) {
        var locallyOwned = reviewed.ExistingFulfillments
            .Where(ownership => ownership.HasLocalSource)
            .ToArray();
        if (!policy.AccumulatesTargets) {
            return locallyOwned.Length != 0
                ? (locallyOwned[0].EntityId, new(scope.Rendition, TargetEntityIds: null, ManagedRequest: null))
                : null;
        }

        if (reviewed.Work.Targets is null) {
            return null;
        }

        var targetIds = locallyOwned
            .SelectMany(ownership => ownership.TargetEntityIds ?? [])
            .Distinct()
            .ToArray();
        return targetIds.Length == reviewed.Work.Targets.Count
            ? (locallyOwned[0].EntityId, new(scope.Rendition, targetIds, ManagedRequest: null))
            : null;
    }

    /// <summary>Each rendition scope keeps its own stable operation identity beneath the request's.</summary>
    internal static Guid ScopeOperationId(Guid operationId, BookRendition? rendition) {
        if (rendition is not { } chosen) {
            return operationId;
        }

        Span<byte> seed = stackalloc byte[17];
        operationId.TryWriteBytes(seed);
        seed[16] = checked((byte)chosen);
        return new Guid(SHA256.HashData(seed).AsSpan(0, 16));
    }

    internal static void ValidateCommitInput(CommitReviewedManagedRequestInput? input) {
        if (input is null) {
            throw new RequestCommitValidationException("Submit a complete reviewed request.");
        }

        ValidateSource(input.EntityId, input.Request, input.Connected);
        if (input.OperationId == Guid.Empty) {
            throw new ArgumentException("Supply a stable operation ID for this manager request.");
        }

        if (input.Scopes is not { Count: > 0 }
            || input.Scopes.Any(scope => scope.LibraryRootId is null || scope.LibraryRootId == Guid.Empty)) {
            throw new ArgumentException("Choose a mapped library for every requested scope.");
        }

        if (input.ProfileId is { Length: > MaximumProfileIdLength }) {
            throw new ArgumentException("Choose an existing external profile.");
        }
    }

    #endregion

    /// <summary>The work one request names, resolved from its source, and how to materialize it locally.</summary>
    /// <param name="Kind">Entity kind of the work.</param>
    /// <param name="Title">Display title.</param>
    /// <param name="Work">Exact manager lookup identity of the whole work, without a rendition.</param>
    /// <param name="Targets">Exact targets a connected source names.</param>
    /// <param name="Snapshot">The connected holding's snapshot, for connected sources.</param>
    /// <param name="EntityId">The existing local Entity, for existing sources.</param>
    /// <param name="TargetEntityIds">Finite child scope of an existing container.</param>
    /// <param name="Request">The canonical metadata selection, for reviewed sources.</param>
    /// <param name="ManagerDiscoveryRevision">Discovery revision the selection came from.</param>
    /// <param name="Prepare">Materializes the local work and reports what still needs files.</param>
    private sealed record RequestedWork(
        EntityKind Kind,
        string Title,
        ManagedLookupInput Work,
        IReadOnlyList<ReviewedManagedTarget>? Targets,
        ManagedItemSnapshot? Snapshot,
        Guid? EntityId,
        IReadOnlyList<Guid>? TargetEntityIds,
        ReviewedRequestCommitRequest? Request,
        long? ManagerDiscoveryRevision,
        Func<CancellationToken, Task<ManagedWantedWork>> Prepare);
}
