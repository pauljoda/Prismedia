namespace Prismedia.Infrastructure.Persistence.Entities;

public sealed class EntityRow {
    public Guid Id { get; set; }

    public string KindCode { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public Guid? ParentEntityId { get; set; }

    public int? SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
}
