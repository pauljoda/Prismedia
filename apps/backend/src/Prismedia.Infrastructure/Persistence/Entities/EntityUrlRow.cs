namespace Prismedia.Infrastructure.Persistence.Entities;

public sealed class EntityUrlRow {
    public Guid Id { get; set; }

    public Guid EntityId { get; set; }

    public string Url { get; set; } = string.Empty;

    public string? Label { get; set; }

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
