namespace Prismedia.Application.Jobs;

/// <summary>
/// Immutable selection of media families covered by a library scan operation.
/// Library-root settings, file mutations, and the recurring scheduler use this value to carry scan
/// intent without relying on the positional order of several boolean flags.
/// </summary>
public readonly record struct LibraryScanSelection(
    bool Videos,
    bool Images,
    bool Audio,
    bool Books,
    bool Comics) {
    /// <summary>Represents a selection that does not request any scan work.</summary>
    public static LibraryScanSelection None => new();

    /// <summary>Gets whether this selection does not include any media family.</summary>
    public bool IsEmpty => !Videos && !Images && !Audio && !Books && !Comics;

    /// <summary>
    /// Combines this selection with another selection, retaining every media family enabled by either.
    /// </summary>
    /// <param name="other">The selection to merge with this selection.</param>
    /// <returns>A selection containing the union of both selections.</returns>
    public LibraryScanSelection Union(LibraryScanSelection other) => new(
        Videos: Videos || other.Videos,
        Images: Images || other.Images,
        Audio: Audio || other.Audio,
        Books: Books || other.Books,
        Comics: Comics || other.Comics);
}
