using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media;

/// <summary>Defines the standalone directly playable video kind.</summary>
public sealed class VideoEntityKindDefinition() : PlayableVideoEntityKindDefinition<Video>(
    EntityKind.Video,
    "video",
    "Video",
    "Videos",
    new EntityKindPresentation(
        EntityKindIcon.Video,
        EntityKindIcon.Video,
        16,
        9,
        EntityAccentHue.Red,
        EntityAccentHue.Orange,
        EntityArtworkFit.Cover),
    new EntityKindNavigation(EntityKind.Video, "videos", "/videos", "/videos/{id}"),
    new EntityKindSearch(2),
    PlayableVideoScanPlacement.Standalone,
    static root => new Video(root.Id, root.Title),
    identification: new(AutoIdentifySelectorKind.Video),
    manualAcquisition: EntityManualAcquisitionPolicy.UploadAndReplacement,
    browse: null,
    libraryVisibility: EntityLibraryVisibilityPolicy.DirectRoot) {

    /// <inheritdoc />
    public override EntityProgressTopology ProgressTopology => EntityProgressTopology.Direct;

    /// <inheritdoc />
    public override bool OwnsMetadataRelationships => true;

    /// <inheritdoc />
    public override EntityStructurePolicy StructurePolicy => EntityStructurePolicy.RootOnly;
}

/// <summary>
/// Domain model for a playable video media item.
/// </summary>
public sealed class Video : Entity<VideoEntityKindDefinition> {
    public Video(
        Guid id,
        string title,
        IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
    }
}

/// <summary>Defines a directly playable episode that belongs to a series or season.</summary>
public sealed class VideoEpisodeEntityKindDefinition() : PlayableVideoEntityKindDefinition<VideoEpisode>(
    EntityKind.VideoEpisode,
    "video-episode",
    "Video Episode",
    "Episodes",
    new EntityKindPresentation(
        EntityKindIcon.Video,
        EntityKindIcon.Video,
        16,
        9,
        EntityAccentHue.Red,
        EntityAccentHue.Orange,
        EntityArtworkFit.Cover,
        borrowArtworkFromParentKinds: [EntityKind.VideoSeries, EntityKind.VideoSeason]),
    new EntityKindNavigation(EntityKind.Video, "videos", "/videos", "/videos/{id}"),
    search: null,
    scanPlacement: PlayableVideoScanPlacement.Episode,
    static root => new VideoEpisode(root.Id, root.Title, root.ParentEntityId, sortOrder: root.SortOrder),
    identification: new(
        AutoIdentifySelectorKind.Video,
        pluginFallbackKind: EntityKind.Video,
        allowsDirectReconcileChildTarget: true),
    manualAcquisition: null,
    browse: null,
    libraryVisibility: EntityLibraryVisibilityPolicy.DirectRoot,
    additionalDefaultCapabilities: static () => [new CapabilityPosition()]) {
    /// <inheritdoc />
    public override EntityProgressTopology ProgressTopology => EntityProgressTopology.OrderedRollup(
        EntityKind.VideoEpisode,
        EntityKind.VideoSeason,
        EntityKind.VideoSeries);

    private static readonly IReadOnlyList<string> SortOrderPrecedence = Array.AsReadOnly([
        EntityPositionCodes.Episode,
        EntityPositionCodes.AbsoluteEpisode,
        EntityPositionCodes.Sort
    ]);

    /// <inheritdoc />
    public override IReadOnlyList<string> PositionSortOrderPrecedence => SortOrderPrecedence;

    /// <inheritdoc />
    public override bool OwnsMetadataRelationships => true;

    /// <inheritdoc />
    public override IReadOnlyList<RequestKindDescriptor> RequestKinds =>
    [
        new(RequestMediaKind.Episode, "Episode", "Episodes", null, EntityKind.VideoEpisode, EntityKind.VideoEpisode,
            ProfileEntityKind: EntityKind.VideoSeries,
            ReviewSelection: RequestReviewSelection.Root,
            IsContainer: false, ChildKind: null, Committable: true,
            AcquisitionKind: EntityKind.VideoEpisode, Discoverable: false, AcquireFromEntity: true)
    ];

    /// <inheritdoc />
    public override EntityStructurePolicy StructurePolicy { get; } =
        EntityStructurePolicy.ChildOf(EntityKind.VideoSeries, EntityKind.VideoSeason);
}

/// <summary>Domain model for a directly playable episodic video file.</summary>
public sealed class VideoEpisode : Entity<VideoEpisodeEntityKindDefinition> {
    /// <summary>Creates an episode with its required series or season parent identifier.</summary>
    public VideoEpisode(
        Guid id,
        string title,
        Guid? parentEntityId,
        IEnumerable<EntityCapability>? capabilities = null,
        int? sortOrder = null)
        : base(
            id,
            title,
            capabilities,
            parentEntityId: parentEntityId,
            sortOrder: sortOrder) {
    }
}
