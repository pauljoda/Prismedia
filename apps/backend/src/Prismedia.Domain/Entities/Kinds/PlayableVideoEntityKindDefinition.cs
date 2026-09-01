using Prismedia.Domain.Capabilities;

namespace Prismedia.Domain.Entities;

/// <summary>
/// Opt-in facet for definitions that directly own a playable video source file.
/// Consumers query this facet through definition discovery instead of maintaining kind lists.
/// </summary>
public interface IPlayableVideoKindDefinition {
    /// <summary>Typed identity of the playable definition.</summary>
    EntityKind Kind { get; }

    /// <summary>
    /// Structural placement this definition owns when a library scan materializes a video file.
    /// The scan parses filesystem layout in Application, then resolves the concrete kind through
    /// this declaration rather than maintaining a second kind map.
    /// </summary>
    PlayableVideoScanPlacement ScanPlacement { get; }

    /// <summary>
    /// Whether playback of this kind advances a structural episodic cursor on its containing
    /// series or season. Standalone videos and movies intentionally do not participate.
    /// </summary>
    bool IsEpisodic => ScanPlacement == PlayableVideoScanPlacement.Episode;
}

/// <summary>Structural placement of one playable video file discovered by a library scan.</summary>
public enum PlayableVideoScanPlacement {
    /// <summary>A parentless, independently browsable video.</summary>
    Standalone,

    /// <summary>A directly playable movie whose folder is provenance, not an Entity file.</summary>
    Movie,

    /// <summary>An episode structurally owned by a series or season.</summary>
    Episode
}

/// <summary>
/// Shared definition base for directly playable video entities. It owns the video processing,
/// playback, quality, file-management, and common mutable-capability contract once for every
/// concrete playable video kind.
/// </summary>
/// <typeparam name="TEntity">Concrete directly playable video entity.</typeparam>
public abstract class PlayableVideoEntityKindDefinition<TEntity> : RootEntityKindDefinition<TEntity>, IPlayableVideoKindDefinition
    where TEntity : Entity {
    /// <inheritdoc />
    public abstract override EntityProgressTopology ProgressTopology { get; }

    /// <summary>Creates a directly playable video definition with optional kind-specific defaults.</summary>
    protected PlayableVideoEntityKindDefinition(
        EntityKind kind,
        string code,
        string displayName,
        string groupLabel,
        EntityKindPresentation presentation,
        EntityKindNavigation navigation,
        EntityKindSearch? search,
        PlayableVideoScanPlacement scanPlacement,
        Func<EntityRootData, TEntity> factory,
        EntityIdentificationPolicy? identification,
        EntityManualAcquisitionPolicy? manualAcquisition,
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
                libraryVisibility: libraryVisibility,
                supportsFileDeletion: true,
                mediaQualityFamily: EntityMediaQualityFamily.Video,
                upgradeMode: EntityUpgradeMode.AtomicMediaFile),
            () => CreateDefaultCapabilities(additionalDefaultCapabilities)) {
        ScanPlacement = scanPlacement;
    }

    /// <inheritdoc />
    public PlayableVideoScanPlacement ScanPlacement { get; }

    private static EntityProcessingPolicy VideoProcessing { get; } = new(
        assetFamily: GeneratedAssetFamily.Video,
        probeJobType: JobType.ProbeVideo,
        probeRequiresAutomaticMetadata: true,
        fingerprintJobType: JobType.FingerprintVideo,
        previewJobType: JobType.GeneratePreview,
        previewRequiresAutomaticGeneration: true,
        trickplayJobType: JobType.GenerateTrickplay,
        subtitleExtractionJobType: JobType.ExtractSubtitles,
        gridThumbnailJobType: JobType.GenerateGridThumbnail,
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
            new CapabilityConsumption(),
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
