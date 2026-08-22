using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using ComicInstallmentMetadataDocumentCapability = Prismedia.Contracts.Entities.ComicInstallmentMetadataCapability;
using ContractCapability = Prismedia.Contracts.Entities.EntityCapability;

namespace Prismedia.Domain.Media;

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
        manualAcquisition: EntityManualAcquisitionPolicy.UploadAndReplacement,
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
    public override int? AutomaticImportFileLimit => 1;

    /// <inheritdoc />
    public override IReadOnlyList<Type> ProjectedCapabilityTypes =>
        [typeof(ComicInstallmentMetadataDocumentCapability)];

    /// <inheritdoc />
    protected override IReadOnlyList<ContractCapability> ProjectCapabilities(
        ComicInstallment entity,
        EntityKindProjectionContext context) =>
        [new ComicInstallmentMetadataDocumentCapability(entity.InstallmentKind)];

    /// <inheritdoc />
    public override IReadOnlyList<RequestKindDescriptor> RequestKinds =>
    [
        new(
            RequestMediaKind.ComicInstallment,
            "Comic Installment",
            "Comic Installments",
            null,
            EntityKind.ComicInstallment,
            EntityKind.ComicInstallment,
            ProfileEntityKind: EntityKind.ComicSeries,
            ReviewSelection: RequestReviewSelection.Root,
            IsContainer: false,
            ChildKind: null,
            Committable: true,
            AcquisitionKind: EntityKind.ComicInstallment,
            Discoverable: false,
            AcquireFromEntity: true)
    ];
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
