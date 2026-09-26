using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Persistence.Entities;

/// <summary>Long-lived acquisition evidence, independent of ephemeral queue runs and their retention policy.</summary>
public sealed class IntegrationTransferRow {
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public long Revision { get; set; }
    public IntegrationTransferPhase Phase { get; set; }
    public string StateJson { get; set; } = "{}";
    public string ProtectedPlan { get; set; } = string.Empty;
    public string? ActiveOwnershipKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? LastError { get; set; }
}
