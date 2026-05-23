namespace Prismedia.Infrastructure.Persistence.Entities;

public sealed class EntityMarkerRow {
    public Guid Id { get; set; }

    public Guid EntityId { get; set; }

    public string Title { get; set; } = string.Empty;

    public double Seconds { get; set; }

    public double? EndSeconds { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
