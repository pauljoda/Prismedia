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
/// Reads descriptive metadata embedded in single-file books (EPUB/PDF), normalized into the
/// shared <see cref="ComicInfoMetadata"/> shape so the scan applies it through one path.
/// </summary>
public interface IBookFileMetadataReader {
    /// <summary>
    /// Reads metadata (title, authors, description, publisher, language, series, tags) from a
    /// single-file book, or returns null when the file cannot be read.
    /// </summary>
    /// <param name="sourcePath">Absolute path to the EPUB/PDF file.</param>
    /// <param name="format">Book format selecting the parser.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ComicInfoMetadata?> ReadAsync(string sourcePath, Prismedia.Domain.Entities.BookFormat format, CancellationToken cancellationToken);
}

/// <summary>
/// Extracts a cover/first-page image from a single-file book to a temporary image file that
/// the thumbnail pipeline can resize. Returns null when no cover can be produced.
/// </summary>
public interface IBookCoverImageExtractor {
    /// <summary>
    /// Writes the book's cover image (EPUB embedded cover, or rendered PDF first page) to a
    /// temporary file and returns its path. The caller owns deletion of the returned file.
    /// </summary>
    /// <param name="sourcePath">Absolute path to the EPUB/PDF file.</param>
    /// <param name="format">Book format selecting the extraction strategy.</param>
    /// <param name="entityId">Book entity id, used to name the temp file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string?> ExtractCoverToTempAsync(string sourcePath, Prismedia.Domain.Entities.BookFormat format, Guid entityId, CancellationToken cancellationToken);
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
    /// Applies one scan batch of video sidecar metadata with the same apply-if-missing semantics
    /// as the single-entity overload. Implementations preload the batch's persisted state in a
    /// handful of set queries and save once, instead of several reads and one save per video —
    /// the dominant cost of large library scans. The default falls back to per-item application.
    /// </summary>
    /// <param name="items">Batch of videos with their sidecar metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    async Task ApplyVideoSidecarMetadataBatchAsync(
        IReadOnlyList<VideoSidecarApplyItem> items,
        CancellationToken cancellationToken) {
        foreach (var item in items) {
            await ApplyVideoSidecarMetadataAsync(
                item.EntityId,
                item.Metadata,
                item.FallbackTitle,
                item.MarkNsfw,
                cancellationToken);
        }
    }

    /// <summary>
    /// Marks the end of one scan persistence batch. Implementations backed by a job-lifetime
    /// unit of work release accumulated tracked state here so throughput stays flat across a
    /// large scan instead of degrading as the tracked graph grows. Callers invoke it only after
    /// the batch's writes have been saved. The default is a no-op.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CompleteScanBatchAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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

/// <summary>One video in a sidecar-metadata apply batch.</summary>
/// <param name="EntityId">Video entity receiving metadata.</param>
/// <param name="Metadata">Sidecar metadata discovered for the video.</param>
/// <param name="FallbackTitle">Title inferred from the source path before sidecar metadata was considered.</param>
/// <param name="MarkNsfw">Whether linked taxonomy should be marked NSFW.</param>
public sealed record VideoSidecarApplyItem(
    Guid EntityId,
    VideoSidecarMetadata Metadata,
    string FallbackTitle,
    bool MarkNsfw);

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
