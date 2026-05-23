namespace Prismedia.Infrastructure.Persistence.Entities;

public sealed class EntityFlagRow {
    public Guid EntityId { get; set; }

    public bool IsFavorite { get; set; }

    public bool IsNsfw { get; set; }

    public bool IsOrganized { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
