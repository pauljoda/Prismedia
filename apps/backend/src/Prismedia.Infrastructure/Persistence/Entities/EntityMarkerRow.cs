namespace Prismedia.Infrastructure.Persistence.Entities;

public sealed class EntityMarkerRow {
    public Guid Id { get; set; }

    public Guid EntityId { get; set; }

    public string Title { get; set; } = string.Empty;

    public double Seconds { get; set; }

    public double? EndSeconds { get; set; }

    /// <summary>
    /// Source-owned chapter index for markers imported from a media container. Null identifies a
    /// user-managed timeline marker.
    /// </summary>
    public int? SourceIndex { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
