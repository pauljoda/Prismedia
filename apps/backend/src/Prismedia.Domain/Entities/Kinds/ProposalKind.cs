namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of entity kinds a plugin identify proposal can target. It mirrors
/// <see cref="EntityKind"/> code-for-code and adds <see cref="VideoEpisode"/>: a plugin-protocol
/// distinction for a leaf episode inside a series/season that Prismedia persists as an ordinary
/// <see cref="EntityKind.Video"/>. The structural matching/dedup logic relies on keeping that
/// distinction, so proposals carry <see cref="ProposalKind"/> rather than <see cref="EntityKind"/>.
/// Collapse a proposal kind back to the entity kind Prismedia stores it as with
/// <see cref="ProposalKindExtensions.ToEntityKind"/>.
/// </summary>
public enum ProposalKind {
    /// <summary>Generic audio media root.</summary>
    [Code("audio")]
    Audio,

    /// <summary>Audio library, album, audiobook, or podcast grouping.</summary>
    [Code("audio-library")]
    AudioLibrary,

    /// <summary>Playable audio track.</summary>
    [Code("audio-track")]
    AudioTrack,

    /// <summary>Book, comic, manga, or other page-based media item.</summary>
    [Code("book")]
    Book,

    /// <summary>Structural book volume.</summary>
    [Code("book-volume")]
    BookVolume,

    /// <summary>Structural book chapter.</summary>
    [Code("book-chapter")]
    BookChapter,

    /// <summary>Structural book page.</summary>
    [Code("book-page")]
    BookPage,

    /// <summary>Book author or writer grouping (mirrors <see cref="EntityKind.BookAuthor"/>).</summary>
    [Code("book-author")]
    BookAuthor,

    /// <summary>User collection.</summary>
    [Code("collection")]
    Collection,

    /// <summary>Image gallery.</summary>
    [Code("gallery")]
    Gallery,

    /// <summary>Single image.</summary>
    [Code("image")]
    Image,

    /// <summary>Music artist or band grouping.</summary>
    [Code("music-artist")]
    MusicArtist,

    /// <summary>Person taxonomy entity.</summary>
    [Code("person")]
    Person,

    /// <summary>Single-film video release grouping.</summary>
    [Code("movie")]
    Movie,

    /// <summary>Studio, publisher, label, or production group.</summary>
    [Code("studio")]
    Studio,

    /// <summary>Tag taxonomy entity.</summary>
    [Code("tag")]
    Tag,

    /// <summary>Playable video media item.</summary>
    [Code("video")]
    Video,

    /// <summary>Video series grouping.</summary>
    [Code("video-series")]
    VideoSeries,

    /// <summary>Structural video season.</summary>
    [Code("video-season")]
    VideoSeason,

    /// <summary>
    /// Leaf video episode within a series/season. Has no <see cref="EntityKind"/> of its own —
    /// Prismedia stores episodes as <see cref="EntityKind.Video"/>; this kind only exists in the
    /// identify protocol so providers can mark a child as a playable leaf rather than a container.
    /// </summary>
    [Code("video-episode")]
    VideoEpisode
}
