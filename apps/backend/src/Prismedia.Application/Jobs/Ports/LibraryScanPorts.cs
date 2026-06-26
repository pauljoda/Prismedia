using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Ports;

/// <summary>Reads library roots, scan settings, and root-scoped exclusions used by scan handlers.</summary>
public interface ILibraryScanRootPersistence {
    Task<LibraryRootData?> GetLibraryRootAsync(Guid rootId, CancellationToken cancellationToken);
    Task<IReadOnlyList<LibraryRootData>> GetEnabledRootsAsync(CancellationToken cancellationToken);
    Task<LibrarySettingsData> GetSettingsAsync(CancellationToken cancellationToken);
    Task UpdateRootLastScannedAsync(Guid rootId, CancellationToken cancellationToken);
    Task<IReadOnlySet<string>> GetExcludedPathsForRootAsync(Guid rootId, CancellationToken cancellationToken);
    Task<int> RemoveEntitiesInExcludedPathsAsync(Guid rootId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes media source entities whose source file paths are no longer under any configured
    /// library root. This recovers data left behind by older root deletions that removed the root row
    /// before media cleanup could run. Disabled roots still count as configured roots so their hidden
    /// media can be restored by re-enabling them.
    /// </summary>
    Task<int> RemoveEntitiesOutsideLibraryRootsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Deletes tags that nothing references (no inbound relationship links). Returns the count
    /// removed. Lives here, on the scan-root port, because orphan-tag cleanup runs once at the end of
    /// every scan kind — not just the video scan — so the base scan handler can invoke it.
    /// </summary>
    Task<int> RemoveOrphanTagsAsync(CancellationToken cancellationToken);
}

/// <summary>Video scan persistence operations for discovered files and stale cleanup.</summary>
public interface IVideoScanPersistence {
    Task<Guid> UpsertVideoAsync(string filePath, string title, Guid libraryRootId, bool isNsfw, CancellationToken cancellationToken);
    Task<int> RemoveStaleVideosByRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken);
    Task<int> RemoveStaleMoviesByRootAsync(Guid rootId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken);
    Task<int> RemoveOrphanSeriesAndSeasonsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Upserts a batch of video entities in a single database round-trip,
    /// returning the entity ID for each input file path in the same order.
    /// </summary>
    Task<IReadOnlyList<Guid>> UpsertVideosBatchAsync(
        IReadOnlyList<VideoUpsertItem> items, CancellationToken cancellationToken);
}

/// <summary>Image and gallery scan persistence operations for discovered files and stale cleanup.</summary>
public interface IImageGalleryScanPersistence {
    Task<Guid> UpsertImageAsync(string filePath, string title, Guid? galleryEntityId, long? sizeBytes, int sortOrder, bool isNsfw, CancellationToken cancellationToken);
    Task<Guid> UpsertGalleryAsync(string folderPath, string title, Guid libraryRootId, Guid? parentGalleryEntityId, int sortOrder, bool isNsfw, CancellationToken cancellationToken);

    /// <summary>
    /// Upserts discovered gallery folders as one persistence unit, returning IDs in input order.
    /// Parent IDs must already be known, so callers batch by hierarchy depth.
    /// </summary>
    Task<IReadOnlyList<Guid>> UpsertGalleriesBatchAsync(
        IReadOnlyList<GalleryUpsertItem> items, CancellationToken cancellationToken);

    /// <summary>
    /// Upserts discovered image files as one persistence unit, returning IDs in input order.
    /// </summary>
    Task<IReadOnlyList<Guid>> UpsertImagesBatchAsync(
        IReadOnlyList<ImageUpsertItem> items, CancellationToken cancellationToken);

    Task<int> RemoveStaleLooseImagesInRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken);
    Task<int> RemoveStaleImagesInGalleryAsync(Guid galleryEntityId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken);
    Task<int> RemoveStaleGalleriesInRootAsync(Guid rootId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken);
}

/// <summary>Audio scan persistence operations for discovered tracks/libraries and stale cleanup.</summary>
public interface IAudioScanPersistence {
    /// <summary>
    /// Upserts an audio track under its album. <paramref name="sectionLabel"/> names the
    /// album section (disc) the track belongs to, or null for an unsectioned album;
    /// <paramref name="sectionOrder"/> orders sections within the album. <paramref name="sortOrder"/>
    /// is the album-global ordinal (sections concatenated in section order, then file order)
    /// so play-all ordering is preserved.
    /// </summary>
    Task<Guid> UpsertAudioTrackAsync(string filePath, string title, Guid? audioLibraryId, int sortOrder, string? sectionLabel, int sectionOrder, bool isNsfw, CancellationToken cancellationToken);

    /// <summary>
    /// Upserts an album folder. <paramref name="parentEntityId"/> is the owning
    /// <see cref="EntityKind.MusicArtist"/> id, or null for an album with no artist folder.
    /// </summary>
    Task<Guid> UpsertAudioLibraryAsync(string folderPath, string title, Guid libraryRootId, Guid? parentEntityId, int sortOrder, bool isNsfw, CancellationToken cancellationToken);

    /// <summary>Upserts an artist folder that groups albums under one heading.</summary>
    Task<Guid> UpsertMusicArtistAsync(string folderPath, string title, Guid libraryRootId, int sortOrder, bool isNsfw, CancellationToken cancellationToken);

    /// <summary>
    /// Upserts discovered artist grouping folders as one persistence unit, returning IDs in input order.
    /// </summary>
    Task<IReadOnlyList<Guid>> UpsertMusicArtistsBatchAsync(
        IReadOnlyList<MusicArtistUpsertItem> items, CancellationToken cancellationToken);

    /// <summary>
    /// Upserts discovered album folders as one persistence unit, returning IDs in input order.
    /// Parent artist IDs must already be resolved by the caller.
    /// </summary>
    Task<IReadOnlyList<Guid>> UpsertAudioLibrariesBatchAsync(
        IReadOnlyList<AudioLibraryUpsertItem> items, CancellationToken cancellationToken);

    /// <summary>
    /// Upserts discovered audio tracks as one persistence unit, returning IDs in input order.
    /// </summary>
    Task<IReadOnlyList<Guid>> UpsertAudioTracksBatchAsync(
        IReadOnlyList<AudioTrackUpsertItem> items, CancellationToken cancellationToken);

    /// <summary>
    /// Lists existing audio tracks under a library root without re-running discovery/upsert. Used by
    /// unchanged scans to recover downstream work such as cancelled waveform jobs.
    /// </summary>
    Task<IReadOnlyList<EntityRefreshTarget>> GetAudioTrackTargetsInRootAsync(
        Guid rootId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EntityRefreshTarget>>([]);

    Task<int> RemoveStaleLooseAudioTracksInRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken);
    Task<int> RemoveStaleAudioTracksInLibraryAsync(Guid libraryEntityId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken);
    Task<int> RemoveStaleAudioLibrariesInRootAsync(Guid rootId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken);

    /// <summary>Removes artist groupings in the root whose folders no longer exist.</summary>
    Task<int> RemoveStaleMusicArtistsInRootAsync(Guid rootId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken);
}

/// <summary>Book/comic scan persistence operations for discovered archives/pages and stale cleanup.</summary>
public interface IBookScanPersistence {
    Task<Guid> UpsertBookAsync(string sourcePath, string title, Guid libraryRootId, bool isNsfw, CancellationToken cancellationToken);

    /// <summary>
    /// Upserts a folder-backed book series parent used to group single-file EPUB/PDF books.
    /// The parent is still represented by the book entity kind so existing book detail and
    /// thumbnail surfaces can render it as the library entry point.
    /// </summary>
    Task<Guid> UpsertBookSeriesAsync(
        string folderPath,
        string title,
        Guid libraryRootId,
        bool isNsfw,
        BookType bookType,
        BookFormat format,
        CancellationToken cancellationToken);

    /// <summary>
    /// Upserts a single-file book (EPUB/PDF) where the file itself is the whole book and
    /// no chapter/page entities are created. Stores the book format and content type so the
    /// reader can stream the raw file and select the matching renderer.
    /// When <paramref name="parentBookEntityId"/> is supplied, the book is attached under
    /// a folder-backed book series parent and ordered by <paramref name="sortOrder"/>.
    /// </summary>
    Task<Guid> UpsertSingleFileBookAsync(
        string sourcePath,
        string title,
        Guid libraryRootId,
        bool isNsfw,
        BookType bookType,
        BookFormat format,
        string contentType,
        Guid? parentBookEntityId,
        int? sortOrder,
        CancellationToken cancellationToken);

    Task<Guid> UpsertBookVolumeAsync(string folderPath, string title, Guid bookEntityId, int sortOrder, bool isNsfw, CancellationToken cancellationToken);
    Task<Guid> UpsertBookChapterAsync(string archivePath, string title, Guid parentEntityId, int sortOrder, int pageCount, bool isNsfw, CancellationToken cancellationToken);
    Task<Guid> UpsertBookPageAsync(string filePath, string title, Guid bookEntityId, Guid chapterEntityId, int sortOrder, bool isNsfw, CancellationToken cancellationToken);

    /// <summary>
    /// Upserts discovered book pages as one persistence unit, returning IDs in input order.
    /// </summary>
    Task<IReadOnlyList<Guid>> UpsertBookPagesBatchAsync(
        IReadOnlyList<BookPageUpsertItem> items, CancellationToken cancellationToken);

    /// <summary>
    /// Upserts a folder-backed author grouping that gathers an author's single-file books as children,
    /// mirroring how a music artist groups albums. Returns the author entity id.
    /// </summary>
    Task<Guid> UpsertBookAuthorAsync(string folderPath, string title, int? sortOrder, bool isNsfw, CancellationToken cancellationToken);

    Task<int> RemoveStaleBookVolumesAsync(Guid bookEntityId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken);
    Task<int> RemoveStaleBookChaptersAsync(Guid bookEntityId, IReadOnlySet<string> validArchivePaths, CancellationToken cancellationToken);
    Task<int> RemoveStaleBooksInRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken);

    /// <summary>Removes author groupings that no longer have any child books (run after stale book cleanup).</summary>
    Task<int> RemoveEmptyBookAuthorsAsync(CancellationToken cancellationToken);
}

/// <summary>Reads downstream processing state used to decide which jobs are still needed.</summary>
public interface IDownstreamNeedsPersistence {
    /// <summary>
    /// Checks what downstream jobs are needed for a batch of entities in a single query.
    /// Returns one <see cref="DownstreamNeeds"/> per entity ID.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, DownstreamNeeds>> CheckDownstreamNeedsBatchAsync(
        IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken);

    Task<bool> HasEntityTechnicalAsync(Guid entityId, CancellationToken cancellationToken);
    Task<bool> HasEntityFingerprintAsync(Guid entityId, FingerprintAlgorithm algorithm, CancellationToken cancellationToken);
    Task<bool> HasEntityFileAsync(Guid entityId, EntityFileRole role, CancellationToken cancellationToken);
    Task<bool> IsEntityOrganizedAsync(Guid entityId, CancellationToken cancellationToken) => Task.FromResult(false);
    Task<bool> HasSubtitlesExtractedAsync(Guid entityId, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the distinct top-level ancestors of the given scanned entities. Children (episodes,
    /// images in a gallery, tracks in an album) collapse to their root so auto identify targets only
    /// the parent, which cascades down to its descendants. Each root is returned once.
    /// </summary>
    /// <param name="entityIds">Scanned entity IDs to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AutoIdentifyRootTarget>> ResolveAutoIdentifyRootsAsync(
        IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AutoIdentifyRootTarget>>([]);

    /// <summary>
    /// Resolves auto-identify roots already persisted under a library root for a no-file-change scan.
    /// This lets scans enqueue metadata work that was previously skipped or enabled after the first
    /// scan without re-running the full media upsert pipeline.
    /// </summary>
    /// <param name="libraryRootId">Library root whose existing media should be considered.</param>
    /// <param name="scanCategories">Media categories covered by the current scan handler.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AutoIdentifyRootTarget>> ResolveAutoIdentifyRootsForLibraryRootAsync(
        Guid libraryRootId,
        IReadOnlyList<MediaCategory> scanCategories,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AutoIdentifyRootTarget>>([]);
}

/// <summary>
/// A distinct top-level entity that auto identify should target.
/// </summary>
/// <param name="Id">Root entity identifier.</param>
/// <param name="KindCode">Root entity kind code.</param>
/// <param name="Title">Root entity title for job dashboards.</param>
/// <param name="IsOrganized">Whether the root is already marked organized.</param>
public sealed record AutoIdentifyRootTarget(Guid Id, string KindCode, string Title, bool IsOrganized = false);

/// <summary>Writes media processing outputs and source-file metadata for entities.</summary>
public interface IMediaProcessingStatePersistence {
    Task UpsertEntityTechnicalAsync(Guid entityId, double? duration, int? width, int? height,
        double? frameRate, int? bitRate, int? sampleRate, int? channels,
        string? codec, string? container, string? format, CancellationToken cancellationToken);

    Task UpsertMediaSourceAsync(
        Guid entityId,
        string path,
        MediaSourceProbeData source,
        IReadOnlyList<MediaStreamProbeData> streams,
        CancellationToken cancellationToken);

    Task UpsertTrickplayInfoAsync(
        Guid entityId,
        TrickplayInfoData info,
        CancellationToken cancellationToken);

    Task UpsertEntityFileAsync(Guid entityId, EntityFileRole role, string path, string? mimeType, long? sizeBytes, CancellationToken cancellationToken);

    Task UpsertEntityFingerprintAsync(Guid entityId, FingerprintAlgorithm algorithm, string value, Guid? entityFileId, CancellationToken cancellationToken);

    Task<Guid?> GetSourceFileIdAsync(Guid entityId, CancellationToken cancellationToken);

    Task<string?> GetSourceFilePathAsync(Guid entityId, CancellationToken cancellationToken);

    Task MarkSubtitlesExtractedAsync(Guid entityId, CancellationToken cancellationToken);

    Task UpsertSubtitleAsync(Guid entityId, string language, string? label, string format,
        EntitySubtitleSource source, string storagePath, string sourceFormat, int streamIndex, CancellationToken cancellationToken);

    Task UpsertAudioTrackTagsAsync(Guid entityId, string? artist, string? album, int? trackNumber, CancellationToken cancellationToken);

    Task<EntityTechnicalData?> GetEntityTechnicalAsync(Guid entityId, CancellationToken cancellationToken);
}

/// <summary>Reads entity trees for refresh jobs that re-queue processing work.</summary>
public interface IEntityRefreshTreePersistence {
    /// <summary>
    /// Returns summary info for an entity and all its descendants (recursive children).
    /// Used by the refresh-entity job to re-queue processing for an entity tree.
    /// </summary>
    Task<IReadOnlyList<EntityRefreshTarget>> GetEntityTreeAsync(Guid entityId, CancellationToken cancellationToken);
}

public sealed record LibraryRootData(
    Guid Id,
    string Path,
    string Label,
    bool Enabled,
    bool Recursive,
    bool ScanVideos,
    bool ScanImages,
    bool ScanAudio,
    bool ScanBooks,
    bool IsNsfw,
    bool AutoIdentify = true);

public sealed record EntityTechnicalData(
    double? DurationSeconds,
    int? Width,
    int? Height,
    double? FrameRate,
    int? BitRate,
    int? SampleRate,
    int? Channels,
    string? Codec,
    string? Container);

public sealed record MediaSourceProbeData(
    double? DurationSeconds,
    long? SizeBytes,
    int? BitRate,
    string? Container,
    string? VideoCodec,
    string? AudioCodec,
    int? Width,
    int? Height,
    double? FrameRate);

public sealed record MediaStreamProbeData(
    int StreamIndex,
    string Type,
    string? Codec,
    string? Language,
    string? Title,
    int? Width,
    int? Height,
    double? FrameRate,
    int? BitRate,
    int? SampleRate,
    int? Channels,
    bool IsDefault,
    bool IsForced,
    string? PixelFormat = null,
    int? BitDepth = null,
    string? ColorRange = null,
    string? ColorSpace = null,
    string? ColorTransfer = null,
    string? ColorPrimaries = null,
    int? DvProfile = null,
    int? DvLevel = null,
    bool? RpuPresentFlag = null,
    bool? ElPresentFlag = null,
    bool? BlPresentFlag = null,
    int? DvBlSignalCompatibilityId = null,
    bool Hdr10PlusPresentFlag = false);

public sealed record TrickplayInfoData(
    int Width,
    int Height,
    int TileWidth,
    int TileHeight,
    int ThumbnailCount,
    double IntervalSeconds,
    int Bandwidth);

/// <param name="AutoIdentifyEnabled">Whether scanned media should be auto-identified via enabled plugins.</param>
/// <param name="AutoIdentifyKinds">Selector kind codes (video, gallery, image, audio, book) auto identify applies to.</param>
/// <param name="RemoveOrphanTags">Whether scan cleanup should delete tags that nothing references.</param>
/// <param name="AutoIdentifyUnorganizedOnly">Whether scans should skip auto-identify roots already marked organized.</param>
public sealed record LibrarySettingsData(
    bool AutoGenerateMetadata,
    bool AutoGenerateOshash,
    bool AutoGenerateMd5,
    bool AutoGeneratePreview,
    bool GenerateTrickplay,
    int TrickplayIntervalSeconds,
    int PreviewClipDurationSeconds,
    int ThumbnailQuality,
    int TrickplayQuality,
    bool AutoIdentifyEnabled = false,
    IReadOnlyList<string>? AutoIdentifyKinds = null,
    bool RemoveOrphanTags = false,
    bool AutoIdentifyUnorganizedOnly = true);

/// <summary>
/// Describes a video file discovered by a scan and the structural series context inferred from its path.
/// </summary>
/// <param name="FilePath">Absolute path to the playable video file.</param>
/// <param name="Title">Display title inferred from the filename or imported metadata.</param>
/// <param name="LibraryRootId">Library root that owns the file.</param>
/// <param name="IsNsfw">Whether the owning library root marks discovered media as NSFW.</param>
/// <param name="Movie">Optional movie folder context for single-film releases.</param>
/// <param name="Series">Optional series folder context for episode-style video files.</param>
/// <param name="Season">Optional season folder context when the file lives beneath a season grouping.</param>
/// <param name="EpisodeNumber">Episode number parsed from the filename when available.</param>
/// <param name="AbsoluteEpisodeNumber">Absolute episode number parsed from the filename when available.</param>
/// <param name="Metadata">Optional sidecar metadata read during scan.</param>
/// <param name="Movie">Optional movie folder context for single-film releases.</param>
/// <param name="FolderSortOrder">
/// Filename-based ordering index for a video grouped into a series purely because its folder holds
/// multiple loose videos (no season folders or episode tokens). Used to order such episodes
/// alphabetically when no real episode number exists.
/// </param>
public sealed record VideoUpsertItem(
    string FilePath,
    string Title,
    Guid LibraryRootId,
    bool IsNsfw,
    VideoSeriesScanInfo? Series = null,
    VideoSeasonScanInfo? Season = null,
    int? EpisodeNumber = null,
    int? AbsoluteEpisodeNumber = null,
    VideoSidecarMetadata? Metadata = null,
    MovieScanInfo? Movie = null,
    int? FolderSortOrder = null);

/// <summary>
/// Image file discovered during a gallery scan.
/// </summary>
/// <param name="FilePath">Absolute file path used as the source identity.</param>
/// <param name="Title">Display title inferred from the file name.</param>
/// <param name="GalleryEntityId">Optional owning gallery entity ID.</param>
/// <param name="SizeBytes">Observed file size when available.</param>
/// <param name="SortOrder">Position within the owning gallery.</param>
/// <param name="IsNsfw">Whether the owning library root marks discovered media as NSFW.</param>
public sealed record ImageUpsertItem(
    string FilePath,
    string Title,
    Guid? GalleryEntityId,
    long? SizeBytes,
    int SortOrder,
    bool IsNsfw);

/// <summary>
/// Gallery folder discovered during an image scan.
/// </summary>
/// <param name="FolderPath">Absolute folder path used as the source identity.</param>
/// <param name="Title">Display title inferred from the folder name.</param>
/// <param name="LibraryRootId">Library root that owns the folder.</param>
/// <param name="ParentGalleryEntityId">Optional parent gallery entity ID.</param>
/// <param name="SortOrder">Position within the parent gallery.</param>
/// <param name="IsNsfw">Whether the owning library root marks discovered media as NSFW.</param>
public sealed record GalleryUpsertItem(
    string FolderPath,
    string Title,
    Guid LibraryRootId,
    Guid? ParentGalleryEntityId,
    int SortOrder,
    bool IsNsfw);

/// <summary>
/// Audio track discovered during an audio scan.
/// </summary>
/// <param name="FilePath">Absolute file path used as the source identity.</param>
/// <param name="Title">Display title inferred from the file name.</param>
/// <param name="AudioLibraryId">Optional album entity ID.</param>
/// <param name="SortOrder">Album-global position, spanning disc sections.</param>
/// <param name="SectionLabel">Optional disc/section label.</param>
/// <param name="SectionOrder">Ordering index for the disc/section.</param>
/// <param name="IsNsfw">Whether the owning library root marks discovered media as NSFW.</param>
public sealed record AudioTrackUpsertItem(
    string FilePath,
    string Title,
    Guid? AudioLibraryId,
    int SortOrder,
    string? SectionLabel,
    int SectionOrder,
    bool IsNsfw);

/// <summary>
/// Album folder discovered during an audio scan.
/// </summary>
/// <param name="FolderPath">Absolute folder path used as the source identity.</param>
/// <param name="Title">Display title inferred from the folder name.</param>
/// <param name="LibraryRootId">Library root that owns the album.</param>
/// <param name="ParentEntityId">Optional artist grouping entity ID.</param>
/// <param name="SortOrder">Position within the parent artist/root.</param>
/// <param name="IsNsfw">Whether the owning library root marks discovered media as NSFW.</param>
public sealed record AudioLibraryUpsertItem(
    string FolderPath,
    string Title,
    Guid LibraryRootId,
    Guid? ParentEntityId,
    int SortOrder,
    bool IsNsfw);

/// <summary>
/// Artist grouping folder discovered during an audio scan.
/// </summary>
/// <param name="FolderPath">Absolute folder path used as the source identity.</param>
/// <param name="Title">Display title inferred from the folder name.</param>
/// <param name="LibraryRootId">Library root that owns the artist grouping.</param>
/// <param name="SortOrder">Position within the root.</param>
/// <param name="IsNsfw">Whether the owning library root marks discovered media as NSFW.</param>
public sealed record MusicArtistUpsertItem(
    string FolderPath,
    string Title,
    Guid LibraryRootId,
    int SortOrder,
    bool IsNsfw);

/// <summary>
/// Book page discovered inside an archive-backed chapter.
/// </summary>
/// <param name="FilePath">Synthetic archive member path used as the source identity.</param>
/// <param name="Title">Display title inferred from the member name.</param>
/// <param name="BookEntityId">Top-level book entity that owns the page.</param>
/// <param name="ChapterEntityId">Chapter entity that directly contains the page.</param>
/// <param name="SortOrder">Position within the chapter.</param>
/// <param name="IsNsfw">Whether the owning book/chapter marks discovered pages as NSFW.</param>
public sealed record BookPageUpsertItem(
    string FilePath,
    string Title,
    Guid BookEntityId,
    Guid ChapterEntityId,
    int SortOrder,
    bool IsNsfw);

/// <summary>
/// Movie folder context inferred during a scan.
/// </summary>
/// <param name="FolderPath">Absolute folder path that identifies the movie in the library.</param>
/// <param name="Title">Movie title inferred from the folder name or sidecar metadata.</param>
public sealed record MovieScanInfo(string FolderPath, string Title);

/// <summary>
/// Series folder context inferred during a scan.
/// </summary>
/// <param name="FolderPath">Absolute folder path that identifies the series in the library.</param>
/// <param name="Title">Series title inferred from the folder name.</param>
public sealed record VideoSeriesScanInfo(string FolderPath, string Title);

/// <summary>
/// Season folder context inferred during a scan.
/// </summary>
/// <param name="FolderPath">Absolute folder path that identifies the season in the library.</param>
/// <param name="Title">Season title inferred from the folder name.</param>
/// <param name="SeasonNumber">Numeric season index used for ordering and metadata.</param>
public sealed record VideoSeasonScanInfo(string FolderPath, string Title, int SeasonNumber);

/// <summary>
/// Flags indicating which downstream jobs are still needed for an entity.
/// </summary>
/// <param name="NeedsProbe">Technical metadata has not been probed yet.</param>
/// <param name="MissingOshash">No stored oshash fingerprint exists for the entity.</param>
/// <param name="MissingMd5">No stored MD5 fingerprint exists for the entity.</param>
/// <param name="NeedsPreview">No preview asset (thumbnail, waveform, or animated image clip) exists yet.</param>
/// <param name="NeedsTrickplay">No trickplay tiles exist yet.</param>
/// <param name="NeedsSubtitleExtraction">Embedded subtitles have not been extracted yet.</param>
/// <param name="NeedsGridThumbnail">A cover exists but its small grid-card variant has not been generated yet.</param>
public sealed record DownstreamNeeds(
    bool NeedsProbe,
    bool MissingOshash,
    bool MissingMd5,
    bool NeedsPreview,
    bool NeedsTrickplay,
    bool NeedsSubtitleExtraction,
    bool NeedsGridThumbnail);

/// <summary>
/// Lightweight entity info used by the refresh-entity job to decide which downstream jobs to queue.
/// </summary>
/// <param name="Id">Entity identifier.</param>
/// <param name="KindCode">Entity kind code (e.g. "video", "image", "audio-track").</param>
/// <param name="Title">Entity title for dashboard display.</param>
public sealed record EntityRefreshTarget(Guid Id, string KindCode, string Title);
