using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using ContractCapability = Prismedia.Contracts.Entities.EntityCapability;
using SeriesMetadataDocumentCapability = Prismedia.Contracts.Entities.SeriesMetadataCapability;
using ThumbnailMetaIcons = Prismedia.Contracts.Entities.EntityThumbnailMetaIcons;

namespace Prismedia.Domain.Media;

/// <summary>Defines the series grouping kind and its default credits capability.</summary>
public sealed class VideoSeriesEntityKindDefinition() : EntityKindDefinition<VideoSeries>(
    EntityKind.VideoSeries,
    "video-series",
    "Video Series",
    "Series",
    EntityKindCategory.Media,
    EntityStorageShape.Folder,
    new EntityKindPresentation(
        EntityKindIcon.Series,
        EntityKindIcon.Video,
        2,
        3,
        EntityAccentHue.Yellow,
        EntityAccentHue.Green,
        EntityArtworkFit.Cover,
        usesRepresentativeChildArtwork: true),
    new EntityKindNavigation(EntityKind.VideoSeries, "series", "/series", "/series/{id}"),
    new EntityKindSearch(1),
    new EntityKindBehavior(
        identification: new(AutoIdentifySelectorKind.Video, enumeratesChildren: true),
        engagement: new(EntityEngagementMode.Playback),
        libraryVisibility: EntityLibraryVisibilityPolicy.FromDescendants(EntityKind.VideoEpisode, 2),
        supportsFileDeletion: true,
        prunesWhenEmpty: true,
        mediaQualityFamily: EntityMediaQualityFamily.Video),
    defaultCapabilities: static () =>
    [
        new CapabilityCredits(),
        new CapabilityProgress(),
        new CapabilityConsumption()
    ]) {
    /// <inheritdoc />
    public override EntityProgressTopology ProgressTopology => EntityProgressTopology.OrderedContainer(EntityKind.VideoEpisode);

    /// <inheritdoc />
    public override AcquisitionAncestorContextRole AcquisitionAncestorContextRole =>
        AcquisitionAncestorContextRole.Series;

    /// <inheritdoc />
    public override bool OwnsMetadataRelationships => true;

    /// <inheritdoc />
    public override IReadOnlyList<EntityStructuralCountDefinition> StructuralThumbnailCounts =>
    [
        new(EntityKind.VideoSeason, 1, ThumbnailMetaIcons.Season),
        new(EntityKind.VideoEpisode, 2, ThumbnailMetaIcons.Episode)
    ];

    /// <inheritdoc />
    public override IReadOnlyList<Type> ProjectedCapabilityTypes => [typeof(SeriesMetadataDocumentCapability)];

    /// <inheritdoc />
    public override IReadOnlyList<RequestKindDescriptor> RequestKinds =>
    [
        new(RequestMediaKind.Series, "Series", "Series", "season", EntityKind.VideoSeries, EntityKind.VideoSeries,
            ProfileEntityKind: EntityKind.VideoSeries,
            ReviewSelection: RequestReviewSelection.DirectChildren,
            IsContainer: true, ChildKind: RequestMediaKind.Season, Committable: true,
            AcquisitionKind: EntityKind.VideoSeason)
    ];

    /// <inheritdoc />
    public override EntityStructurePolicy StructurePolicy => EntityStructurePolicy.RootOnly;

    /// <inheritdoc />
    public override AcquisitionProfileDefinition AcquisitionProfile { get; } = new(
        "TV (series)",
        2,
        LibraryRootMediaCapability.ScanVideos,
        [
            EntityDateType.Premiere,
            EntityDateType.Air,
            EntityDateType.FirstAir,
            EntityDateType.StreamingRelease,
            EntityDateType.DigitalRelease,
            EntityDateType.Release
        ],
        "{Series}/Season {Season:00}/{Series} - S{Season:00}E{Episode:00}.{ext}",
        "{Series} {Season} {Season:00} {Episode:00} {Quality} {ext} — 3 segments: series/season/episode",
        AcquisitionNamingFamily.Television,
        AcquisitionCheckpointProtocol.Television);

    /// <inheritdoc />
    protected override IReadOnlyList<ContractCapability> ProjectCapabilities(
        VideoSeries entity,
        EntityKindProjectionContext context) =>
        [new SeriesMetadataDocumentCapability(entity.Status)];
}

/// <summary>Defines the structural season kind and shared-root construction.</summary>
public sealed class VideoSeasonEntityKindDefinition() : RootEntityKindDefinition<VideoSeason>(
    EntityKind.VideoSeason,
    "video-season",
    "Video Season",
    "Seasons",
    EntityKindCategory.Media,
    EntityStorageShape.Folder,
    new EntityKindPresentation(
        EntityKindIcon.Season,
        EntityKindIcon.Video,
        2,
        3,
        EntityAccentHue.Yellow,
        EntityAccentHue.Green,
        EntityArtworkFit.Cover,
        usesRepresentativeChildArtwork: true),
    new EntityKindNavigation(
        EntityKind.VideoSeries,
        "series",
        "/series",
        "/series/{parentId}/seasons/{id}",
        EntityKind.VideoSeries),
    search: null,
    static root => new VideoSeason(
        root.Id,
        root.Title,
        root.ParentEntityId,
        sortOrder: root.SortOrder),
    behavior: new EntityKindBehavior(
        identification: new(enumeratesChildren: true),
        manualAcquisition: EntityManualAcquisitionPolicy.Upload,
        engagement: new(EntityEngagementMode.Playback),
        libraryVisibility: EntityLibraryVisibilityPolicy.FromDescendants(EntityKind.VideoEpisode, 1),
        supportsFileDeletion: true,
        prunesWhenEmpty: true,
        mediaQualityFamily: EntityMediaQualityFamily.Video),
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
    /// <inheritdoc />
    public override EntityProgressTopology ProgressTopology => EntityProgressTopology.OrderedContainer(EntityKind.VideoEpisode);

    private static readonly IReadOnlyList<string> SortOrderPrecedence = Array.AsReadOnly([
        EntityPositionCodes.Season,
        EntityPositionCodes.Sort
    ]);

    /// <inheritdoc />
    public override IReadOnlyList<string> PositionSortOrderPrecedence => SortOrderPrecedence;

    /// <inheritdoc />
    public override string StructuralFallbackPositionCode => EntityPositionCodes.Season;

    /// <inheritdoc />
    public override IReadOnlyList<EntityStructuralCountDefinition> StructuralThumbnailCounts =>
        [new(EntityKind.VideoEpisode, 1, ThumbnailMetaIcons.Episode)];

    /// <inheritdoc />
    public override IReadOnlyList<RequestKindDescriptor> RequestKinds =>
    [
        new(RequestMediaKind.Season, "Season", "Seasons", "episode", EntityKind.VideoSeason, EntityKind.VideoSeason,
            ProfileEntityKind: EntityKind.VideoSeries,
            ReviewSelection: RequestReviewSelection.Root,
            IsContainer: false, ChildKind: RequestMediaKind.Episode, Committable: true,
            AcquisitionKind: EntityKind.VideoSeason, Discoverable: false, AcquireFromEntity: true,
            MaterializeChildPhantoms: true)
    ];

    /// <inheritdoc />
    public override EntityStructurePolicy StructurePolicy { get; } = EntityStructurePolicy.ChildOf(EntityKind.VideoSeries);
}

/// <summary>
/// Domain model for a video series grouping.
/// </summary>
public sealed class VideoSeries : Entity<VideoSeriesEntityKindDefinition> {
    public VideoSeries(
        Guid id,
        string title,
        string? status = null,
        IEnumerable<Entity>? children = null,
        IEnumerable<Entity>? episodes = null,
        IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
        Status = status;

        foreach (var child in children ?? []) {
            AddChild(child);
        }

        foreach (var episode in episodes ?? []) {
            AddChild(episode);
        }
    }

    public string? Status { get; private set; }

    /// <summary>Direct child episodes in insertion order.</summary>
    public IReadOnlyList<Entity> Episodes => ChildrenOf(EntityKind.VideoEpisode);

    /// <summary>Child seasons in insertion order.</summary>
    public IReadOnlyList<Entity> Seasons => ChildrenOf(EntityKind.VideoSeason);

    /// <summary>
    /// Layout for the series detail view, preserving both direct episodes and season groups when
    /// the target series topology contains both.
    /// </summary>
    public VideoSeriesRenderingMode RenderingMode => (Episodes.Count > 0, Seasons.Count > 0) switch {
        (true, true) => VideoSeriesRenderingMode.Mixed,
        (false, true) => VideoSeriesRenderingMode.Seasons,
        _ => VideoSeriesRenderingMode.Flat
    };
}

/// <summary>
/// Structural video-season aggregate.
/// </summary>
public sealed class VideoSeason : Entity<VideoSeasonEntityKindDefinition> {
    public VideoSeason(
        Guid id,
        string title,
        Guid? parentEntityId,
        IEnumerable<EntityCapability>? capabilities = null,
        IEnumerable<Entity>? episodes = null,
        int? sortOrder = null)
        : base(
            id,
            title,
            capabilities,
            parentEntityId: parentEntityId,
            sortOrder: sortOrder) {
        foreach (var episode in episodes ?? []) {
            AddChild(episode);
        }
    }

    /// <summary>Direct child episodes in insertion order.</summary>
    public IReadOnlyList<Entity> Episodes => ChildrenOf(EntityKind.VideoEpisode);
}
