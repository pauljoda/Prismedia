namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of active book-engagement modes recorded by reader and audiobook heartbeats.
/// </summary>
public enum BookActivityKind {
    /// <summary>Time actively spent in a text, page, comic, EPUB, or PDF reader.</summary>
    [Code("reading")]
    Reading,

    /// <summary>Time actively spent listening to an audiobook rendition.</summary>
    [Code("listening")]
    Listening
}
