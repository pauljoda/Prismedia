namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of entity kinds owned by the domain model. Each member declares its stable
/// code and taxonomy metadata inline; <see cref="EntityKindRegistry"/> builds itself from
/// these attributes.
/// </summary>
public enum EntityKind {
    /// <summary>Generic audio media root.</summary>
    [Code("audio")]
    [EntityKindMeta(EntityKindCategory.Media, EntityStorageShape.File, "Audio", supportsFileDeletion: true)]
    Audio,

    /// <summary>Audio library, album, audiobook, or podcast grouping.</summary>
    [Code("audio-library")]
    [EntityKindMeta(EntityKindCategory.Media, EntityStorageShape.Folder, "Audio Libraries", typeof(Prismedia.Domain.Media.AudioLibrary), enumeratesIdentifyChildren: true, supportsFileDeletion: true)]
    AudioLibrary,

    /// <summary>Playable audio track.</summary>
    [Code("audio-track")]
    [EntityKindMeta(EntityKindCategory.Media, EntityStorageShape.File, "Audio Tracks", typeof(Prismedia.Domain.Media.AudioTrack), supportsFileDeletion: true)]
    AudioTrack,

    /// <summary>Book, comic, manga, or other page-based media item.</summary>
    [Code("book")]
    [EntityKindMeta(EntityKindCategory.Media, EntityStorageShape.Archive, "Books", typeof(Prismedia.Domain.Media.Book), enumeratesIdentifyChildren: true, supportsFileDeletion: true)]
    Book,

    /// <summary>Structural book volume.</summary>
    [Code("book-volume")]
    [EntityKindMeta(EntityKindCategory.Media, EntityStorageShape.None, "Volumes", typeof(Prismedia.Domain.Media.BookVolume), enumeratesIdentifyChildren: true, supportsFileDeletion: true)]
    BookVolume,

    /// <summary>Structural book chapter.</summary>
    [Code("book-chapter")]
    [EntityKindMeta(EntityKindCategory.Media, EntityStorageShape.None, "Chapters", typeof(Prismedia.Domain.Media.BookChapter))]
    BookChapter,

    /// <summary>Structural book page.</summary>
    [Code("book-page")]
    [EntityKindMeta(EntityKindCategory.Media, EntityStorageShape.ArchiveEntry, "Pages", typeof(Prismedia.Domain.Media.BookPage))]
    BookPage,

    /// <summary>User collection.</summary>
    [Code("collection")]
    [EntityKindMeta(EntityKindCategory.Collection, EntityStorageShape.None, "Collections", typeof(Prismedia.Domain.Media.Collection))]
    Collection,

    /// <summary>Image gallery.</summary>
    [Code("gallery")]
    [EntityKindMeta(EntityKindCategory.Media, EntityStorageShape.Folder, "Galleries", typeof(Prismedia.Domain.Media.Gallery), supportsFileDeletion: true)]
    Gallery,

    /// <summary>Single image.</summary>
    [Code("image")]
    [EntityKindMeta(EntityKindCategory.Media, EntityStorageShape.File, "Images", typeof(Prismedia.Domain.Media.Image), supportsFileDeletion: true)]
    Image,

    /// <summary>
    /// Music artist or band: a folder-backed grouping that gathers an artist's albums
    /// (<see cref="EntityKind.AudioLibrary"/> children) under one heading, like a gallery
    /// groups images.
    /// </summary>
    [Code("music-artist")]
    [EntityKindMeta(EntityKindCategory.Media, EntityStorageShape.Folder, "Artists", typeof(Prismedia.Domain.Media.MusicArtist), enumeratesIdentifyChildren: true, supportsFileDeletion: true)]
    MusicArtist,

    /// <summary>
    /// Book author: a folder-backed grouping that gathers an author's books
    /// (<see cref="EntityKind.Book"/> children) under one heading, mirroring how
    /// <see cref="EntityKind.MusicArtist"/> groups albums.
    /// </summary>
    [Code("book-author")]
    [EntityKindMeta(EntityKindCategory.Media, EntityStorageShape.Folder, "Authors", typeof(Prismedia.Domain.Media.BookAuthor), enumeratesIdentifyChildren: true, supportsFileDeletion: true)]
    BookAuthor,

    /// <summary>Person taxonomy entity.</summary>
    [Code("person")]
    [EntityKindMeta(EntityKindCategory.Taxonomy, EntityStorageShape.None, "People", typeof(Prismedia.Domain.Taxonomy.Person))]
    Person,

    /// <summary>Single-film video release grouping with one playable video child.</summary>
    [Code("movie")]
    [EntityKindMeta(EntityKindCategory.Media, EntityStorageShape.Folder, "Movies", typeof(Prismedia.Domain.Media.Movie), supportsFileDeletion: true)]
    Movie,

    /// <summary>Studio, publisher, label, or production group.</summary>
    [Code("studio")]
    [EntityKindMeta(EntityKindCategory.Taxonomy, EntityStorageShape.None, "Studios", typeof(Prismedia.Domain.Taxonomy.Studio))]
    Studio,

    /// <summary>Tag taxonomy entity.</summary>
    [Code("tag")]
    [EntityKindMeta(EntityKindCategory.Taxonomy, EntityStorageShape.None, "Tags", typeof(Prismedia.Domain.Taxonomy.Tag))]
    Tag,

    /// <summary>Playable video media item.</summary>
    [Code("video")]
    [EntityKindMeta(EntityKindCategory.Media, EntityStorageShape.File, "Videos", typeof(Prismedia.Domain.Media.Video), supportsFileDeletion: true)]
    Video,

    /// <summary>Video series grouping.</summary>
    [Code("video-series")]
    [EntityKindMeta(EntityKindCategory.Media, EntityStorageShape.Folder, "Series", typeof(Prismedia.Domain.Media.VideoSeries), enumeratesIdentifyChildren: true, supportsFileDeletion: true)]
    VideoSeries,

    /// <summary>Structural video season.</summary>
    [Code("video-season")]
    [EntityKindMeta(EntityKindCategory.Media, EntityStorageShape.Folder, "Seasons", typeof(Prismedia.Domain.Media.VideoSeason), enumeratesIdentifyChildren: true, supportsFileDeletion: true)]
    VideoSeason
}
