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

    /// <summary>
    /// True when this entity was created by a request but has no file on disk yet ("wanted"). An acquisition
    /// download attaches the Source file and clears this flag, turning the placeholder into a real library item.
    /// </summary>
    public bool IsWanted { get; set; }

    /// <summary>
    /// Count of completed auto-identify runs that ended without a confident match. Once this
    /// reaches the policy maximum the entity is skipped by auto identify and must be identified
    /// manually.
    /// </summary>
    public int AutoIdentifyAttempts { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
