namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed typed identity for entity kinds owned by the domain model. Stable codes, metadata,
/// construction, and optional facets live in discovered <see cref="EntityKindDefinition"/>
/// implementations rather than parallel enum attributes and registry tables.
/// </summary>
public enum EntityKind {
    /// <summary>Generic audio media root.</summary>
    Audio,

    /// <summary>Audio library, album, audiobook, or podcast grouping.</summary>
    AudioLibrary,

    /// <summary>Playable audio track.</summary>
    AudioTrack,

    /// <summary>Prose book whose chapters are navigation markers within the published work.</summary>
    Book,

    /// <summary>Structural book volume.</summary>
    BookVolume,

    /// <summary>Structural book chapter.</summary>
    BookChapter,

    /// <summary>Structural book page.</summary>
    BookPage,

    /// <summary>Released serialized-comic chapter, issue, special, or one-shot.</summary>
    ComicInstallment,

    /// <summary>Serialized-comic title or western comic run.</summary>
    ComicSeries,

    /// <summary>Optional collected or thematic serialized-comic grouping.</summary>
    ComicVolume,

    /// <summary>User collection.</summary>
    Collection,

    /// <summary>Image gallery.</summary>
    Gallery,

    /// <summary>Single image.</summary>
    Image,

    /// <summary>
    /// Music artist or band: a folder-backed grouping that gathers an artist's albums
    /// (<see cref="EntityKind.AudioLibrary"/> children) under one heading, like a gallery
    /// groups images.
    /// </summary>
    MusicArtist,

    /// <summary>
    /// Book author: a folder-backed grouping that gathers an author's books
    /// (<see cref="EntityKind.Book"/> children) under one heading, mirroring how
    /// <see cref="EntityKind.MusicArtist"/> groups albums.
    /// </summary>
    BookAuthor,

    /// <summary>Person taxonomy entity.</summary>
    Person,

    /// <summary>Directly playable single-film video release.</summary>
    Movie,

    /// <summary>Studio, publisher, label, or production group.</summary>
    Studio,

    /// <summary>Tag taxonomy entity.</summary>
    Tag,

    /// <summary>Playable video media item.</summary>
    Video,

    /// <summary>Directly playable episodic video file.</summary>
    VideoEpisode,

    /// <summary>Video series grouping.</summary>
    VideoSeries,

    /// <summary>Structural video season.</summary>
    VideoSeason
}
