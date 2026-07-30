using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using ContractCapability = Prismedia.Contracts.Entities.EntityCapability;
using EmbeddedAudioMetadataDocumentCapability = Prismedia.Contracts.Entities.EmbeddedAudioMetadataCapability;

namespace Prismedia.Domain.Media;

/// <summary>Defines the playable audio-track kind and its default playback capability.</summary>
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
        EntityArtworkFit.Cover),
    defaultCapabilities: static () => [new CapabilityPlayback()],
    supportsFileDeletion: true,
    autoIdentifySelector: AutoIdentifySelectorKind.Audio) {
    /// <inheritdoc />
    public override bool OwnsMetadataRelationships => true;

    /// <inheritdoc />
    public override IReadOnlyList<Type> ProjectedCapabilityTypes => [typeof(EmbeddedAudioMetadataDocumentCapability)];

    /// <inheritdoc />
    public override IReadOnlyList<RequestKindDescriptor> RequestKinds =>
    [
        new(RequestMediaKind.Track, "Track", "Tracks", null, EntityKind.AudioTrack, EntityKind.AudioTrack,
            ProfileEntityKind: EntityKind.AudioLibrary,
            LibraryRootMediaCapability: LibraryRootMediaCapability.ScanAudio,
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
    /// Records a playback event on the attached playback capability.
    /// </summary>
    public void MarkPlayed(TimeSpan resumeTime, DateTimeOffset playedAt) {
        var playback = RequireCapability<CapabilityPlayback>();
        playback.MarkPlayed(resumeTime, playedAt);
    }
}
