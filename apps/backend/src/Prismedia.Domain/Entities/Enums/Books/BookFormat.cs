namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of physical formats a book entity can take. The format determines which
/// reader mechanics and detail presentation the application uses, independent of the
/// <see cref="BookType"/> genre label. Comics keep the existing image-archive behavior;
/// EPUB and PDF are single self-contained files whose chapters live inside the file.
/// </summary>
public enum BookFormat {
    /// <summary>One or more image archives (.cbz/.zip), read page by page as images.</summary>
    [Code("image-archive")]
    ImageArchive,

    /// <summary>A single reflowable EPUB file, read with a reflowable text reader.</summary>
    [Code("epub")]
    Epub,

    /// <summary>A single fixed-layout PDF file, read with a paged canvas reader.</summary>
    [Code("pdf")]
    Pdf,

    /// <summary>An audio-only book whose playable payload is exposed through ordered audio-track children.</summary>
    [Code("audio")]
    Audio
}
