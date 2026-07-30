using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using ContractCapability = Prismedia.Contracts.Entities.EntityCapability;
using SeriesMetadataDocumentCapability = Prismedia.Contracts.Entities.SeriesMetadataCapability;

namespace Prismedia.Domain.Media;

/// <summary>Defines the series grouping kind and its default credits capability.</summary>
public sealed class VideoSeriesEntityKindDefinition() : EntityKindDefinition<VideoSeries>(
    EntityKind.VideoSeries,
    "video-series",
    "Video Series",
    "Series",
    EntityKindCategory.Media,
    EntityStorageShape.Folder,
    defaultCapabilities: static () => [new CapabilityCredits()],
    enumeratesIdentifyChildren: true,
    supportsFileDeletion: true) {
    /// <inheritdoc />
    public override IReadOnlyList<Type> ProjectedCapabilityTypes => [typeof(SeriesMetadataDocumentCapability)];

    /// <inheritdoc />
    public override IReadOnlyList<RequestKindDescriptor> RequestKinds =>
    [
        new(RequestMediaKind.Series, "Series", "Series", "season", EntityKind.VideoSeries, EntityKind.VideoSeries,
            ProfileEntityKind: EntityKind.VideoSeries,
            LibraryRootMediaCapability: LibraryRootMediaCapability.ScanVideos,
            ReviewSelection: RequestReviewSelection.DirectChildren,
            IsContainer: true, ChildKind: RequestMediaKind.Season, Committable: true,
            AcquisitionKind: EntityKind.VideoSeason)
    ];

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
    static root => new VideoSeason(
        root.Id,
        root.Title,
        root.ParentEntityId,
        sortOrder: root.SortOrder),
    defaultCapabilities: static () =>
    [
        new CapabilityDescription(),
        new CapabilityDates(),
        new CapabilitySource(),
        new CapabilityPosition(),
        new CapabilityCredits()
    ],
    enumeratesIdentifyChildren: true,
    supportsFileDeletion: true) {
    /// <inheritdoc />
    public override IReadOnlyList<RequestKindDescriptor> RequestKinds =>
    [
        new(RequestMediaKind.Season, "Season", "Seasons", "episode", EntityKind.VideoSeason, EntityKind.VideoSeason,
            ProfileEntityKind: EntityKind.VideoSeries,
            LibraryRootMediaCapability: LibraryRootMediaCapability.ScanVideos,
            ReviewSelection: RequestReviewSelection.Root,
            IsContainer: false, ChildKind: RequestMediaKind.Episode, Committable: true,
            AcquisitionKind: EntityKind.VideoSeason, Discoverable: false, AcquireFromEntity: true,
            MaterializeChildPhantoms: true)
    ];
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
        IEnumerable<Entity>? videos = null,
        IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
        Status = status;

        foreach (var child in children ?? []) {
            AddChild(child);
        }

        foreach (var video in videos ?? []) {
            AddChild(video);
        }
    }

    public string? Status { get; private set; }

    /// <summary>Direct child videos in insertion order.</summary>
    public IReadOnlyList<Entity> Videos => ChildrenOf(EntityKind.Video);

    /// <summary>Child seasons in insertion order.</summary>
    public IReadOnlyList<Entity> Seasons => ChildrenOf(EntityKind.VideoSeason);

    /// <summary>
    /// Layout for the series detail view, derived from whether the series has season children.
    /// </summary>
    public VideoSeriesRenderingMode RenderingMode =>
        Seasons.Count > 0 ? VideoSeriesRenderingMode.Seasons : VideoSeriesRenderingMode.Flat;
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
        IEnumerable<Entity>? videos = null,
        int? sortOrder = null)
        : base(
            id,
            title,
            capabilities,
            parentEntityId: parentEntityId,
            sortOrder: sortOrder) {
        foreach (var video in videos ?? []) {
            AddChild(video);
        }
    }

    public IReadOnlyList<Entity> Videos => ChildrenOf(EntityKind.Video);
}
