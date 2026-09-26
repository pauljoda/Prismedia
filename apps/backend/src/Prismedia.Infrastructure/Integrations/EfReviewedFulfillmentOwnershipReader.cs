using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Projects saved fulfillment and source ownership for canonical reviewed Entity scopes.</summary>
public sealed class EfReviewedFulfillmentOwnershipReader(
    PrismediaDbContext db,
    IEntityExternalIdentityStore externalIdentities)
    : IReviewedFulfillmentOwnershipReader {
    #region Actions - Queries

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReviewedFulfillmentOwnership>> ListAsync(
        ManagedLookupInput work,
        CancellationToken cancellationToken) {
        var entityId = await ResolveUniqueEntityAsync(work.EntityKind, work.ExternalIds, parentEntityId: null, cancellationToken);
        if (entityId is null) {
            return [];
        }

        var scopes = await ResolveScopesAsync(entityId.Value, work, cancellationToken);
        if (scopes.Count == 0) {
            return [];
        }

        var scopeIds = scopes.Select(scope => scope.EntityId).ToArray();
        var scopeKinds = scopes.Select(scope => scope.Kind.ToCode()).ToArray();
        // A rendition scope only overlaps ownership of that rendition; whole-work scopes overlap every owner.
        var rendition = work.BookRendition?.ToCode();
        var matchRows = await db.Database.SqlQuery<OwnershipMatchRow>($$"""
                SELECT reservation.id AS "ReservationId", target.entity_id AS "ScopeId"
                FROM fulfillment_reservations reservation
                CROSS JOIN unnest({{scopeIds}}, {{scopeKinds}}) AS target(entity_id, kind_code)
                WHERE reservation.released_at IS NULL
                  AND prismedia_fulfillment_matches(
                      reservation, target.entity_id, target.kind_code, {{rendition}}::text)
                """)
            .ToArrayAsync(cancellationToken);
        var reservationIds = matchRows.Select(match => match.ReservationId).Distinct().ToArray();
        var reservations = await db.FulfillmentReservations.AsNoTracking()
            .Where(reservation => reservationIds.Contains(reservation.Id))
            .ToDictionaryAsync(reservation => reservation.Id, cancellationToken);
        var matches = matchRows
            .Where(match => reservations.ContainsKey(match.ReservationId))
            .Select(match => (match.ScopeId, Reservation: reservations[match.ReservationId]))
            .ToArray();

        var ownerIds = matches.Select(match => match.Reservation.OwnerId).Distinct().ToArray();
        var requests = await db.ManagedRequests.AsNoTracking()
            .Where(request => ownerIds.Contains(request.Id))
            .ToDictionaryAsync(request => request.Id, cancellationToken);
        var connectionIds = matches.Select(match => match.Reservation.ConnectionId).Distinct().ToArray();
        var connections = await db.IntegrationConnections.AsNoTracking()
            .Where(connection => connectionIds.Contains(connection.Id))
            .ToDictionaryAsync(connection => connection.Id, cancellationToken);
        var sourceScopeIds = await SourceScopeIdsAsync(scopeIds, work, cancellationToken);

        var activeMatches = matches.Where(match =>
            !requests.TryGetValue(match.Reservation.OwnerId, out var request)
            || ManagedRequestPhaseDefinition.HoldingFulfillment.Contains(request.Phase));
        var projected = activeMatches
            .GroupBy(match => new {
                match.Reservation.OwnerId,
                match.Reservation.OwnerKind,
                match.Reservation.ConnectionId
            })
            .Select(group => {
                requests.TryGetValue(group.Key.OwnerId, out var request);
                connections.TryGetValue(group.Key.ConnectionId, out var connection);
                var targetIds = work.EntityKind == EntityKind.VideoSeries
                    ? group.Select(match => match.ScopeId).Distinct().Order().ToArray()
                    : null;
                return new ReviewedFulfillmentOwnership(
                    entityId.Value,
                    targetIds,
                    group.Key.OwnerKind,
                    group.Key.ConnectionId,
                    connection?.Name,
                    request?.Id,
                    request?.Phase,
                    group.All(match => sourceScopeIds.Contains(match.ScopeId)));
            })
            .ToList();

        var externallyOwnedScopeIds = activeMatches.Select(match => match.ScopeId).ToHashSet();
        var nativeScopeIds = await NativeScopeIdsAsync(scopes, work.BookRendition, cancellationToken);
        nativeScopeIds.UnionWith(sourceScopeIds);
        nativeScopeIds.ExceptWith(externallyOwnedScopeIds);
        if (nativeScopeIds.Count != 0) {
            projected.Add(new ReviewedFulfillmentOwnership(
                entityId.Value,
                work.EntityKind == EntityKind.VideoSeries ? nativeScopeIds.Order().ToArray() : null,
                OwnerKind: null,
                ConnectionId: null,
                ConnectionName: null,
                RequestId: null,
                RequestPhase: null,
                HasLocalSource: nativeScopeIds.All(sourceScopeIds.Contains)));
        }

        return projected;
    }

    #endregion

    #region Actions - Scope Resolution

    private async Task<IReadOnlyList<(Guid EntityId, EntityKind Kind)>> ResolveScopesAsync(
        Guid entityId,
        ManagedLookupInput work,
        CancellationToken cancellationToken) {
        if (work.EntityKind != EntityKind.VideoSeries) {
            return [(entityId, work.EntityKind)];
        }

        if (work.Targets is null || work.Targets.Count == 0) {
            return [];
        }

        return await ResolveTargetsAsync(entityId, work.Targets, cancellationToken);
    }

    private async Task<Guid?> ResolveUniqueEntityAsync(
        EntityKind kind,
        IReadOnlyDictionary<string, string> externalIds,
        Guid? parentEntityId,
        CancellationToken cancellationToken) {
        if (externalIds.Count == 0) {
            return null;
        }

        var identities = externalIds.Select(identity => new ExternalIdentity(identity.Key, identity.Value)).ToArray();
        var resolution = await externalIdentities.ResolveAsync(kind, identities, parentEntityId, cancellationToken);
        if (resolution.Status == ExternalIdentityResolutionStatus.Ambiguous) {
            throw new ExternalIdentityAmbiguityException(kind, resolution);
        }

        return resolution.EntityId;
    }

    private async Task<IReadOnlyList<(Guid EntityId, EntityKind Kind)>> ResolveTargetsAsync(
        Guid rootId,
        IReadOnlyList<ManagedLookupTarget> targets,
        CancellationToken cancellationToken) {
        var requested = targets
            .Select(target => new RequestedTarget(
                target,
                target.ExternalIds.Select(identity => new ExternalIdentity(
                    identity.Key.Trim().ToLowerInvariant(), identity.Value.Trim())).ToHashSet()))
            .ToArray();
        var candidateIds = await db.Database.SqlQuery<Guid>($$"""
                WITH RECURSIVE descendants AS (
                    SELECT entity.id, entity.parent_entity_id
                    FROM entities entity WHERE entity.parent_entity_id = {{rootId}}
                    UNION ALL
                    SELECT child.id, child.parent_entity_id
                    FROM entities child JOIN descendants parent ON child.parent_entity_id = parent.id
                )
                SELECT DISTINCT id AS "Value" FROM descendants
                """)
            .ToArrayAsync(cancellationToken);
        if (candidateIds.Length == 0) {
            return [];
        }

        var kindCodes = requested.Select(target => target.Target.EntityKind.ToCode()).Distinct().ToArray();
        var candidates = await db.Entities.AsNoTracking()
            .Where(row => candidateIds.Contains(row.Id) && kindCodes.Contains(row.KindCode))
            .Select(row => new { row.Id, row.KindCode, row.ParentEntityId })
            .ToArrayAsync(cancellationToken);
        if (candidates.Length == 0) {
            return [];
        }

        candidateIds = candidates.Select(candidate => candidate.Id).ToArray();
        var namespaces = requested.SelectMany(target => target.Identities)
            .Select(identity => identity.Namespace.Trim().ToLower()).Distinct().ToArray();
        var values = requested.SelectMany(target => target.Identities)
            .Select(identity => identity.Value.Trim()).Distinct().ToArray();
        var identityRows = await db.EntityExternalIds.AsNoTracking()
            .Where(row => candidateIds.Contains(row.EntityId)
                && namespaces.Contains(row.Provider.Trim().ToLower()) && values.Contains(row.Value.Trim()))
            .ToArrayAsync(cancellationToken);
        var positionIds = candidateIds.Concat(candidates
            .Where(candidate => candidate.ParentEntityId is not null)
            .Select(candidate => candidate.ParentEntityId!.Value)).Distinct().ToArray();
        var positions = (await db.EntityPositions.AsNoTracking()
                .Where(row => positionIds.Contains(row.EntityId))
                .ToArrayAsync(cancellationToken))
            .GroupBy(row => row.EntityId)
            .ToDictionary(group => group.Key,
                group => group.ToDictionary(row => row.Code, row => row.Value, StringComparer.Ordinal));

        var scopes = new List<(Guid EntityId, EntityKind Kind)>();
        foreach (var target in requested) {
            var kindCode = target.Target.EntityKind.ToCode();
            var hasCoordinates = target.Target.SeasonNumber is not null && target.Target.EpisodeNumber is not null
                || target.Target.AbsoluteNumber is not null;
            if (target.Identities.Count == 0 && !hasCoordinates) {
                continue;
            }

            var matches = candidates
                .Where(candidate => candidate.KindCode == kindCode && CoordinatesMatch(target.Target, candidate.Id,
                    candidate.ParentEntityId, positions))
                .Select(candidate => new ExternalIdentityMatch(
                    candidate.Id,
                    identityRows.Where(row => row.EntityId == candidate.Id)
                        .Select(row => new ExternalIdentity(row.Provider.Trim().ToLower(), row.Value.Trim()))
                        .Where(target.Identities.Contains)
                        .Distinct().ToArray()))
                .Where(match => target.Identities.Count == 0 || match.MatchedIdentities.Count != 0)
                .ToArray();
            var resolution = new ExternalIdentityResolution(matches);
            if (resolution.Status == ExternalIdentityResolutionStatus.Ambiguous) {
                throw new ExternalIdentityAmbiguityException(target.Target.EntityKind, resolution);
            }

            if (resolution.EntityId is { } targetId) {
                scopes.Add((targetId, target.Target.EntityKind));
            }
        }

        return scopes.Distinct().ToArray();
    }

    private static bool CoordinatesMatch(
        ManagedLookupTarget target,
        Guid candidateId,
        Guid? parentId,
        IReadOnlyDictionary<Guid, Dictionary<string, int>> positions) {
        var own = positions.GetValueOrDefault(candidateId) ?? [];
        var parent = parentId is { } id
            ? positions.GetValueOrDefault(id) ?? []
            : [];
        var season = own.GetValueOrDefault(
            EntityPositionCodes.Season,
            parent.GetValueOrDefault(EntityPositionCodes.Season, -1));
        var episode = own.GetValueOrDefault(EntityPositionCodes.Episode, -1);
        var absolute = own.GetValueOrDefault(EntityPositionCodes.AbsoluteEpisode, -1);
        return (target.SeasonNumber is null || target.SeasonNumber == season)
            && (target.EpisodeNumber is null || target.EpisodeNumber == episode)
            && (target.AbsoluteNumber is null || target.AbsoluteNumber == absolute);
    }

    #endregion

    #region Actions - Native Ownership

    /// <summary>
    /// Scopes that already hold a local source: the Entity's own source file, or, for a rendition whose
    /// files belong to child Entities, a child of the rendition's target kind with a source file.
    /// </summary>
    private async Task<HashSet<Guid>> SourceScopeIdsAsync(
        Guid[] scopeIds,
        ManagedLookupInput work,
        CancellationToken cancellationToken) {
        var target = work.BookRendition is { } rendition && ManagedFulfillmentPolicy.Supports(work.EntityKind)
            ? ManagedFulfillmentPolicy.For(work.EntityKind).TargetFor(rendition)
            : null;
        if (target is null || target.Kind == work.EntityKind) {
            return await db.EntityFiles.AsNoTracking()
                .Where(file => scopeIds.Contains(file.EntityId)
                    && file.Role == EntityFileRole.Source)
                .Select(file => file.EntityId)
                .Distinct()
                .ToHashSetAsync(cancellationToken);
        }

        var childKind = target.Kind.ToCode();
        return await db.Entities.AsNoTracking()
            .Where(child => child.KindCode == childKind && child.ParentEntityId != null
                && scopeIds.Contains(child.ParentEntityId.Value)
                && db.EntityFiles.Any(file => file.EntityId == child.Id && file.Role == EntityFileRole.Source))
            .Select(child => child.ParentEntityId!.Value)
            .Distinct()
            .ToHashSetAsync(cancellationToken);
    }

    private async Task<HashSet<Guid>> NativeScopeIdsAsync(
        IReadOnlyList<(Guid EntityId, EntityKind Kind)> scopes,
        BookRendition? scopeRendition,
        CancellationToken cancellationToken) {
        var scopeIds = scopes.Select(scope => scope.EntityId).ToArray();
        var scopeKinds = scopes.Select(scope => scope.Kind.ToCode()).ToArray();
        var owning = AcquisitionStatusDefinition.OwningFulfillment.Select(status => status.ToCode()).ToArray();
        // Native owners of another rendition do not own this scope; owners without a rendition own the default one.
        var rendition = scopeRendition?.ToCode();
        var defaultRendition = BookRendition.Ebook.ToCode();
        return (await db.Database.SqlQuery<Guid>($$"""
                SELECT DISTINCT target.entity_id AS "Value"
                FROM unnest({{scopeIds}}, {{scopeKinds}}) AS target(entity_id, kind_code)
                WHERE EXISTS (
                    SELECT 1 FROM acquisitions acquisition
                    WHERE acquisition.status = ANY({{owning}})
                      AND ({{rendition}}::text IS NULL OR coalesce(acquisition.book_rendition, {{defaultRendition}}) = {{rendition}}::text)
                      AND (
                        (acquisition.entity_id IS NOT NULL
                          AND prismedia_fulfillment_overlap(acquisition.entity_id, target.entity_id))
                        OR (acquisition.kind = target.kind_code
                          AND acquisition.identity_namespace IS NOT NULL
                          AND EXISTS (
                            SELECT 1 FROM entity_external_ids identity
                            WHERE identity.entity_id = target.entity_id
                              AND lower(identity.provider) = lower(acquisition.identity_namespace)
                              AND identity.value = acquisition.identity_value))))
                   OR EXISTS (
                    SELECT 1 FROM monitors monitor
                    LEFT JOIN acquisitions acquisition ON acquisition.id = monitor.acquisition_id
                    WHERE ({{rendition}}::text IS NULL OR coalesce(monitor.book_rendition, {{defaultRendition}}) = {{rendition}}::text)
                      AND ((monitor.entity_id IS NOT NULL
                            AND prismedia_fulfillment_overlap(monitor.entity_id, target.entity_id))
                       OR (acquisition.id IS NOT NULL
                           AND acquisition.status = ANY({{owning}})
                           AND ((acquisition.entity_id IS NOT NULL
                                  AND prismedia_fulfillment_overlap(acquisition.entity_id, target.entity_id))
                             OR (acquisition.kind = target.kind_code
                                 AND acquisition.identity_namespace IS NOT NULL
                                 AND EXISTS (
                                   SELECT 1 FROM entity_external_ids identity
                                   WHERE identity.entity_id = target.entity_id
                                     AND lower(identity.provider) = lower(acquisition.identity_namespace)
                                     AND identity.value = acquisition.identity_value))))))
                """)
            .ToArrayAsync(cancellationToken)).ToHashSet();
    }

    #endregion

    private sealed class OwnershipMatchRow {
        #region Variables

        public Guid ReservationId { get; init; }

        public Guid ScopeId { get; init; }

        #endregion
    }

    private sealed record RequestedTarget(
        ManagedLookupTarget Target,
        IReadOnlySet<ExternalIdentity> Identities);
}
