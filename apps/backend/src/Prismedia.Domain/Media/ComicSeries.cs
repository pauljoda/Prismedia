using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using ContractCapability = Prismedia.Contracts.Entities.EntityCapability;
using SeriesMetadataDocumentCapability = Prismedia.Contracts.Entities.SeriesMetadataCapability;
using ThumbnailMetaIcons = Prismedia.Contracts.Entities.EntityThumbnailMetaIcons;

namespace Prismedia.Domain.Media;

/// <summary>Defines the serialized-comic title/run and its ordered installments.</summary>
public sealed class ComicSeriesEntityKindDefinition() : EntityKindDefinition<ComicSeries>(
    EntityKind.ComicSeries,
    "comic-series",
    "Comic Series",
    "Comics",
    EntityKindCategory.Media,
    EntityStorageShape.Folder,
    new EntityKindPresentation(
        EntityKindIcon.Series,
        EntityKindIcon.Book,
        2,
        3,
        EntityAccentHue.Cyan,
        EntityAccentHue.Blue,
        EntityArtworkFit.Cover,
        usesRepresentativeChildArtwork: true),
    new EntityKindNavigation(EntityKind.ComicSeries, "comics", "/comics", "/comics/{id}"),
    new EntityKindSearch(8),
    new EntityKindBehavior(
        identification: new(AutoIdentifySelectorKind.Comic, enumeratesChildren: true),
        engagement: new(EntityEngagementMode.Reading),
        libraryVisibility: EntityLibraryVisibilityPolicy.FromDescendants(EntityKind.ComicInstallment, 2),
        supportsFileDeletion: true,
        prunesWhenEmpty: true),
    defaultCapabilities: static () =>
    [
        new CapabilityCredits(),
        new CapabilityStats(),
        new CapabilityProgress(),
        new CapabilityConsumption()
    ]) {
    /// <inheritdoc />
    public override EntityProgressTopology ProgressTopology =>
        EntityProgressTopology.OrderedContainer(EntityKind.ComicInstallment);

    /// <inheritdoc />
    public override AcquisitionAncestorContextRole AcquisitionAncestorContextRole =>
        AcquisitionAncestorContextRole.Series;

    /// <inheritdoc />
    public override EntityStructurePolicy StructurePolicy => EntityStructurePolicy.RootOnly;

    /// <inheritdoc />
    public override bool OwnsMetadataRelationships => true;

    /// <inheritdoc />
    public override IReadOnlyList<EntityStructuralCountDefinition> StructuralThumbnailCounts =>
    [
        new(EntityKind.ComicVolume, 1, ThumbnailMetaIcons.Volume),
        new(EntityKind.ComicInstallment, 2, ThumbnailMetaIcons.Chapter)
    ];

    /// <inheritdoc />
    public override IReadOnlyList<RequestKindDescriptor> RequestKinds =>
    [
        new(
            RequestMediaKind.ComicSeries,
            "Comic Series",
            "Comic Series",
            "release",
            EntityKind.ComicSeries,
            EntityKind.ComicSeries,
            ProfileEntityKind: EntityKind.ComicSeries,
            ReviewSelection: RequestReviewSelection.DirectChildren,
            IsContainer: true,
            ChildKind: RequestMediaKind.ComicVolume,
            Committable: true,
            AcquisitionKind: EntityKind.ComicVolume,
            AdditionalChildKinds: [RequestMediaKind.ComicInstallment])
    ];

    /// <inheritdoc />
    public override AcquisitionProfileDefinition AcquisitionProfile { get; } = new(
        "Comics (serialized)",
        4,
        LibraryRootMediaCapability.ScanBooks,
        [
            EntityDateType.Publication,
            EntityDateType.DigitalRelease,
            EntityDateType.PhysicalRelease,
            EntityDateType.Release
        ],
        "{Series}/{VolumeFolder}/{Title}.{ext}",
        "{Series} {VolumeFolder} {Title} {Year} {ext} — series/optional volume/archive layout",
        AcquisitionNamingFamily.Book,
        AcquisitionCheckpointProtocol.Placement,
        JobType.ScanComic);

    /// <inheritdoc />
    public override IReadOnlyList<Type> ProjectedCapabilityTypes => [typeof(SeriesMetadataDocumentCapability)];

    /// <inheritdoc />
    protected override IReadOnlyList<ContractCapability> ProjectCapabilities(
        ComicSeries entity,
        EntityKindProjectionContext context) =>
        [new SeriesMetadataDocumentCapability(entity.Status)];
}

/// <summary>Serialized-comic title or western comic run.</summary>
public sealed class ComicSeries : Entity<ComicSeriesEntityKindDefinition> {
    /// <summary>Creates a series with its optional provider status.</summary>
    public ComicSeries(
        Guid id,
        string title,
        string? status = null,
        IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
        Status = status;
    }

    /// <summary>Provider status such as releasing, completed, or cancelled.</summary>
    public string? Status { get; private set; }
}
