using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Persistence.Entities;

/// <summary>Durable external fulfillment intent with stable local identity and dispatch progress.</summary>
public sealed class ManagedRequestRow {
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid EntityId { get; set; }
    public Guid LibraryRootId { get; set; }
    public long Revision { get; set; }
    public ManagedRequestPhase Phase { get; set; }
    public string StateJson { get; set; } = "{}";
    public string PlanJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? NextCheckAt { get; set; }
    public string? Problem { get; set; }
}
