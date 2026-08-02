using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using ThumbnailMetaIcons = Prismedia.Contracts.Entities.EntityThumbnailMetaIcons;

namespace Prismedia.Domain.Media;

/// <summary>Defines the audio-library kind, defaults, and shared-root construction.</summary>
public sealed class AudioLibraryEntityKindDefinition() : RootEntityKindDefinition<AudioLibrary>(
    EntityKind.AudioLibrary,
    "audio-library",
    "Audio Library",
    "Audio Libraries",
    EntityKindCategory.Media,
    EntityStorageShape.Folder,
    new EntityKindPresentation(
        EntityKindIcon.Album,
        EntityKindIcon.Audio,
        1,
        1,
        EntityAccentHue.Violet,
        EntityAccentHue.Magenta,
        EntityArtworkFit.Cover),
    new EntityKindNavigation(EntityKind.AudioLibrary, "albums", "/audio", "/audio/{id}"),
    new EntityKindSearch(10),
    static root => new AudioLibrary(root.Id, root.Title),
    behavior: new EntityKindBehavior(
        identification: new(
            AutoIdentifySelectorKind.Audio,
            enumeratesChildren: true,
            allowsParentedAutoIdentifyRoot: true,
            usesParentExternalIdentityContext: true),
        manualAcquisition: EntityManualAcquisitionPolicy.UploadAndReplacement,
        engagement: new(
            EntityEngagementMode.Playback,
            defaultActivityKind: ConsumptionActivityKind.Listening),
        libraryVisibility: EntityLibraryVisibilityPolicy.DirectRoot,
        supportsFileDeletion: true,
        mediaQualityFamily: EntityMediaQualityFamily.Audio),
    defaultCapabilities: static () => [new CapabilityConsumption()]) {
    /// <inheritdoc />
    public override EntityProgressTopology ProgressTopology => EntityProgressTopology.None;

    /// <inheritdoc />
    public override EntityStructurePolicy StructurePolicy { get; } =
        EntityStructurePolicy.RootOrChildOf(EntityKind.MusicArtist, EntityKind.AudioLibrary);

    /// <inheritdoc />
    public override AcquisitionAncestorContextRole AcquisitionAncestorContextRole =>
        AcquisitionAncestorContextRole.Series;

    /// <inheritdoc />
    public override bool IsFulfilledBySourceBackedSubtree => true;

    /// <inheritdoc />
    public override bool OwnsMetadataRelationships => true;

    /// <inheritdoc />
    public override IReadOnlyList<EntityStructuralCountDefinition> StructuralThumbnailCounts =>
        [new(EntityKind.AudioTrack, 1, ThumbnailMetaIcons.Track)];

    /// <inheritdoc />
    public override IReadOnlyList<RequestKindDescriptor> RequestKinds =>
    [
        new(RequestMediaKind.Album, "Album", "Albums", "track", EntityKind.AudioLibrary, EntityKind.AudioLibrary,
            ProfileEntityKind: EntityKind.AudioLibrary,
            ReviewSelection: RequestReviewSelection.Root,
            IsContainer: false, ChildKind: RequestMediaKind.Track, Committable: true,
            AcquisitionKind: EntityKind.AudioLibrary, MaterializeChildPhantoms: true)
    ];

    /// <inheritdoc />
    public override AcquisitionProfileDefinition AcquisitionProfile { get; } = new(
        "Music (albums)",
        3,
        LibraryRootMediaCapability.ScanAudio,
        [
            EntityDateType.Release,
            EntityDateType.DigitalRelease,
            EntityDateType.PhysicalRelease
        ],
        "{Artist}/{Album}",
        "{Artist} {Album} {Year} — 2 segments: artist/album folder (track files keep their release names)",
        AcquisitionNamingFamily.Music,
        AcquisitionCheckpointProtocol.Placement);
}

/// <summary>
/// Domain model for an album, audiobook, podcast, or other audio grouping.
/// </summary>
public sealed class AudioLibrary : Entity<AudioLibraryEntityKindDefinition> {
    public AudioLibrary(Guid id, string title, IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
    }
}
