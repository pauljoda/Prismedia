using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using ContractCapability = Prismedia.Contracts.Entities.EntityCapability;
using EmbeddedAudioMetadataDocumentCapability = Prismedia.Contracts.Entities.EmbeddedAudioMetadataCapability;

namespace Prismedia.Domain.Media;

/// <summary>Defines the playable audio-track kind and its default consumption capability.</summary>
public sealed class AudioTrackEntityKindDefinition() : EntityKindDefinition<AudioTrack>(
    EntityKind.AudioTrack,
    "audio-track",
    "Audio Track",
    "Audio Tracks",
    EntityKindCategory.Media,
    EntityStorageShape.File,
    new EntityKindPresentation(
        EntityKindIcon.Track,
        EntityKindIcon.Audio,
        1,
        1,
        EntityAccentHue.Violet,
        EntityAccentHue.Magenta,
        EntityArtworkFit.Cover,
        borrowArtworkFromParentKinds: [EntityKind.AudioLibrary]),
    new EntityKindNavigation(EntityKind.AudioTrack, "tracks", "/tracks", "/audio/tracks/{id}"),
    new EntityKindSearch(12),
    new EntityKindBehavior(
        identification: new(AutoIdentifySelectorKind.Audio),
        processing: new EntityProcessingPolicy(
            assetFamily: GeneratedAssetFamily.AudioTrack,
            probeJobType: JobType.ProbeAudio,
            probeRequiresAutomaticMetadata: true,
            fingerprintJobType: JobType.FingerprintAudio,
            previewJobType: JobType.GenerateAudioWaveform,
            previewRequiresAutomaticGeneration: true,
            generatedFileRoles: [EntityFileRole.Waveform]),
        engagement: new(
            EntityEngagementMode.Playback,
            defaultActivityKind: ConsumptionActivityKind.Listening),
        browse: new(excludesWantedByDefault: true),
        catalogVisibility: new(
            parentExclusions: [new(EntityKind.Book,
                EntityCatalogSurface.Discovery |
                EntityCatalogSurface.KindBrowse |
                EntityCatalogSurface.Collection |
                EntityCatalogSurface.Statistics)]),
        libraryVisibility: EntityLibraryVisibilityPolicy.AncestorRoot,
        supportsFileDeletion: true,
        mediaQualityFamily: EntityMediaQualityFamily.Audio),
    defaultCapabilities: static () => [new CapabilityConsumption()]), IPlayableAudioKindDefinition {
    /// <inheritdoc />
    public override EntityProgressTopology ProgressTopology => EntityProgressTopology.Work(
        EntityKind.Book,
        fallsBackToDirect: true);

    /// <inheritdoc />
    public override EntityStructurePolicy StructurePolicy { get; } =
        EntityStructurePolicy.RootOrChildOf(EntityKind.AudioLibrary, EntityKind.Book);

    /// <inheritdoc />
    public override bool OwnsMetadataRelationships => true;

    /// <inheritdoc />
    public override IReadOnlyList<Type> ProjectedCapabilityTypes => [typeof(EmbeddedAudioMetadataDocumentCapability)];

    /// <inheritdoc />
    public override IReadOnlyList<RequestKindDescriptor> RequestKinds =>
    [
        new(RequestMediaKind.Track, "Track", "Tracks", null, EntityKind.AudioTrack, EntityKind.AudioTrack,
            ProfileEntityKind: EntityKind.AudioLibrary,
            ReviewSelection: RequestReviewSelection.Root,
            IsContainer: false, ChildKind: null, Committable: true,
            AcquisitionKind: EntityKind.AudioTrack, Discoverable: false, AcquireFromEntity: true)
    ];

    /// <inheritdoc />
    protected override IReadOnlyList<ContractCapability> ProjectCapabilities(
        AudioTrack entity,
        EntityKindProjectionContext context) =>
        [new EmbeddedAudioMetadataDocumentCapability(entity.EmbeddedArtist, entity.EmbeddedAlbum)];
}

/// <summary>
/// Domain model for a playable audio track.
/// </summary>
public sealed class AudioTrack : Entity<AudioTrackEntityKindDefinition> {
    public AudioTrack(
        Guid id,
        string title,
        string? embeddedArtist,
        string? embeddedAlbum,
        IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
        EmbeddedArtist = embeddedArtist;
        EmbeddedAlbum = embeddedAlbum;
    }

    public string? EmbeddedArtist { get; private set; }
    public string? EmbeddedAlbum { get; private set; }

    /// <summary>
    /// Records an access event on the attached consumption capability.
    /// </summary>
    public void MarkPlayed(TimeSpan resumeTime, DateTimeOffset playedAt) {
        var consumption = RequireCapability<CapabilityConsumption>();
        consumption.RecordAccessed(playedAt);
        consumption.RecordResume(resumeTime, playedAt);
    }
}
