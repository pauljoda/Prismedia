using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Persistence.Entities;

/// <summary>Retained manager control intent and command identity; an active slot prevents concurrent actions on a holding.</summary>
public sealed class ManagedControlRow {
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid HoldingId { get; set; }
    public Guid? ActiveHoldingId { get; set; }
    public long Revision { get; set; }
    /// <summary>Promoted from the state document so ownership release can find unresolved outcomes.</summary>
    public ManagedControlPhase Phase { get; set; }
    public string StateJson { get; set; } = "{}";
    public string PlanJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? NextCheckAt { get; set; }
    public string? Problem { get; set; }
}
