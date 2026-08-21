using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using ComicInstallmentMetadataDocumentCapability = Prismedia.Contracts.Entities.ComicInstallmentMetadataCapability;
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
    public override IReadOnlyList<Type> ProjectedCapabilityTypes => [typeof(SeriesMetadataDocumentCapability)];

    /// <inheritdoc />
    protected override IReadOnlyList<ContractCapability> ProjectCapabilities(
        ComicSeries entity,
        EntityKindProjectionContext context) =>
        [new SeriesMetadataDocumentCapability(entity.Status)];
}

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
    public override IReadOnlyList<EntityStructuralCountDefinition> StructuralThumbnailCounts =>
        [new(EntityKind.ComicInstallment, 1, ThumbnailMetaIcons.Chapter)];
}

/// <summary>Defines an independently released comic chapter, issue, special, or one-shot.</summary>
public sealed class ComicInstallmentEntityKindDefinition() : EntityKindDefinition<ComicInstallment>(
    EntityKind.ComicInstallment,
    "comic-installment",
    "Comic Installment",
    "Installments",
    EntityKindCategory.Media,
    EntityStorageShape.Archive,
    new EntityKindPresentation(
        EntityKindIcon.Chapter,
        EntityKindIcon.Book,
        2,
        3,
        EntityAccentHue.Cyan,
        EntityAccentHue.Blue,
        EntityArtworkFit.Cover,
        borrowArtworkFromParentKinds: [EntityKind.ComicSeries, EntityKind.ComicVolume]),
    new EntityKindNavigation(
        EntityKind.ComicSeries,
        "comics",
        "/comics",
        "/comics/{parentId}/installments/{id}",
        EntityKind.ComicSeries),
    search: null,
    behavior: new EntityKindBehavior(
        identification: new(
            AutoIdentifySelectorKind.Comic,
            allowsDirectReconcileChildTarget: true),
        engagement: new(EntityEngagementMode.Reading),
        libraryVisibility: EntityLibraryVisibilityPolicy.DirectRoot,
        supportsFileDeletion: true),
    defaultCapabilities: static () =>
    [
        new CapabilityFingerprints(),
        new CapabilityStats(),
        new CapabilityTechnical(),
        new CapabilitySource(),
        new CapabilityPosition(),
        new CapabilityCredits(),
        new CapabilityDates(),
        new CapabilityProgress(),
        new CapabilityConsumption()
    ]) {
    private static readonly IReadOnlyList<string> SortOrderPrecedence = Array.AsReadOnly([
        EntityPositionCodes.Chapter,
        EntityPositionCodes.Sort
    ]);

    /// <inheritdoc />
    public override EntityProgressTopology ProgressTopology => EntityProgressTopology.OrderedRollup(
        EntityKind.ComicInstallment,
        EntityKind.ComicVolume,
        EntityKind.ComicSeries);

    /// <inheritdoc />
    public override EntityStructurePolicy StructurePolicy { get; } =
        EntityStructurePolicy.ChildOf(EntityKind.ComicSeries, EntityKind.ComicVolume);

    /// <inheritdoc />
    public override IReadOnlyList<string> PositionSortOrderPrecedence => SortOrderPrecedence;

    /// <inheritdoc />
    public override bool OwnsMetadataRelationships => true;

    /// <inheritdoc />
    public override IReadOnlyList<Type> ProjectedCapabilityTypes =>
        [typeof(ComicInstallmentMetadataDocumentCapability)];

    /// <inheritdoc />
    protected override IReadOnlyList<ContractCapability> ProjectCapabilities(
        ComicInstallment entity,
        EntityKindProjectionContext context) =>
        [new ComicInstallmentMetadataDocumentCapability(entity.InstallmentKind)];
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

/// <summary>Independently released comic chapter, issue, special, or one-shot.</summary>
public sealed class ComicInstallment : Entity<ComicInstallmentEntityKindDefinition> {
    /// <summary>Creates an installment under a comic series or optional volume.</summary>
    public ComicInstallment(
        Guid id,
        string title,
        ComicInstallmentKind installmentKind,
        Guid? parentEntityId,
        IEnumerable<EntityCapability>? capabilities = null,
        int? sortOrder = null)
        : base(id, title, capabilities, parentEntityId: parentEntityId, sortOrder: sortOrder) {
        InstallmentKind = installmentKind;
    }

    /// <summary>Released-work subtype retained independently from its exact display label.</summary>
    public ComicInstallmentKind InstallmentKind { get; private set; }
}
