using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Reserves acquisition ownership before an external side effect is published.</summary>
public interface IFulfillmentReservationStore {
    #region Abstract Methods

    /// <summary>
    /// Reserves a scope in the caller's transaction. Ancestors, descendants, known equivalent provider
    /// identities and matching renditions must have no other owner. Replaying the same owner is safe.
    /// A multi-scope operation holds all Entity lifecycle leases before reserving its first scope.
    /// </summary>
    Task ReserveAsync(Guid ownerId, FulfillmentOwnerKind ownerKind, Guid connectionId, Guid entityId,
        BookRendition? rendition, CancellationToken token);

    #endregion
}
