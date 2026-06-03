namespace Prismedia.Infrastructure.Persistence.Entities;

public sealed class EntityRow {
    public Guid Id { get; set; }

    public string KindCode { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Database-computed sort key: <see cref="Title"/> with a leading article ("the", "a", "an")
    /// removed so "The Grinch" sorts under "G". Maintained by a stored generated column, so it is
    /// read-only from the application and always tracks <see cref="Title"/>.
    /// </summary>
    public string SortName { get; private set; } = string.Empty;

    public Guid? ParentEntityId { get; set; }

    public int? SortOrder { get; set; }

    public int? RatingValue { get; set; }

    public bool IsFavorite { get; set; }

    public bool IsNsfw { get; set; }

    public bool IsOrganized { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
