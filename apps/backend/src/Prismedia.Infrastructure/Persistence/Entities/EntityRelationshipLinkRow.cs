namespace Prismedia.Infrastructure.Persistence.Entities;

public sealed class EntityRelationshipLinkRow {
    public Guid EntityId { get; set; }

    public string RelationshipCode { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public Guid TargetEntityId { get; set; }

    public string TargetKindCode { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public string? MetadataJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
