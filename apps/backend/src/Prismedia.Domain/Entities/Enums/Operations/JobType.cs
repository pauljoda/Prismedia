namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of queue job types known to the .NET backend.
/// Each value maps 1:1 to a processor that the worker can dispatch.
/// </summary>
public enum JobType {
    /// <summary>No-operation job used to verify queue plumbing.</summary>
    [Code("noop")]
    Noop,

    // ── Scanning ────────────────────────────────────────────────
    /// <summary>Discovers video files in a library root.</summary>
    [Code("scan-library")]
    ScanLibrary,

    /// <summary>Discovers image galleries in a library root.</summary>
    [Code("scan-gallery")]
    ScanGallery,

    /// <summary>Discovers comic books in a library root.</summary>
    [Code("scan-book")]
    ScanBook,

    /// <summary>Discovers audio tracks in a library root.</summary>
    [Code("scan-audio")]
    ScanAudio,

    // ── Probing ─────────────────────────────────────────────────
    /// <summary>Extracts technical metadata from a video file via ffprobe.</summary>
    [Code("probe-video")]
    ProbeVideo,

    /// <summary>Extracts technical metadata and embedded tags from an audio file.</summary>
    [Code("probe-audio")]
    ProbeAudio,

    // ── Fingerprinting ──────────────────────────────────────────
    /// <summary>Computes MD5 and oshash for a video.</summary>
    [Code("fingerprint-video")]
    FingerprintVideo,

    /// <summary>Computes MD5 and oshash for an image.</summary>
    [Code("fingerprint-image")]
    FingerprintImage,

    /// <summary>Computes MD5 and oshash for an audio track.</summary>
    [Code("fingerprint-audio")]
    FingerprintAudio,

    // ── Preview / asset generation ──────────────────────────────
    /// <summary>Builds video thumbnails, preview clips, and trickplay sprites.</summary>
    [Code("generate-preview")]
    GeneratePreview,

    /// <summary>Generates thumbnails and lightweight previews for images.</summary>
    [Code("generate-image-thumbnail")]
    GenerateImageThumbnail,

    /// <summary>Generates the small grid-card cover variant for an entity that already has a cover.</summary>
    [Code("generate-grid-thumbnail")]
    GenerateGridThumbnail,

    /// <summary>Generates thumbnails for comic book pages.</summary>
    [Code("generate-book-page-thumbnail")]
    GenerateBookPageThumbnail,

    /// <summary>Generates the cover thumbnail for a single-file book (EPUB/PDF).</summary>
    [Code("generate-book-cover-thumbnail")]
    GenerateBookCoverThumbnail,

    /// <summary>Generates waveform peak data for audio playback visualization.</summary>
    [Code("generate-audio-waveform")]
    GenerateAudioWaveform,

    /// <summary>Extracts embedded subtitle tracks from video files as WebVTT.</summary>
    [Code("extract-subtitles")]
    ExtractSubtitles,

    // ── Metadata / collections ──────────────────────────────────
    /// <summary>Coordinates provider imports and metadata application.</summary>
    [Code("import-metadata")]
    ImportMetadata,

    /// <summary>Re-evaluates dynamic collection rules and updates membership.</summary>
    [Code("refresh-collection")]
    RefreshCollection,

    /// <summary>Moves video-derived assets between cache and media-adjacent storage.</summary>
    [Code("library-maintenance")]
    LibraryMaintenance,

    /// <summary>Creates a retained automatic database backup.</summary>
    [Code("database-backup")]
    DatabaseBackup,

    // ── Entity refresh ─────────────────────────────────────────
    /// <summary>Re-runs the processing pipeline for a single entity and its children.</summary>
    [Code("refresh-entity")]
    RefreshEntity,

    // ── Identify ─────────────────────────────────────────────────
    /// <summary>Runs one requested provider search for a single identify queue item.</summary>
    [Code("identify-search")]
    IdentifySearch,

    /// <summary>Legacy batch identify; retained so historical job rows decode. New batches enqueue one identify-search job per entity.</summary>
    [Code("bulk-identify")]
    BulkIdentify,

    /// <summary>Auto-identifies one scanned entity through the configured plugins and applies the first confident match.</summary>
    [Code("auto-identify")]
    AutoIdentify,

    /// <summary>Walks a queued entity's full child tree through a provider, streaming the growing proposal onto the queue item.</summary>
    [Code("identify-cascade")]
    IdentifyCascade,

    // ── Acquisition ─────────────────────────────────────────────
    /// <summary>Searches configured indexers for an acquisition's book and persists scored release candidates.</summary>
    [Code("acquisition-search")]
    AcquisitionSearch,

    /// <summary>Polls active download-client transfers for in-flight acquisitions and advances their status.</summary>
    [Code("acquisition-monitor")]
    AcquisitionMonitor,

    /// <summary>Moves a completed acquisition payload into a library root, writes identify hints, and enqueues a book scan.</summary>
    [Code("acquisition-import")]
    AcquisitionImport,

    /// <summary>Handles a failed download: blocklists the release and, when auto-redownload is on, grabs the next-best candidate.</summary>
    [Code("acquisition-failed-handle")]
    AcquisitionFailedHandle,

    /// <summary>Re-runs the release search for every due monitored acquisition so a wanted item is fetched once a release appears.</summary>
    [Code("monitored-search")]
    MonitoredSearch,

    /// <summary>Replaces an owned book file with a fully-downloaded, verified, strictly-better upgrade release.</summary>
    [Code("acquisition-upgrade-replace")]
    AcquisitionUpgradeReplace,

    /// <summary>Enriches a request's held metadata (cover, description, dates) from the provider before import.</summary>
    [Code("acquisition-enrich")]
    AcquisitionEnrich,

    /// <summary>Purges recycle-bin entries older than the configured cleanup window.</summary>
    [Code("recycle-bin-cleanup")]
    RecycleBinCleanup
}
