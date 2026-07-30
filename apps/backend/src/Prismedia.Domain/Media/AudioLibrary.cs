using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

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
        EntityAccentHue.Magenta),
    static root => new AudioLibrary(root.Id, root.Title),
    enumeratesIdentifyChildren: true,
    supportsFileDeletion: true,
    autoIdentifySelector: AutoIdentifySelectorKind.Audio) {
    /// <inheritdoc />
    public override bool OwnsMetadataRelationships => true;

    /// <inheritdoc />
    public override IReadOnlyList<RequestKindDescriptor> RequestKinds =>
    [
        new(RequestMediaKind.Album, "Album", "Albums", "track", EntityKind.AudioLibrary, EntityKind.AudioLibrary,
            ProfileEntityKind: EntityKind.AudioLibrary,
            LibraryRootMediaCapability: LibraryRootMediaCapability.ScanAudio,
            ReviewSelection: RequestReviewSelection.Root,
            IsContainer: false, ChildKind: RequestMediaKind.Track, Committable: true,
            AcquisitionKind: EntityKind.AudioLibrary, MaterializeChildPhantoms: true)
    ];
}

/// <summary>
/// Domain model for an album, audiobook, podcast, or other audio grouping.
/// </summary>
public sealed class AudioLibrary : Entity<AudioLibraryEntityKindDefinition> {
    public AudioLibrary(Guid id, string title, IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
    }
}
