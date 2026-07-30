using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media;

/// <summary>Defines the music-artist grouping kind and its shared-root behavior.</summary>
public sealed class MusicArtistEntityKindDefinition() : RootEntityKindDefinition<MusicArtist>(
    EntityKind.MusicArtist,
    "music-artist",
    "Music Artist",
    "Artists",
    EntityKindCategory.Media,
    EntityStorageShape.Folder,
    new EntityKindPresentation(
        EntityKindIcon.Artist,
        EntityKindIcon.Audio,
        1,
        1,
        EntityAccentHue.Violet,
        EntityAccentHue.Magenta,
        EntityArtworkFit.Cover),
    static root => new MusicArtist(root.Id, root.Title),
    defaultCapabilities: static () => [new CapabilityCredits()],
    enumeratesIdentifyChildren: true,
    supportsFileDeletion: true,
    autoIdentifySelector: AutoIdentifySelectorKind.Audio) {
    /// <inheritdoc />
    public override bool OwnsMetadataRelationships => true;

    /// <inheritdoc />
    public override IReadOnlyList<RequestKindDescriptor> RequestKinds =>
    [
        new(RequestMediaKind.Artist, "Artist", "Artists", "album", EntityKind.MusicArtist, EntityKind.MusicArtist,
            ProfileEntityKind: EntityKind.AudioLibrary,
            LibraryRootMediaCapability: LibraryRootMediaCapability.ScanAudio,
            ReviewSelection: RequestReviewSelection.DirectChildren,
            IsContainer: true, ChildKind: RequestMediaKind.Album, Committable: true,
            AcquisitionKind: EntityKind.AudioLibrary, DeferChildPhantomHydration: true)
    ];
}

/// <summary>
/// Domain model for a music artist or band: a folder-backed grouping that gathers an
/// artist's albums (<see cref="AudioLibrary"/> children) under one heading, much like a
/// <see cref="Gallery"/> groups images. Carries its own metadata and band members, which
/// are stored as person credits (<see cref="CapabilityCredits"/>) where the credit label
/// holds the member's role, e.g. "Drummer" or "Composer".
/// </summary>
public sealed class MusicArtist : Entity<MusicArtistEntityKindDefinition> {
    /// <summary>
    /// Creates a music artist grouping.
    /// </summary>
    /// <param name="id">Stable entity identity.</param>
    /// <param name="title">Display name of the artist or band.</param>
    /// <param name="capabilities">Optional initial capability set.</param>
    public MusicArtist(Guid id, string title, IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
    }

}
