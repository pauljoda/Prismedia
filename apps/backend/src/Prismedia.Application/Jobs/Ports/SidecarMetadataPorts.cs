namespace Prismedia.Application.Jobs.Ports;

/// <summary>
/// Reads metadata sidecars located next to video files.
/// </summary>
public interface IVideoSidecarMetadataReader {
    /// <summary>
    /// Reads supported sidecar metadata for a video file, or returns null when no metadata exists.
    /// </summary>
    /// <param name="videoFilePath">Absolute path to the video file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<VideoSidecarMetadata?> ReadAsync(string videoFilePath, CancellationToken cancellationToken);
}

/// <summary>
/// Reads ComicInfo.xml metadata embedded in comic book archives.
/// </summary>
public interface IComicInfoMetadataReader {
    /// <summary>
    /// Reads ComicInfo.xml metadata from a ZIP/CBZ archive, or returns null when no metadata exists.
    /// </summary>
    /// <param name="archivePath">Absolute path to the archive file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ComicInfoMetadata?> ReadAsync(string archivePath, CancellationToken cancellationToken);
}

/// <summary>
/// Persists scanner-discovered descriptive metadata into the entity model.
/// </summary>
public interface IScanMetadataPersistence {
    /// <summary>
    /// Applies video sidecar metadata without clearing existing user or provider metadata.
    /// </summary>
    /// <param name="entityId">Video entity receiving metadata.</param>
    /// <param name="metadata">Sidecar metadata discovered for the video.</param>
    /// <param name="fallbackTitle">Title inferred from the source path before sidecar metadata was considered.</param>
    /// <param name="markNsfw">Whether linked taxonomy should be marked NSFW.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ApplyVideoSidecarMetadataAsync(
        Guid entityId,
        VideoSidecarMetadata metadata,
        string fallbackTitle,
        bool markNsfw,
        CancellationToken cancellationToken);

    /// <summary>
    /// Applies ComicInfo.xml metadata without clearing existing user or provider metadata.
    /// </summary>
    /// <param name="bookEntityId">Book/comic entity receiving metadata.</param>
    /// <param name="metadata">ComicInfo.xml metadata discovered for the book.</param>
    /// <param name="markNsfw">Whether linked taxonomy should be marked NSFW.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ApplyComicInfoMetadataAsync(
        Guid bookEntityId,
        ComicInfoMetadata metadata,
        bool markNsfw,
        CancellationToken cancellationToken);
}

/// <summary>
/// Metadata read from video NFO and JSON sidecars.
/// </summary>
public sealed record VideoSidecarMetadata {
    /// <summary>Display title supplied by the sidecar.</summary>
    public string? Title { get; init; }

    /// <summary>Description, plot, or synopsis supplied by the sidecar.</summary>
    public string? Description { get; init; }

    /// <summary>Release, air, or upload date supplied by the sidecar.</summary>
    public string? Date { get; init; }

    /// <summary>Studio, uploader, channel, creator, or artist supplied by the sidecar.</summary>
    public string? Studio { get; init; }

    /// <summary>Canonical or reference URLs supplied by the sidecar.</summary>
    public IReadOnlyList<string> Urls { get; init; } = [];

    /// <summary>Tag names supplied by the sidecar.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Credited person names supplied by the sidecar.</summary>
    public IReadOnlyList<string> Performers { get; init; } = [];

    /// <summary>Optional duration in seconds supplied by the sidecar.</summary>
    public double? DurationSeconds { get; init; }
}

/// <summary>
/// Metadata read from ComicInfo.xml inside ZIP/CBZ comic archives.
/// </summary>
public sealed record ComicInfoMetadata {
    /// <summary>Issue or chapter title.</summary>
    public string? Title { get; init; }

    /// <summary>Series title.</summary>
    public string? Series { get; init; }

    /// <summary>Issue/chapter number.</summary>
    public string? Number { get; init; }

    /// <summary>Total issue/chapter count when supplied.</summary>
    public int? Count { get; init; }

    /// <summary>Volume number when supplied.</summary>
    public int? Volume { get; init; }

    /// <summary>Summary or description text.</summary>
    public string? Summary { get; init; }

    /// <summary>Publication date with the precision available in the source file.</summary>
    public string? Date { get; init; }

    /// <summary>Publisher or imprint.</summary>
    public string? Publisher { get; init; }

    /// <summary>Reference URLs.</summary>
    public IReadOnlyList<string> Urls { get; init; } = [];

    /// <summary>Declared page count.</summary>
    public int? PageCount { get; init; }

    /// <summary>Language code from ComicInfo.xml.</summary>
    public string? Language { get; init; }

    /// <summary>Format value from ComicInfo.xml.</summary>
    public string? Format { get; init; }

    /// <summary>Manga direction/classification value.</summary>
    public string? Manga { get; init; }

    /// <summary>Age rating value.</summary>
    public string? AgeRating { get; init; }

    /// <summary>Creator names from writer, penciller, inker, and related fields.</summary>
    public IReadOnlyList<string> Creators { get; init; } = [];

    /// <summary>Tag names from genre, tags, characters, arcs, and rating fields.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>True when ComicInfo.xml indicates adult-oriented content.</summary>
    public bool MarksNsfw { get; init; }
}
