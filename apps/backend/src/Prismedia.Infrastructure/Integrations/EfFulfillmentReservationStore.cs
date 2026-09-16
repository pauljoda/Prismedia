using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>
/// Writes durable ownership under the PostgreSQL invariant shared with every native acquisition and
/// monitor mutation, including bulk updates and recovery paths.
/// </summary>
public sealed class EfFulfillmentReservationStore(PrismediaDbContext db) : IFulfillmentReservationStore {
    /// <inheritdoc />
    public async Task ReserveAsync(Guid ownerId, FulfillmentOwnerKind ownerKind, Guid connectionId, Guid entityId,
        BookRendition? rendition, CancellationToken token) {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Fulfillment ownership must be accepted with its durable intent in one transaction.");
        if (ownerId == Guid.Empty || connectionId == Guid.Empty || !Enum.IsDefined(ownerKind))
            throw new ArgumentException("Choose an explicit connection and durable ownership operation.");
        var existing = await db.FulfillmentReservations.SingleOrDefaultAsync(row => row.OwnerId == ownerId
            && row.OwnerKind == ownerKind && row.EntityId == entityId && row.BookRendition == rendition, token);
        if (existing is not null) {
            if (existing.ConnectionId != connectionId || existing.ReleasedAt is not null)
                throw new ArgumentException("This ownership operation has already been accepted or released with different intent.");
            return;
        }
        var kind = await db.Entities.Where(row => row.Id == entityId).Select(row => row.KindCode).SingleAsync(token);
        var scope = new FulfillmentScope(entityId, EntityKindRegistry.Require(kind), rendition);
        var identities = await db.EntityExternalIds.AsNoTracking().Where(row => row.EntityId == entityId)
            .ToDictionaryAsync(row => row.Provider.ToLowerInvariant(), row => row.Value, token);
        db.FulfillmentReservations.Add(new() { Id = Guid.NewGuid(), OwnerId = ownerId, OwnerKind = ownerKind,
            ConnectionId = connectionId, EntityId = scope.EntityId, BookRendition = scope.Rendition,
            ExternalIdsJson = JsonSerializer.Serialize(identities), CreatedAt = DateTimeOffset.UtcNow });
        try { await db.SaveChangesAsync(token); }
        catch (Exception error) when (FulfillmentOwnershipViolation.IsConflict(error)) {
            throw new FulfillmentOwnershipConflictException(error);
        }
    }
}
