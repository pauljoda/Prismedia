namespace Prismedia.Infrastructure.Persistence.Entities;

/// <summary>Retained manager control intent and command identity; an active slot prevents concurrent actions on a holding.</summary>
public sealed class ManagedControlRow {
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid HoldingId { get; set; }
    public Guid? ActiveHoldingId { get; set; }
    public long Revision { get; set; }
    public string StateJson { get; set; } = "{}";
    public string PlanJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? NextCheckAt { get; set; }
    public string? Problem { get; set; }
}
