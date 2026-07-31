using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using ContractCapability = Prismedia.Contracts.Entities.EntityCapability;
using TagPolicyDocumentCapability = Prismedia.Contracts.Entities.TagPolicyCapability;

namespace Prismedia.Domain.Taxonomy;

/// <summary>Defines the tag taxonomy kind.</summary>
public sealed class TagEntityKindDefinition() : EntityKindDefinition<Tag>(
    EntityKind.Tag,
    "tag",
    "Tag",
    "Tags",
    EntityKindCategory.Taxonomy,
    EntityStorageShape.None,
    new EntityKindPresentation(
        EntityKindIcon.Tag,
        EntityKindIcon.Tag,
        1,
        1,
        EntityAccentHue.Green,
        EntityAccentHue.Yellow,
        EntityArtworkFit.Cover),
    new EntityKindNavigation(EntityKind.Tag, "tags", "/tags", "/tags/{id}"),
    new EntityKindSearch(5, expandsRelationshipResults: true),
    EntityManualAcquisitionPolicy.None,
    EntityProcessingPolicy.None,
    supportsManualManagement: true) {
    /// <inheritdoc />
    public override IReadOnlyList<Type> ProjectedCapabilityTypes => [typeof(TagPolicyDocumentCapability)];

    /// <inheritdoc />
    protected override IReadOnlyList<ContractCapability> ProjectCapabilities(
        Tag entity,
        EntityKindProjectionContext context) =>
        [new TagPolicyDocumentCapability(entity.IgnoreAutoTag)];
}

/// <summary>
/// Domain model for a tag taxonomy entity.
/// </summary>
public sealed class Tag : Entity<TagEntityKindDefinition> {
    public Tag(Guid id, string title, bool ignoreAutoTag = false, IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
        IgnoreAutoTag = ignoreAutoTag;
    }

    public bool IgnoreAutoTag { get; private set; }
}
