namespace Prismedia.Infrastructure.Persistence.Entities;

public sealed class EntityExternalIdRow {
    public Guid Id { get; set; }

    public Guid EntityId { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string? Url { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
