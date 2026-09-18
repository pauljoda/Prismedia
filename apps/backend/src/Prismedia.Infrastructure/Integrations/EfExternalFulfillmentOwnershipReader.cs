using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Projects active connected ownership through the same database matcher that guards native writes.</summary>
public sealed class EfExternalFulfillmentOwnershipReader(PrismediaDbContext db)
    : IExternalFulfillmentOwnershipReader {
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, ExternalFulfillmentOwnership>> ListAsync(
        IReadOnlyCollection<FulfillmentOwnershipQuery> scopes,
        CancellationToken cancellationToken) {
        if (scopes.Count == 0) return new Dictionary<Guid, ExternalFulfillmentOwnership>();

        var entityIds = scopes.Select(scope => scope.EntityId).ToArray();
        var kindCodes = scopes.Select(scope => scope.Kind.ToCode()).ToArray();
        var renditions = scopes.Select(scope => scope.BookRendition?.ToCode()).ToArray();
        var matches = await db.Database.SqlQuery<OwnershipMatch>($$"""
                SELECT target.entity_id AS "EntityId", connection.name AS "ConnectionName"
                FROM unnest({{entityIds}}, {{kindCodes}}, {{renditions}})
                    AS target(entity_id, kind_code, rendition)
                JOIN fulfillment_reservations reservation
                    ON reservation.released_at IS NULL
                   AND prismedia_fulfillment_matches(
                        reservation, target.entity_id, target.kind_code, target.rendition)
                JOIN integration_connections connection ON connection.id = reservation.connection_id
                ORDER BY target.entity_id, reservation.created_at, reservation.id
                """)
            .ToArrayAsync(cancellationToken);

        return matches
            .GroupBy(match => match.EntityId)
            .ToDictionary(
                group => group.Key,
                group => new ExternalFulfillmentOwnership(group.First().ConnectionName));
    }

    private sealed class OwnershipMatch {
        public Guid EntityId { get; init; }
        public string? ConnectionName { get; init; }
    }
}
