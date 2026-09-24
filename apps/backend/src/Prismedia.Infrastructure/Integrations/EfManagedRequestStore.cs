using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Prismedia.Application.Entities;
using Prismedia.Application.Integrations;
using Prismedia.Application.Jobs;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Commits request ownership, creation fences, and exact source bindings using the same lifecycle and queue
/// boundaries as library tracking.</summary>
public sealed partial class EfManagedRequestStore(PrismediaDbContext db, IExternalLibraryMountStore mounts,
    IJobQueueService queue, IEntityLifecycleMutationLease lifecycle) : IManagedRequestStore {
    #region Static Variables

    private static readonly JsonSerializerOptions Json = PluginProcessTransport.JsonOptions;

    #endregion

    #region Actions - Target Resolution

    /// <inheritdoc />
    public Task<ManagedRequestTarget> RequireTargetAsync(
        Guid connectionId,
        Guid entityId,
        Guid libraryRootId,
        IReadOnlyList<Guid>? targetEntityIds,
        BookRendition? bookRendition,
        CancellationToken token) =>
        RequireTargetCoreAsync(connectionId, entityId, libraryRootId, targetEntityIds, bookRendition, ownerId: null, token);

    private async Task<ManagedRequestTarget> RequireTargetCoreAsync(Guid connectionId, Guid entityId,
        Guid libraryRootId, IReadOnlyList<Guid>? targetEntityIds, BookRendition? bookRendition,
        Guid? ownerId, CancellationToken token) {
        var requestedTargetIds = targetEntityIds ?? [];
        if (requestedTargetIds.Any(id => id == Guid.Empty)
            || requestedTargetIds.Distinct().Count() != requestedTargetIds.Count) {
            throw new ArgumentException("Choose unique wanted targets.");
        }

        var entity = await db.Entities.AsNoTracking().SingleOrDefaultAsync(row => row.Id == entityId, token);
        if (entity is null) {
            throw new ArgumentException("Choose an existing wanted item.");
        }

        if (!entity.KindCode.TryDecodeAs<EntityKind>(out var kind) || !ManagedFulfillmentPolicy.Supports(kind)) {
            throw new ArgumentException("Choose a wanted work that a connected manager can fulfill.");
        }

        var policy = ManagedFulfillmentPolicy.For(kind);
        if (!policy.AcceptsRendition(bookRendition)) {
            throw new ArgumentException("Choose one exact rendition for this work.");
        }

        var mount = (await mounts.ListAsync(connectionId, token)).SingleOrDefault(item => item.LibraryRootId == libraryRootId)
            ?? throw new ArgumentException("Choose a library mapped to this connection.");
        var capability = EntityKindRegistry.Describe(kind).AcquisitionProfile?.LibraryRootMediaCapability;
        var root = await db.LibraryRoots.AsNoTracking().SingleOrDefaultAsync(row => row.Id == libraryRootId && row.Enabled, token);
        // Independently fulfilled renditions may live in different libraries, so only single-root works
        // must not already belong to another library.
        if (root is null || capability is not { } required || !root.Supports(required)
            || !policy.RequiresRendition && await db.EntityLibraryRoots.AnyAsync(association =>
                (association.EntityId == entityId || requestedTargetIds.Contains(association.EntityId))
                && association.LibraryRootId != libraryRootId, token)) {
            throw new ArgumentException("Enable the mapped library and resolve any previous library association first.");
        }

        if (policy.RequiresRendition) {
            return await RequireRenditionTargetAsync(entity, kind, policy, mount, connectionId, requestedTargetIds,
                bookRendition!.Value, ownerId, token);
        }

        var shape = policy.Target!.Shape;
        if (shape.IsItem) {
            return await RequireWholeWorkTargetAsync(entity, kind, policy, mount, requestedTargetIds, token);
        }

        if (shape.RequiresIssueLabel) {
            return await RequireIssueTargetAsync(entity, kind, policy, mount, connectionId, requestedTargetIds, ownerId, token);
        }

        if (!shape.RequiresEpisode) {
            throw new ArgumentException("This kind of work cannot be requested through a connected manager yet.");
        }

        return await RequireEpisodeTargetsAsync(entity, kind, policy, mount, connectionId, requestedTargetIds, ownerId, token);
    }

    private async Task<ManagedRequestTarget> RequireWholeWorkTargetAsync(EntityRow entity, EntityKind kind,
        ManagedFulfillmentPolicy policy, ExternalLibraryMount mount, IReadOnlyList<Guid> requestedTargetIds, CancellationToken token) {
        if (requestedTargetIds.Count != 0 || !entity.IsWanted || await HasSourceAsync([entity.Id], token)) {
            throw new ArgumentException(
                "Choose a wanted work without a retained source. Existing files can be linked through connected-library tracking.");
        }

        var identities = await db.EntityExternalIds.AsNoTracking()
            .Where(row => row.EntityId == entity.Id && policy.IdentityProviders.Contains(row.Provider))
            .ToArrayAsync(token);
        foreach (var provider in policy.IdentityProviders) {
            var values = identities.Where(row => row.Provider == provider).Select(row => row.Value).Distinct().ToArray();
            if (values.Length == 1 && !string.IsNullOrWhiteSpace(values[0])) {
                return new(entity.Id, entity.Title, new(kind, new Dictionary<string, string> { [provider] = values[0] }), mount);
            }
        }

        throw new ArgumentException($"Identify this wanted work with {policy.IdentityDescription} first.");
    }

    private async Task<ManagedRequestTarget> RequireEpisodeTargetsAsync(EntityRow entity, EntityKind kind,
        ManagedFulfillmentPolicy policy, ExternalLibraryMount mount, Guid connectionId, IReadOnlyList<Guid> requestedTargetIds,
        Guid? ownerId, CancellationToken token) {
        var entityId = entity.Id;
        if (requestedTargetIds.Count == 0) {
            throw new ArgumentException("Select at least one finite wanted episode.");
        }

        var episodeCode = policy.Target!.Kind.ToCode();
        var episodes = await db.Entities.AsNoTracking()
            .Where(row => requestedTargetIds.Contains(row.Id) && row.KindCode == episodeCode)
            .ToDictionaryAsync(row => row.Id, token);
        var ownedTargets = ownerId is { } acceptedOwner
            ? await db.FulfillmentReservations.AsNoTracking()
                .Where(row => row.OwnerId == acceptedOwner
                    && row.OwnerKind == FulfillmentOwnerKind.ExternalManager
                    && row.ConnectionId == connectionId
                    && row.ReleasedAt == null
                    && requestedTargetIds.Contains(row.EntityId))
                .Select(row => row.EntityId)
                .ToHashSetAsync(token)
            : [];
        var sourceTargetIds = await db.EntityFiles.AsNoTracking()
            .Where(file => requestedTargetIds.Contains(file.EntityId)
                && (file.Role == EntityFileRole.Source || file.Role == EntityFileRole.UnavailableSource))
            .Select(file => file.EntityId)
            .Distinct()
            .ToArrayAsync(token);
        if (episodes.Count != requestedTargetIds.Count
            || episodes.Values.Any(episode =>
                (!episode.IsWanted || sourceTargetIds.Contains(episode.Id))
                && !ownedTargets.Contains(episode.Id))) {
            throw new ArgumentException("Every selected target must be a wanted episode without a retained source.");
        }

        var parentIds = episodes.Values.Select(episode => episode.ParentEntityId).OfType<Guid>().Distinct().ToArray();
        var parents = await db.Entities.AsNoTracking()
            .Where(row => parentIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, token);
        if (episodes.Values.Any(episode => episode.ParentEntityId is not { } parentId
                || !parents.TryGetValue(parentId, out var parent)
                || parent.Id != entityId && parent.ParentEntityId != entityId)) {
            throw new ArgumentException("Every selected episode must belong to the reviewed series.");
        }

        var identityEntityIds = requestedTargetIds.Append(entityId).Distinct().ToArray();
        var identityRows = await db.EntityExternalIds.AsNoTracking()
            .Where(row => identityEntityIds.Contains(row.EntityId))
            .ToArrayAsync(token);
        var identities = identityRows
            .GroupBy(row => row.EntityId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<string, string>)group
                    .GroupBy(row => row.Provider.Trim().ToLowerInvariant(), StringComparer.Ordinal)
                    .Where(provider => provider.Select(row => row.Value.Trim()).Distinct(StringComparer.Ordinal).Count() == 1)
                    .ToDictionary(provider => provider.Key, provider => provider.First().Value.Trim(), StringComparer.Ordinal));
        if (!identities.TryGetValue(entityId, out var allSeriesIdentities)) {
            throw new ArgumentException("Identify the series before choosing external fulfillment.");
        }

        var seriesIdentities = allSeriesIdentities
            .Where(pair => policy.IdentityProviders.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (seriesIdentities.Count == 0) {
            throw new ArgumentException($"Identify the series with {policy.IdentityDescription} before choosing external fulfillment.");
        }

        var positionEntityIds = requestedTargetIds.Concat(parentIds).Distinct().ToArray();
        var positions = (await db.EntityPositions.AsNoTracking()
                .Where(row => positionEntityIds.Contains(row.EntityId))
                .ToArrayAsync(token))
            .GroupBy(row => row.EntityId)
            .ToDictionary(group => group.Key,
                group => group.ToDictionary(row => row.Code, row => row.Value, StringComparer.Ordinal));
        var targets = new List<ManagedRequestEntityTarget>(requestedTargetIds.Count);
        foreach (var targetId in requestedTargetIds) {
            var episode = episodes[targetId];
            var episodePositions = positions.GetValueOrDefault(targetId)
                ?? new Dictionary<string, int>();
            var parentPositions = positions.GetValueOrDefault(episode.ParentEntityId!.Value)
                ?? new Dictionary<string, int>();
            var seasonNumber = episodePositions.GetValueOrDefault(
                EntityPositionCodes.Season,
                parentPositions.GetValueOrDefault(EntityPositionCodes.Season, -1));
            var episodeNumber = episodePositions.GetValueOrDefault(EntityPositionCodes.Episode, -1);
            var absoluteNumber = episodePositions.GetValueOrDefault(EntityPositionCodes.AbsoluteEpisode, -1);
            if (seasonNumber < 0 || episodeNumber <= 0) {
                throw new ArgumentException("Every selected episode needs exact season and episode coordinates.");
            }

            targets.Add(new(targetId, new(
                policy.Target.Kind,
                identities.GetValueOrDefault(targetId)?
                    .Where(pair => policy.TargetIdentityProviders.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                    ?? new Dictionary<string, string>(),
                seasonNumber,
                episodeNumber,
                absoluteNumber > 0 ? absoluteNumber : null)));
        }

        if (targets.Select(target => (target.Target.SeasonNumber, target.Target.EpisodeNumber)).Distinct().Count()
            != targets.Count) {
            throw new ArgumentException("Selected episodes must have unique season and episode coordinates.");
        }

        return new(entityId, entity.Title,
            new(kind, seriesIdentities, targets.Select(target => target.Target).ToArray()),
            mount,
            targets);
    }

    private async Task<ManagedRequestTarget> RequireRenditionTargetAsync(EntityRow book, EntityKind kind,
        ManagedFulfillmentPolicy policy, ExternalLibraryMount mount, Guid connectionId, IReadOnlyList<Guid> requestedTargetIds,
        BookRendition rendition, Guid? ownerId, CancellationToken token) {
        if (requestedTargetIds.Count != 0) {
            throw new ArgumentException("Request one rendition through its work, without child targets.");
        }

        var ownReservation = ownerId is { } owner && await db.FulfillmentReservations.AsNoTracking().AnyAsync(row =>
            row.OwnerId == owner && row.OwnerKind == FulfillmentOwnerKind.ExternalManager
            && row.ConnectionId == connectionId && row.EntityId == book.Id
            && row.BookRendition == rendition && row.ReleasedAt == null, token);
        // A rendition delivered as the work itself owns the work's source; one delivered in parts owns its child parts.
        var target = policy.TargetFor(rendition);
        var partCode = target.Kind.ToCode();
        var sourceIds = target.Shape.IsItem ? new[] { book.Id }
            : await db.Entities.AsNoTracking().Where(row => row.ParentEntityId == book.Id
                && row.KindCode == partCode).Select(row => row.Id).ToArrayAsync(token);
        if (await HasSourceAsync(sourceIds, token) && !ownReservation) {
            throw new ArgumentException("This rendition already has a retained source. Link it through connected-library tracking.");
        }

        var provider = policy.IdentityProviders[0];
        var workIds = await db.EntityExternalIds.AsNoTracking().Where(row => row.EntityId == book.Id
            && row.Provider == provider).Select(row => row.Value).Distinct().ToArrayAsync(token);
        if (workIds.Length != 1 || string.IsNullOrWhiteSpace(workIds[0])) {
            throw new ArgumentException($"Identify this work with {policy.IdentityDescription} before manager fulfillment.");
        }

        return new(book.Id, book.Title, new(kind,
            new Dictionary<string, string> { [provider] = workIds[0] },
            BookRendition: rendition), mount);
    }

    private async Task<ManagedRequestTarget> RequireIssueTargetAsync(EntityRow series, EntityKind kind,
        ManagedFulfillmentPolicy policy, ExternalLibraryMount mount, Guid connectionId, IReadOnlyList<Guid> requestedTargetIds,
        Guid? ownerId, CancellationToken token) {
        if (requestedTargetIds.Count != 1) {
            throw new ArgumentException("Select one exact wanted comic issue.");
        }

        var issueId = requestedTargetIds[0];
        var issueCode = policy.Target!.Kind.ToCode();
        var issue = await db.Entities.AsNoTracking().SingleOrDefaultAsync(row => row.Id == issueId
            && row.KindCode == issueCode, token);
        if (issue is null) {
            throw new ArgumentException("Choose a wanted comic installment.");
        }

        var parent = issue.ParentEntityId is { } parentId
            ? await db.Entities.AsNoTracking().SingleOrDefaultAsync(row => row.Id == parentId, token)
            : null;
        if (parent is null || parent.Id != series.Id && parent.ParentEntityId != series.Id) {
            throw new ArgumentException("The selected issue does not belong to this comic series.");
        }

        var ownsIssue = ownerId is { } owner && await db.FulfillmentReservations.AsNoTracking().AnyAsync(row =>
            row.OwnerId == owner && row.OwnerKind == FulfillmentOwnerKind.ExternalManager
            && row.ConnectionId == connectionId && row.EntityId == issueId && row.ReleasedAt == null, token);
        if ((!issue.IsWanted || await HasSourceAsync([issueId], token)) && !ownsIssue) {
            throw new ArgumentException("Choose a wanted comic issue without a retained source.");
        }

        var seriesProvider = policy.IdentityProviders[0];
        var issueProvider = policy.TargetIdentityProviders[0];
        var identities = await db.EntityExternalIds.AsNoTracking()
            .Where(row => row.EntityId == series.Id && row.Provider == seriesProvider
                || row.EntityId == issueId && row.Provider == issueProvider)
            .ToArrayAsync(token);
        var seriesIdentity = identities.Where(row => row.EntityId == series.Id).Select(row => row.Value).Distinct().ToArray();
        var issueIdentity = identities.Where(row => row.EntityId == issueId).Select(row => row.Value).Distinct().ToArray();
        if (seriesIdentity.Length != 1 || issueIdentity.Length != 1
            || !policy.IdentityFormats[0].IsCanonical(seriesIdentity[0])
            || !policy.TargetIdentityFormats[0].IsCanonical(issueIdentity[0])) {
            throw new ArgumentException($"Identify the series and selected issue with {policy.IdentityDescription} first.");
        }

        var positions = await db.EntityPositions.AsNoTracking()
            .Where(row => row.EntityId == issueId && row.Code == EntityPositionCodes.Chapter)
            .ToArrayAsync(token);
        var issueLabel = positions.Length == 1 ? positions[0].Label : null;
        if (string.IsNullOrWhiteSpace(issueLabel) || issueLabel.Length > 128) {
            throw new ArgumentException("The selected issue needs one exact issue label.");
        }

        var target = new ManagedRequestEntityTarget(issueId,
            new(policy.Target.Kind,
                new Dictionary<string, string> { [issueProvider] = issueIdentity[0] },
                IssueLabel: issueLabel));
        return new(series.Id, series.Title,
            new(kind,
                new Dictionary<string, string> { [seriesProvider] = seriesIdentity[0] },
                [target.Target]), mount, [target]);
    }

    private Task<bool> HasSourceAsync(IReadOnlyCollection<Guid> entityIds, CancellationToken token) =>
        db.EntityFiles.AnyAsync(file => entityIds.Contains(file.EntityId)
            && (file.Role == EntityFileRole.Source || file.Role == EntityFileRole.UnavailableSource), token);

    #endregion

    #region Actions - Queries

    /// <inheritdoc />
    public async Task<StoredManagedRequest?> FindAsync(Guid id, CancellationToken token) =>
        await db.ManagedRequests.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token) is { } row ? Map(row) : null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredManagedRequest>> ListAsync(Guid connectionId, CancellationToken token) =>
        (await db.ManagedRequests.AsNoTracking().Where(row => row.ConnectionId == connectionId).OrderByDescending(row => row.CreatedAt)
            .Take(100).ToArrayAsync(token)).Select(Map).ToArray();

    #endregion

    #region Actions - Persistence

    /// <inheritdoc />
    public async Task<StoredManagedRequest> CreateAsync(ManagedRequestOperation operation, ManagedRequestPlan plan,
        CancellationToken token) {
        if (await FindAsync(operation.State.OperationId, token) is { } existing) {
            return Same(existing, operation, plan);
        }

        var state = operation.State;
        if (state.Revision != 1 || state.Phase != ManagedRequestPhase.PendingCreation || plan.Request.OperationId != state.OperationId
            || plan.Request.EntityId != state.EntityId || plan.Request.LibraryRootId != state.LibraryRootId
            || plan.Creation.OperationId != state.OperationId
            || plan.Creation.ProfileId != plan.Request.ProfileId
            || !ManagedRequestIdentity.SameWork(plan.Creation.Work, plan.Request.ReviewedWork)
            || plan.Fingerprint != ManagedRequestIdentity.Fingerprint(plan.Request)) {
            throw new ArgumentException("Invalid managed request intent.");
        }

        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(token)
            : null;
        await PluginLifecycleLease.LockConnectionAsync(db, state.ConnectionId, token, requireReady: true);
        if (plan.ExpectedConnectionRevision is { } expectedRevision
            && await db.IntegrationConnections
                .Where(connection => connection.Id == state.ConnectionId)
                .Select(connection => connection.Revision)
                .SingleAsync(token) != expectedRevision) {
            throw new ManagedRequestConflictException("The selected manager connection changed. Review its options again.");
        }

        var now = DateTimeOffset.UtcNow;
        var row = new ManagedRequestRow { Id = state.OperationId, ConnectionId = state.ConnectionId, EntityId = state.EntityId,
            LibraryRootId = state.LibraryRootId, Revision = state.Revision, Phase = state.Phase,
            StateJson = JsonSerializer.Serialize(state, Json),
            PlanJson = JsonSerializer.Serialize(plan, Json), CreatedAt = now, UpdatedAt = now, NextCheckAt = now.AddSeconds(30) };
        try {
            var targetIds = plan.Request.TargetEntityIds is { Count: > 0 }
                ? plan.Request.TargetEntityIds
                : [state.EntityId];
            if (!await lifecycle.ExecuteManyAsync(targetIds.Append(state.EntityId).ToArray(), async ct => {
                var boundary = await RequireBoundaryAsync(operation, plan, false, ct);
                if (await db.ManagedHoldings.AnyAsync(holding => holding.Id == row.Id, ct)
                    || await db.ManagedControls.AnyAsync(action => action.Id == row.Id, ct)) {
                    throw new ManagedRequestConflictException("This operation ID already belongs to another managed action.");
                }

                if (plan.ExistingHoldingId is { } existingHoldingId) {
                    var ownerRequest = (await db.ManagedRequests
                        .FromSqlInterpolated($"SELECT * FROM managed_requests WHERE id = {existingHoldingId} FOR UPDATE")
                        .AsNoTracking().ToArrayAsync(ct)).SingleOrDefault();
                    var holding = (await db.ManagedHoldings
                        .FromSqlInterpolated($"SELECT * FROM managed_holdings WHERE id = {existingHoldingId} FOR UPDATE")
                        .AsNoTracking().ToArrayAsync(ct)).SingleOrDefault();
                    var item = holding is null ? null : JsonSerializer.Deserialize<ManagedItemInput>(holding.ItemJson, Json);
                    var ownerState = ownerRequest is null
                        ? null
                        : JsonSerializer.Deserialize<ManagedRequestState>(ownerRequest.StateJson, Json);
                    if (ownerRequest is not null && (ownerState is null
                            || ownerRequest.ConnectionId != state.ConnectionId || ownerRequest.EntityId != state.EntityId
                            || ownerState.Revision != ownerRequest.Revision || ownerState.Phase != ownerRequest.Phase
                            || ownerState.ReviewRequired
                            || !ManagedRequestPhaseDefinition.For(ownerState.Phase).ProvidesHolding)
                        || holding is null || holding.ConnectionId != state.ConnectionId
                        || holding.LibraryRootId != state.LibraryRootId || holding.ReleasedAt is not null
                        || holding.ReleaseOperationId is not null
                        || !ManagedTrackingStatusDefinition.For(holding.Status).IsEstablished
                        || item is null || item.EntityKind != plan.Creation.Work.EntityKind
                        || plan.Creation.Work.ExternalIds.Any(pair => item.ExpectedExternalIds.GetValueOrDefault(pair.Key) != pair.Value)) {
                        throw new ManagedRequestConflictException("The reviewed holding is no longer available for this target.");
                    }

                    if (await db.ManagedControls.AsNoTracking()
                        .AnyAsync(action => action.ActiveHoldingId == existingHoldingId, ct)) {
                        throw new ManagedRequestConflictException(
                            "Finish the holding's current manager action before adding another target.");
                    }

                    var requestedIds = (plan.Request.TargetEntityIds ?? []).ToHashSet();
                    var retainedIds = JsonSerializer.Deserialize<ManagedTargetBinding[]>(holding.TargetsJson, Json)!
                        .Select(binding => binding.EntityId).ToHashSet();
                    if (requestedIds.Overlaps(retainedIds)) {
                        throw new ManagedRequestConflictException(
                            "The reviewed target changed because this holding already retained a selected target.");
                    }

                    var activeExpansions = await db.ManagedRequests.AsNoTracking()
                        .Where(request => request.ConnectionId == state.ConnectionId
                            && request.EntityId == state.EntityId
                            && request.Id != state.OperationId
                            && ManagedRequestPhaseDefinition.InFlight.Contains(request.Phase))
                        .ToArrayAsync(ct);
                    if (activeExpansions.Any(request => {
                        var accepted = JsonSerializer.Deserialize<ManagedRequestPlan>(request.PlanJson, Json);
                        return accepted?.ExistingHoldingId == existingHoldingId;
                    })) {
                        throw new ManagedRequestConflictException(
                            "Finish the holding's accepted target request before adding another.");
                    }
                } else if (ManagedFulfillmentPolicy.For(plan.Creation.Work.EntityKind).AccumulatesTargets) {
                    var workKind = plan.Creation.Work.EntityKind;
                    var active = await db.ManagedHoldings.AsNoTracking()
                        .Where(holding => holding.ConnectionId == state.ConnectionId
                            && holding.Kind == workKind
                            && holding.ReleasedAt == null)
                        .ToArrayAsync(ct);
                    if (active.Any(holding => {
                        var item = JsonSerializer.Deserialize<ManagedItemInput>(holding.ItemJson, Json);
                        return item is not null && plan.Creation.Work.ExternalIds.All(pair =>
                            item.ExpectedExternalIds.GetValueOrDefault(pair.Key) == pair.Value);
                    })) {
                        throw new ManagedRequestConflictException(
                            "This work already has active external-manager ownership. Adding targets to an existing holding requires a separate reviewed expansion.");
                    }
                }

                db.ManagedRequests.Add(row);
                var reservations = new EfFulfillmentReservationStore(db);
                foreach (var targetId in boundary.Targets is { Count: > 0 }
                    ? boundary.Targets.Select(target => target.EntityId)
                    : [row.EntityId]) {
                    if (plan.ExistingHoldingId is { } expansionOwner) {
                        await db.FulfillmentReservations
                            .Where(owner => owner.OwnerId == expansionOwner
                                && owner.OwnerKind == FulfillmentOwnerKind.ExternalManager
                                && owner.ConnectionId == row.ConnectionId
                                && owner.EntityId == targetId
                                && owner.ReleasedAt != null)
                            .ExecuteDeleteAsync(ct);
                    }

                    await reservations.ReserveAsync(
                        OwnerId(plan, state),
                        FulfillmentOwnerKind.ExternalManager,
                        row.ConnectionId,
                        targetId,
                        plan.Request.ReviewedWork.BookRendition,
                        ct);
                }

                var visibleEntityIds = targetIds.Append(row.EntityId).Distinct().ToArray();
                var rootedEntityIds = await db.EntityLibraryRoots
                    .Where(root => visibleEntityIds.Contains(root.EntityId))
                    .Select(root => root.EntityId)
                    .ToHashSetAsync(ct);
                foreach (var visibleEntityId in visibleEntityIds.Where(id => !rootedEntityIds.Contains(id))) {
                    db.EntityLibraryRoots.Add(new() {
                        EntityId = visibleEntityId,
                        LibraryRootId = row.LibraryRootId
                    });
                }

                await db.SaveChangesAsync(ct);
                await PublishAsync(row, ct);
            }, token)) {
                throw new EntityLifecycleMutationConflictException(state.EntityId);
            }

            if (transaction is not null) {
                await transaction.CommitAsync(token);
            }
        } catch (DbUpdateException error)
            when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) {
            if (transaction is null) {
                throw new ManagedRequestConflictException("This request conflicts with an existing managed operation.");
            }

            if (transaction is not null) {
                await transaction.RollbackAsync(token);
            }

            db.ChangeTracker.Clear();
            if (await FindAsync(row.Id, token) is { } accepted) {
                return Same(accepted, operation, plan);
            }

            throw new ManagedRequestConflictException("This request conflicts with an existing managed operation.");
        } catch (Exception error) when (FulfillmentOwnershipViolation.IsConflict(error)) {
            if (transaction is not null) {
                await transaction.RollbackAsync(token);
            }

            db.ChangeTracker.Clear();
            throw new FulfillmentOwnershipConflictException(error);
        }

        return Map(row);
    }

    /// <inheritdoc />
    public async Task SaveAsync(ManagedRequestOperation operation, long expectedRevision, string? problem, bool beforeDispatch,
        CancellationToken token) {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        if (!await lifecycle.ExecuteAsync(operation.State.EntityId, async ct => {
            var current = await LockAsync(operation.State.OperationId, expectedRevision, ct);
            if (beforeDispatch) {
                await RequireBoundaryAsync(operation, current.Plan, true, ct);
            }

            await UpdateAsync(operation, expectedRevision, problem, ct);
            if (operation.State.Phase == ManagedRequestPhase.Cancelled) {
                if (!current.Operation.CanCancel || current.Plan.ExistingHoldingId is null
                    && await db.ManagedHoldings.AnyAsync(holding => holding.Id == operation.State.OperationId, ct)) {
                    throw new ManagedRequestConflictException(
                        "This request may have remote effects and cannot release ownership by cancellation.");
                }

                var ownerId = OwnerId(current.Plan, operation.State);
                var appended = current.Plan.ExistingHoldingId is { } holdingId
                    ? JsonSerializer.Deserialize<ManagedTargetBinding[]>((await db.ManagedHoldings.AsNoTracking()
                        .Where(holding => holding.Id == holdingId).Select(holding => holding.TargetsJson).SingleAsync(ct)), Json)!
                        .Select(binding => binding.EntityId).ToHashSet()
                    : [];
                var releasable = (current.Plan.Request.TargetEntityIds ?? [operation.State.EntityId])
                    .Where(id => !appended.Contains(id)).ToArray();
                await db.FulfillmentReservations.Where(owner => owner.OwnerId == ownerId
                    && owner.OwnerKind == FulfillmentOwnerKind.ExternalManager && owner.ReleasedAt == null)
                    .Where(owner => releasable.Contains(owner.EntityId))
                    .ExecuteUpdateAsync(set => set.SetProperty(owner => owner.ReleasedAt, DateTimeOffset.UtcNow), ct);
            }
        }, token)) {
            throw new EntityLifecycleMutationConflictException(operation.State.EntityId);
        }

        await transaction.CommitAsync(token);
    }

    #endregion

    #region Actions - Queueing

    /// <inheritdoc />
    public async Task QueueAsync(Guid id, CancellationToken token) {
        var row = await db.ManagedRequests.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token)
            ?? throw new ManagedRequestConflictException("The request no longer exists.");
        if (Map(row).Operation.IsActive) {
            await PublishAsync(row, token);
        }
    }

    /// <inheritdoc />
    public async Task QueueDueAsync(CancellationToken token) {
        var now = DateTimeOffset.UtcNow;
        var retryReviewedAfter = now.AddMinutes(-1);
        var due = await db.ManagedRequests.AsNoTracking()
            .Where(row => row.NextCheckAt <= now
                || row.Phase == ManagedRequestPhase.AwaitingFiles
                    && row.NextCheckAt == null
                    && row.UpdatedAt <= retryReviewedAfter)
            .Join(db.IntegrationConnections.Where(connection => connection.Enabled), row => row.ConnectionId,
                connection => connection.Id, (row, _) => row)
            .OrderBy(row => row.NextCheckAt).Take(25).ToArrayAsync(token);
        foreach (var row in due) {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            if (await db.ManagedRequests.Where(item => item.Id == row.Id && item.Revision == row.Revision
                    && (item.NextCheckAt <= now
                        || item.Phase == ManagedRequestPhase.AwaitingFiles
                            && item.NextCheckAt == null
                            && item.UpdatedAt <= retryReviewedAfter))
                .ExecuteUpdateAsync(set => set.SetProperty(item => item.NextCheckAt, now.AddSeconds(30)), token) == 1) {
                await PublishAsync(row, token);
            }

            await transaction.CommitAsync(token);
        }
    }

    private async Task PublishAsync(ManagedRequestRow row, CancellationToken token) {
        await queue.DeclareResourceAsync(JobResourceKeys.LibraryScan, 1, TimeSpan.Zero, token);
        if (!await queue.HasPendingAsync(JobType.ManagedLibraryReconcile, row.Id.ToString(), token)) {
            await queue.EnqueueAsync(new EnqueueJobRequest(JobType.ManagedLibraryReconcile, TargetEntityKind: JobTargetKinds.ManagedHolding,
                TargetEntityId: row.Id.ToString(), TargetLabel: Map(row).Plan.Title, ResourceKey: JobResourceKeys.LibraryScan), token);
        }
    }

    #endregion

    #region Actions - Boundary

    private async Task<ManagedRequestTarget> RequireBoundaryAsync(ManagedRequestOperation operation, ManagedRequestPlan plan,
        bool requireOwner, CancellationToken token) {
        var state = operation.State;
        var target = await RequireTargetCoreAsync(
            state.ConnectionId,
            state.EntityId,
            state.LibraryRootId,
            plan.Request.TargetEntityIds,
            plan.Request.ReviewedWork.BookRendition,
            requireOwner ? OwnerId(plan, state) : null,
            token);
        if (!ManagedRequestIdentity.SameWork(target.Work, plan.Creation.Work) || target.Mount.RemoteRootId != plan.Creation.RootId
            || target.Mount.RemotePath != plan.Creation.ExpectedRootPath) {
            throw new ManagedRequestConflictException("The accepted wanted identity or library boundary changed.");
        }

        if (requireOwner) {
            var expectedOwners = target.Targets is { Count: > 0 }
                ? target.Targets.Select(item => item.EntityId).ToArray()
                : [state.EntityId];
            var owned = await db.FulfillmentReservations.AsNoTracking()
                .Where(owner => owner.OwnerId == OwnerId(plan, state)
                    && owner.OwnerKind == FulfillmentOwnerKind.ExternalManager
                    && owner.ConnectionId == state.ConnectionId
                    && owner.ReleasedAt == null
                    && owner.BookRendition == plan.Request.ReviewedWork.BookRendition
                    && expectedOwners.Contains(owner.EntityId))
                .Select(owner => owner.EntityId)
                .Distinct()
                .CountAsync(token);
            if (owned != expectedOwners.Length) {
                throw new ManagedRequestConflictException("The request no longer owns fulfillment for every wanted target.");
            }
        }

        return target;
    }

    private static Guid OwnerId(ManagedRequestPlan plan, ManagedRequestState state) =>
        plan.ExistingHoldingId ?? state.OperationId;

    #endregion

    #region Actions - Revision Fences

    private async Task<StoredManagedRequest> LockAsync(Guid id, long revision, CancellationToken token) {
        var row = (await db.ManagedRequests
            .FromSqlInterpolated($"SELECT * FROM managed_requests WHERE id = {id} FOR UPDATE")
            .AsNoTracking().ToArrayAsync(token)).SingleOrDefault();
        if (row is null || row.Revision != revision) {
            throw Conflict();
        }

        return Map(row);
    }

    private async Task UpdateAsync(ManagedRequestOperation operation, long revision, string? problem, CancellationToken token) {
        var state = operation.State;
        if (state.Revision != revision + 1) {
            throw Conflict();
        }

        var serialized = JsonSerializer.Serialize(state, Json);
        var now = DateTimeOffset.UtcNow;
        var next = operation.IsActive && !state.ReviewRequired ? now.AddSeconds(30) : (DateTimeOffset?)null;
        var safeProblem = problem is { Length: > 4096 } ? problem[..4096] : problem;
        if (await db.ManagedRequests.Where(row => row.Id == state.OperationId && row.Revision == revision
            && row.ConnectionId == state.ConnectionId
            && row.EntityId == state.EntityId && row.LibraryRootId == state.LibraryRootId)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.Revision, state.Revision).SetProperty(row => row.Phase, state.Phase)
                .SetProperty(row => row.StateJson, serialized).SetProperty(row => row.UpdatedAt, now)
                .SetProperty(row => row.Problem, safeProblem)
                .SetProperty(row => row.NextCheckAt, next), token) != 1) {
            throw Conflict();
        }
    }

    private static StoredManagedRequest Same(StoredManagedRequest existing, ManagedRequestOperation operation, ManagedRequestPlan plan) {
        if (existing.Operation.State.ConnectionId != operation.State.ConnectionId || existing.Plan.Fingerprint != plan.Fingerprint) {
            throw Conflict();
        }

        return existing;
    }

    private static ManagedRequestConflictException Conflict() =>
        new("This managed request changed. Reload its progress before continuing.");

    #endregion

    #region Actions - Mapping

    private static StoredManagedRequest Map(ManagedRequestRow row) {
        var state = JsonSerializer.Deserialize<ManagedRequestState>(row.StateJson, Json)
            ?? throw new InvalidDataException("Invalid managed request state.");
        var plan = JsonSerializer.Deserialize<ManagedRequestPlan>(row.PlanJson, Json)
            ?? throw new InvalidDataException("Invalid managed request intent.");
        if (state.OperationId != row.Id || state.ConnectionId != row.ConnectionId || state.EntityId != row.EntityId
            || state.LibraryRootId != row.LibraryRootId || state.Revision != row.Revision || state.Phase != row.Phase) {
            throw new InvalidDataException("Inconsistent managed request identity.");
        }

        return new(new(state), plan, row.CreatedAt, row.UpdatedAt, row.Problem);
    }

    #endregion
}
