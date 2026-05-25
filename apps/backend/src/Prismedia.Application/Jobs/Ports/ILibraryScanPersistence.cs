using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Ports;

/// <summary>
/// Port for entity persistence operations during library scanning. Handles the create/update/delete
/// lifecycle for entities discovered by file system scans.
/// </summary>
public interface ILibraryScanPersistence {
    // ── Library roots & settings ──

    Task<LibraryRootData?> GetLibraryRootAsync(Guid rootId, CancellationToken cancellationToken);
    Task<IReadOnlyList<LibraryRootData>> GetEnabledRootsAsync(CancellationToken cancellationToken);
    Task<LibrarySettingsData> GetSettingsAsync(CancellationToken cancellationToken);
    Task UpdateRootLastScannedAsync(Guid rootId, CancellationToken cancellationToken);

    // ── Entity upsert (returns entity ID) ──

    Task<Guid> UpsertVideoAsync(string filePath, string title, Guid libraryRootId, bool isNsfw, CancellationToken cancellationToken);
    Task<Guid> UpsertImageAsync(string filePath, string title, Guid? galleryEntityId, long? sizeBytes, int sortOrder, bool isNsfw, CancellationToken cancellationToken);
    Task<Guid> UpsertGalleryAsync(string folderPath, string title, Guid libraryRootId, Guid? parentGalleryEntityId, int sortOrder, bool isNsfw, CancellationToken cancellationToken);
    Task<Guid> UpsertAudioTrackAsync(string filePath, string title, Guid? audioLibraryId, int sortOrder, bool isNsfw, CancellationToken cancellationToken);
    Task<Guid> UpsertAudioLibraryAsync(string folderPath, string title, Guid libraryRootId, Guid? parentAudioLibraryEntityId, int sortOrder, bool isNsfw, CancellationToken cancellationToken);
    Task<Guid> UpsertBookAsync(string sourcePath, string title, Guid libraryRootId, bool isNsfw, CancellationToken cancellationToken);
    Task<Guid> UpsertBookVolumeAsync(string folderPath, string title, Guid bookEntityId, int sortOrder, bool isNsfw, CancellationToken cancellationToken);
    Task<Guid> UpsertBookChapterAsync(string archivePath, string title, Guid parentEntityId, int sortOrder, int pageCount, bool isNsfw, CancellationToken cancellationToken);
    Task<Guid> UpsertBookPageAsync(string filePath, string title, Guid bookEntityId, Guid chapterEntityId, int sortOrder, bool isNsfw, CancellationToken cancellationToken);

    // ── Stale entity cleanup ──

    Task<int> RemoveStaleVideosByRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken);
    Task<int> RemoveStaleLooseImagesInRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken);
    Task<int> RemoveStaleImagesInGalleryAsync(Guid galleryEntityId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken);
    Task<int> RemoveStaleGalleriesInRootAsync(Guid rootId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken);
    Task<int> RemoveStaleLooseAudioTracksInRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken);
    Task<int> RemoveStaleAudioTracksInLibraryAsync(Guid libraryEntityId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken);
    Task<int> RemoveStaleAudioLibrariesInRootAsync(Guid rootId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken);
    Task<int> RemoveStaleBookVolumesAsync(Guid bookEntityId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken);
    Task<int> RemoveStaleBookChaptersAsync(Guid bookEntityId, IReadOnlySet<string> validArchivePaths, CancellationToken cancellationToken);
    Task<int> RemoveStaleBooksInRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken);

    /// <summary>
    /// Removes video series and season entities that have no remaining child entities.
    /// Should be called after stale video removal to clean up empty container shells.
    /// </summary>
    Task<int> RemoveOrphanSeriesAndSeasonsAsync(CancellationToken cancellationToken);

    // ── Batch upsert ──

    /// <summary>
    /// Upserts a batch of video entities in a single database round-trip,
    /// returning the entity ID for each input file path in the same order.
    /// </summary>
    Task<IReadOnlyList<Guid>> UpsertVideosBatchAsync(
        IReadOnlyList<VideoUpsertItem> items, CancellationToken cancellationToken);

    /// <summary>
    /// Checks what downstream jobs are needed for a batch of entities in a single query.
    /// Returns one <see cref="DownstreamNeeds"/> per entity ID.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, DownstreamNeeds>> CheckDownstreamNeedsBatchAsync(
        IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken);

    // ── Reads for downstream chaining decisions ──

    Task<bool> HasEntityTechnicalAsync(Guid entityId, CancellationToken cancellationToken);
    Task<bool> HasEntityFingerprintAsync(Guid entityId, FingerprintAlgorithm algorithm, CancellationToken cancellationToken);
    Task<bool> HasEntityFileAsync(Guid entityId, EntityFileRole role, CancellationToken cancellationToken);
    Task<bool> HasSubtitlesExtractedAsync(Guid entityId, CancellationToken cancellationToken);

    // ── Entity technical / file / fingerprint writes ──

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

    Task UpsertAudioTrackTagsAsync(Guid entityId, string? artist, string? album, CancellationToken cancellationToken);

    Task<EntityTechnicalData?> GetEntityTechnicalAsync(Guid entityId, CancellationToken cancellationToken);

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
    bool IsNsfw);

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

public sealed record LibrarySettingsData(
    bool AutoGenerateMetadata,
    bool AutoGenerateFingerprints,
    bool GeneratePhash,
    bool AutoGeneratePreview,
    bool GenerateTrickplay,
    int TrickplayIntervalSeconds,
    int PreviewClipDurationSeconds,
    int ThumbnailQuality,
    int TrickplayQuality);

/// <summary>
/// Describes a video file discovered by a scan and the structural series context inferred from its path.
/// </summary>
/// <param name="FilePath">Absolute path to the playable video file.</param>
/// <param name="Title">Display title inferred from the filename or imported metadata.</param>
/// <param name="LibraryRootId">Library root that owns the file.</param>
/// <param name="IsNsfw">Whether the owning library root marks discovered media as NSFW.</param>
/// <param name="Series">Optional series folder context for episode-style video files.</param>
/// <param name="Season">Optional season folder context when the file lives beneath a season grouping.</param>
/// <param name="EpisodeNumber">Episode number parsed from the filename when available.</param>
/// <param name="AbsoluteEpisodeNumber">Absolute episode number parsed from the filename when available.</param>
public sealed record VideoUpsertItem(
    string FilePath,
    string Title,
    Guid LibraryRootId,
    bool IsNsfw,
    VideoSeriesScanInfo? Series = null,
    VideoSeasonScanInfo? Season = null,
    int? EpisodeNumber = null,
    int? AbsoluteEpisodeNumber = null);

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
public sealed record DownstreamNeeds(
    bool NeedsProbe,
    bool NeedsFingerprint,
    bool NeedsPreview,
    bool NeedsTrickplay,
    bool NeedsSubtitleExtraction);

/// <summary>
/// Lightweight entity info used by the refresh-entity job to decide which downstream jobs to queue.
/// </summary>
/// <param name="Id">Entity identifier.</param>
/// <param name="KindCode">Entity kind code (e.g. "video", "image", "audio-track").</param>
/// <param name="Title">Entity title for dashboard display.</param>
public sealed record EntityRefreshTarget(Guid Id, string KindCode, string Title);
