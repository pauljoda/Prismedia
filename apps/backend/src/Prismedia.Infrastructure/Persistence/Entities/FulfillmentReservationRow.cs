using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Persistence.Entities;

/// <summary>
/// Durable external ownership of an Entity scope and rendition. The owner is an accepted operation,
/// independently of transient queue runs. Released records retain their original identity evidence.
/// </summary>
public sealed class FulfillmentReservationRow {
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public FulfillmentOwnerKind OwnerKind { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid EntityId { get; set; }
    public BookRendition? BookRendition { get; set; }
    /// <summary>Provider identities observed at acceptance, retained even if later metadata changes.</summary>
    public string ExternalIdsJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
}
