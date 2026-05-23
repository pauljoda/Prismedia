namespace Prismedia.Infrastructure.Persistence.Entities;

public sealed class EntityRatingRow {
    public Guid EntityId { get; set; }

    public int Value { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
