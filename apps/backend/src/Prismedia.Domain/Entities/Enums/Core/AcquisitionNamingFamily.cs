namespace Prismedia.Domain.Entities;

/// <summary>Renderer and validation behavior for an acquisition profile naming template.</summary>
public enum AcquisitionNamingFamily {
    /// <summary>Book imports use their dedicated renderer and only require a non-blank template.</summary>
    [Code("book")]
    Book,

    /// <summary>Movie imports require a folder/file template.</summary>
    [Code("movie")]
    Movie,

    /// <summary>Series imports require series/season/episode segments.</summary>
    [Code("television")]
    Television,

    /// <summary>Album imports require artist/album folders.</summary>
    [Code("music")]
    Music
}
