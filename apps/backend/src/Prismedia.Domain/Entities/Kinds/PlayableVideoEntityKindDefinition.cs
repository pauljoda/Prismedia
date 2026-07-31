using Prismedia.Domain.Capabilities;

namespace Prismedia.Domain.Entities;

/// <summary>
/// Opt-in facet for definitions that directly own a playable video source file.
/// Consumers query this facet through definition discovery instead of maintaining kind lists.
/// </summary>
public interface IPlayableVideoKindDefinition {
}

/// <summary>
/// Shared definition base for directly playable video entities. It owns the video processing,
/// playback, quality, file-management, and common mutable-capability contract once for every
/// concrete playable video kind.
/// </summary>
/// <typeparam name="TEntity">Concrete directly playable video entity.</typeparam>
public abstract class PlayableVideoEntityKindDefinition<TEntity> : RootEntityKindDefinition<TEntity>, IPlayableVideoKindDefinition
    where TEntity : Entity {
    /// <summary>Creates a directly playable video definition with optional kind-specific defaults.</summary>
    protected PlayableVideoEntityKindDefinition(
        EntityKind kind,
        string code,
        string displayName,
        string groupLabel,
        EntityKindPresentation presentation,
        EntityKindNavigation navigation,
        EntityKindSearch? search,
        Func<EntityRootData, TEntity> factory,
        EntityIdentificationPolicy? identification,
        EntityManualAcquisitionPolicy? manualAcquisition,
        EntityBrowsePolicy? browse,
        EntityLibraryVisibilityPolicy libraryVisibility,
        Func<IReadOnlyList<EntityCapability>>? additionalDefaultCapabilities = null)
        : base(
            kind,
            code,
            displayName,
            groupLabel,
            EntityKindCategory.Media,
            EntityStorageShape.File,
            presentation,
            navigation,
            search,
            factory,
            new EntityKindBehavior(
                identification: identification,
                manualAcquisition: manualAcquisition,
                processing: VideoProcessing,
                engagement: VideoEngagement,
                browse: browse,
                libraryVisibility: libraryVisibility,
                supportsFileDeletion: true,
                mediaQualityFamily: EntityMediaQualityFamily.Video,
                supportsAtomicMediaUpgrade: true),
            () => CreateDefaultCapabilities(additionalDefaultCapabilities)) {
    }

    private static EntityProcessingPolicy VideoProcessing { get; } = new(
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
        ]);

    private static EntityEngagementPolicy VideoEngagement { get; } = new(
        EntityEngagementMode.Playback,
        derivesCompletionFromPlaybackFraction: true);

    private static IReadOnlyList<EntityCapability> CreateDefaultCapabilities(
        Func<IReadOnlyList<EntityCapability>>? additional) {
        var capabilities = new List<EntityCapability> {
            new CapabilityPlayback(),
            new CapabilityMarkers(),
            new CapabilitySubtitles(),
            new CapabilityCredits()
        };
        if (additional is not null) {
            var extras = additional() ?? throw new InvalidOperationException(
                "Playable video definitions cannot return a null additional-capability collection.");
            capabilities.AddRange(extras);
        }

        return capabilities;
    }
}
