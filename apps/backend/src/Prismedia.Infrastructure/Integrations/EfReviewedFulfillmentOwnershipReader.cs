using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Projects saved fulfillment and source ownership for canonical reviewed Entity scopes.</summary>
public sealed class EfReviewedFulfillmentOwnershipReader(
    PrismediaDbContext db,
    IEntityExternalIdentityStore externalIdentities)
    : IReviewedFulfillmentOwnershipReader {
    /// <inheritdoc />
    public async Task<IReadOnlyList<ReviewedFulfillmentOwnership>> ListAsync(
        ManagedLookupInput work,
        CancellationToken cancellationToken) {
        var entityId = await ResolveUniqueEntityAsync(work.EntityKind, work.ExternalIds, parentEntityId: null, cancellationToken);
        if (entityId is null) return [];

        var scopes = await ResolveScopesAsync(entityId.Value, work, cancellationToken);
        if (scopes.Count == 0) return [];

        var scopeIds = scopes.Select(scope => scope.EntityId).ToArray();
        var scopeKinds = scopes.Select(scope => scope.Kind.ToCode()).ToArray();
        var matchRows = await db.Database.SqlQuery<OwnershipMatchRow>($$"""
                SELECT reservation.id AS "ReservationId", target.entity_id AS "ScopeId"
                FROM fulfillment_reservations reservation
                CROSS JOIN unnest({{scopeIds}}, {{scopeKinds}}) AS target(entity_id, kind_code)
                WHERE reservation.released_at IS NULL
                  AND prismedia_fulfillment_matches(
                      reservation, target.entity_id, target.kind_code, NULL::text)
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
        var sourceScopeIds = await db.EntityFiles.AsNoTracking()
            .Where(file => scopeIds.Contains(file.EntityId)
                && file.Role == EntityFileRole.Source)
            .Select(file => file.EntityId)
            .Distinct()
            .ToHashSetAsync(cancellationToken);

        var activeMatches = matches.Where(match =>
            !requests.TryGetValue(match.Reservation.OwnerId, out var request)
            || request.Phase is not (ManagedRequestPhase.Cancelled or ManagedRequestPhase.OwnershipReleased));
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
        var nativeScopeIds = await NativeScopeIdsAsync(scopes, cancellationToken);
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

    private async Task<IReadOnlyList<(Guid EntityId, EntityKind Kind)>> ResolveScopesAsync(
        Guid entityId,
        ManagedLookupInput work,
        CancellationToken cancellationToken) {
        if (work.EntityKind != EntityKind.VideoSeries)
            return [(entityId, work.EntityKind)];
        if (work.Targets is null || work.Targets.Count == 0) return [];
        return await ResolveTargetsAsync(entityId, work.Targets, cancellationToken);
    }

    private async Task<Guid?> ResolveUniqueEntityAsync(
        EntityKind kind,
        IReadOnlyDictionary<string, string> externalIds,
        Guid? parentEntityId,
        CancellationToken cancellationToken) {
        if (externalIds.Count == 0) return null;
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
                target.ExternalIds.Select(identity => new ExternalIdentity(identity.Key, identity.Value)).ToHashSet()))
            .ToArray();
        var namespaces = requested.SelectMany(target => target.Identities)
            .Select(identity => identity.Namespace).Distinct().ToArray();
        var values = requested.SelectMany(target => target.Identities)
            .Select(identity => identity.Value).Distinct().ToArray();
        var identityRows = await db.EntityExternalIds.AsNoTracking()
            .Where(row => namespaces.Contains(row.Provider.Trim().ToLower()) && values.Contains(row.Value.Trim()))
            .ToArrayAsync(cancellationToken);
        var candidateIds = identityRows.Select(row => row.EntityId).Distinct().ToArray();
        if (candidateIds.Length == 0) return [];
        var candidateKinds = await db.Entities.AsNoTracking()
            .Where(row => candidateIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.KindCode, cancellationToken);
        var descendants = (await db.Database.SqlQuery<Guid>($$"""
                WITH RECURSIVE ancestry AS (
                    SELECT entity.id AS leaf_id, entity.id, entity.parent_entity_id
                    FROM entities entity WHERE entity.id = ANY({{candidateIds}})
                    UNION ALL
                    SELECT ancestry.leaf_id, parent.id, parent.parent_entity_id
                    FROM entities parent JOIN ancestry ON parent.id = ancestry.parent_entity_id
                )
                SELECT DISTINCT leaf_id AS "Value" FROM ancestry WHERE id = {{rootId}}
                """)
            .ToArrayAsync(cancellationToken)).ToHashSet();

        var scopes = new List<(Guid EntityId, EntityKind Kind)>();
        foreach (var target in requested) {
            var kindCode = target.Target.EntityKind.ToCode();
            var matches = identityRows
                .Select(row => new {
                    row.EntityId,
                    Identity = new ExternalIdentity(row.Provider, row.Value)
                })
                .Where(row => descendants.Contains(row.EntityId)
                    && candidateKinds.GetValueOrDefault(row.EntityId) == kindCode
                    && target.Identities.Contains(row.Identity))
                .GroupBy(row => row.EntityId)
                .Select(group => new ExternalIdentityMatch(
                    group.Key,
                    group.Select(row => row.Identity).Distinct().ToArray()))
                .ToArray();
            var resolution = new ExternalIdentityResolution(matches);
            if (resolution.Status == ExternalIdentityResolutionStatus.Ambiguous)
                throw new ExternalIdentityAmbiguityException(target.Target.EntityKind, resolution);
            if (resolution.EntityId is { } targetId) scopes.Add((targetId, target.Target.EntityKind));
        }
        return scopes.Distinct().ToArray();
    }

    private async Task<HashSet<Guid>> NativeScopeIdsAsync(
        IReadOnlyList<(Guid EntityId, EntityKind Kind)> scopes,
        CancellationToken cancellationToken) {
        var scopeIds = scopes.Select(scope => scope.EntityId).ToArray();
        var scopeKinds = scopes.Select(scope => scope.Kind.ToCode()).ToArray();
        var imported = AcquisitionStatus.Imported.ToCode();
        var cancelled = AcquisitionStatus.Cancelled.ToCode();
        return (await db.Database.SqlQuery<Guid>($$"""
                SELECT DISTINCT target.entity_id AS "Value"
                FROM unnest({{scopeIds}}, {{scopeKinds}}) AS target(entity_id, kind_code)
                WHERE EXISTS (
                    SELECT 1 FROM acquisitions acquisition
                    WHERE acquisition.status NOT IN ({{imported}}, {{cancelled}})
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
                    WHERE (monitor.entity_id IS NOT NULL
                            AND prismedia_fulfillment_overlap(monitor.entity_id, target.entity_id))
                       OR (acquisition.id IS NOT NULL
                           AND acquisition.status NOT IN ({{imported}}, {{cancelled}})
                           AND ((acquisition.entity_id IS NOT NULL
                                  AND prismedia_fulfillment_overlap(acquisition.entity_id, target.entity_id))
                             OR (acquisition.kind = target.kind_code
                                 AND acquisition.identity_namespace IS NOT NULL
                                 AND EXISTS (
                                   SELECT 1 FROM entity_external_ids identity
                                   WHERE identity.entity_id = target.entity_id
                                     AND lower(identity.provider) = lower(acquisition.identity_namespace)
                                     AND identity.value = acquisition.identity_value)))))
                """)
            .ToArrayAsync(cancellationToken)).ToHashSet();
    }

    private sealed class OwnershipMatchRow {
        public Guid ReservationId { get; init; }
        public Guid ScopeId { get; init; }
    }

    private sealed record RequestedTarget(
        ManagedLookupTarget Target,
        IReadOnlySet<ExternalIdentity> Identities);
}
