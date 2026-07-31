using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media;

/// <summary>Defines the playable video kind and its default media capabilities.</summary>
public sealed class VideoEntityKindDefinition() : EntityKindDefinition<Video>(
    EntityKind.Video,
    "video",
    "Video",
    "Videos",
    EntityKindCategory.Media,
    EntityStorageShape.File,
    new EntityKindPresentation(
        EntityKindIcon.Video,
        EntityKindIcon.Video,
        16,
        9,
        EntityAccentHue.Red,
        EntityAccentHue.Orange,
        EntityArtworkFit.Cover,
        borrowArtworkFromParentKinds: [EntityKind.Movie]),
    new EntityKindNavigation(EntityKind.Video, "videos", "/videos", "/videos/{id}"),
    new EntityKindSearch(2),
    new EntityKindBehavior(
        identification: new(
            AutoIdentifySelectorKind.Video,
            allowsDirectReconcileChildTarget: true),
        manualAcquisition: EntityManualAcquisitionPolicy.UploadAndReplacement,
        processing: new EntityProcessingPolicy(
            probeJobType: JobType.ProbeVideo,
            probeRequiresAutomaticMetadata: true,
            fingerprintJobType: JobType.FingerprintVideo,
            previewJobType: JobType.GeneratePreview,
            previewRequiresAutomaticGeneration: true,
            supportsTrickplayGeneration: true,
            subtitleExtractionJobType: JobType.ExtractSubtitles,
            generatedFileRoles: [
                EntityFileRole.Thumbnail,
                EntityFileRole.GridThumbnail,
                EntityFileRole.GridThumbnail2x,
                EntityFileRole.Preview,
                EntityFileRole.Sprite,
                EntityFileRole.Trickplay,
                EntityFileRole.Hls
            ]),
        engagement: new(
            EntityEngagementMode.Playback,
            derivesCompletionFromPlaybackFraction: true),
        browse: new(aggregateParentKinds: [EntityKind.Movie]),
        libraryVisibility: EntityLibraryVisibilityPolicy.DirectRoot,
        supportsFileDeletion: true,
        mediaQualityFamily: EntityMediaQualityFamily.Video,
        supportsAtomicMediaUpgrade: true),
    defaultCapabilities: static () =>
    [
        new CapabilityPlayback(),
        new CapabilityPosition(),
        new CapabilityMarkers(),
        new CapabilitySubtitles(),
        new CapabilityCredits()
    ]) {
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
        new(RequestMediaKind.Episode, "Episode", "Episodes", null, EntityKind.Video, EntityKind.Video,
            ProfileEntityKind: EntityKind.VideoSeries,
            ReviewSelection: RequestReviewSelection.Root,
            IsContainer: false, ChildKind: null, Committable: true,
            AcquisitionKind: EntityKind.Video, Discoverable: false, AcquireFromEntity: true)
    ];
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
