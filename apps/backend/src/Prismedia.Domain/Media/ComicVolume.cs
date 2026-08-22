using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using ThumbnailMetaIcons = Prismedia.Contracts.Entities.EntityThumbnailMetaIcons;

namespace Prismedia.Domain.Media;

/// <summary>Defines an optional collected or thematic grouping within a comic series.</summary>
public sealed class ComicVolumeEntityKindDefinition() : RootEntityKindDefinition<ComicVolume>(
    EntityKind.ComicVolume,
    "comic-volume",
    "Comic Volume",
    "Volumes",
    EntityKindCategory.Media,
    EntityStorageShape.None,
    new EntityKindPresentation(
        EntityKindIcon.Volume,
        EntityKindIcon.Book,
        2,
        3,
        EntityAccentHue.Cyan,
        EntityAccentHue.Blue,
        EntityArtworkFit.Cover,
        usesRepresentativeChildArtwork: true),
    new EntityKindNavigation(
        EntityKind.ComicSeries,
        "comics",
        "/comics",
        "/comics/{parentId}/volumes/{id}",
        EntityKind.ComicSeries),
    search: null,
    static root => new ComicVolume(
        root.Id,
        root.Title,
        root.ParentEntityId,
        sortOrder: root.SortOrder),
    behavior: new EntityKindBehavior(
        identification: new(enumeratesChildren: true),
        engagement: new(EntityEngagementMode.Reading),
        libraryVisibility: EntityLibraryVisibilityPolicy.FromDescendants(EntityKind.ComicInstallment, 1),
        supportsFileDeletion: true,
        prunesWhenEmpty: true),
    defaultCapabilities: static () =>
    [
        new CapabilityDescription(),
        new CapabilityDates(),
        new CapabilitySource(),
        new CapabilityPosition(),
        new CapabilityCredits(),
        new CapabilityStats(),
        new CapabilityProgress(),
        new CapabilityConsumption()
    ]) {
    private static readonly IReadOnlyList<string> SortOrderPrecedence = Array.AsReadOnly([
        EntityPositionCodes.Volume,
        EntityPositionCodes.Sort
    ]);

    /// <inheritdoc />
    public override EntityProgressTopology ProgressTopology =>
        EntityProgressTopology.OrderedContainer(EntityKind.ComicInstallment);

    /// <inheritdoc />
    public override EntityStructurePolicy StructurePolicy { get; } =
        EntityStructurePolicy.ChildOf(EntityKind.ComicSeries);

    /// <inheritdoc />
    public override IReadOnlyList<string> PositionSortOrderPrecedence => SortOrderPrecedence;

    /// <inheritdoc />
    public override string StructuralFallbackPositionCode => EntityPositionCodes.Volume;

    /// <inheritdoc />
    public override bool OwnsMetadataRelationships => true;

    /// <inheritdoc />
    public override bool IsFulfilledBySourceBackedSubtree => true;

    /// <inheritdoc />
    public override IReadOnlyList<EntityStructuralCountDefinition> StructuralThumbnailCounts =>
        [new(EntityKind.ComicInstallment, 1, ThumbnailMetaIcons.Chapter)];

    /// <inheritdoc />
    public override IReadOnlyList<RequestKindDescriptor> RequestKinds =>
    [
        new(
            RequestMediaKind.ComicVolume,
            "Comic Volume",
            "Comic Volumes",
            "installment",
            EntityKind.ComicVolume,
            EntityKind.ComicVolume,
            ProfileEntityKind: EntityKind.ComicSeries,
            ReviewSelection: RequestReviewSelection.Root,
            IsContainer: false,
            ChildKind: RequestMediaKind.ComicInstallment,
            Committable: true,
            AcquisitionKind: EntityKind.ComicVolume,
            Discoverable: false,
            AcquireFromEntity: true,
            MaterializeChildPhantoms: true)
    ];
}

/// <summary>Optional collected or thematic grouping within a serialized-comic title.</summary>
public sealed class ComicVolume : Entity<ComicVolumeEntityKindDefinition> {
    /// <summary>Creates a volume under its required comic-series parent.</summary>
    public ComicVolume(
        Guid id,
        string title,
        Guid? parentEntityId,
        IEnumerable<EntityCapability>? capabilities = null,
        int? sortOrder = null)
        : base(id, title, capabilities, parentEntityId: parentEntityId, sortOrder: sortOrder) {
    }
}
