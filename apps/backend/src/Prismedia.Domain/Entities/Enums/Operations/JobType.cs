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

    /// <summary>Discovers prose books and audiobook sources in a library root.</summary>
    [Code("scan-book")]
    ScanBook,

    /// <summary>Discovers serialized comic installments and their page manifests in a library root.</summary>
    [Code("scan-comic")]
    ScanComic,

    /// <summary>Discovers audio tracks in a library root.</summary>
    [Code("scan-audio")]
    ScanAudio,

    /// <summary>Plans and appends processing work for one exact entity tree without enumerating a library root.</summary>
    [Code("reconcile-entity")]
    ReconcileEntity,

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
    /// <summary>Builds video thumbnails and short preview clips.</summary>
    [Code("generate-preview")]
    GeneratePreview,

    /// <summary>Builds deferred trickplay tiles for video scrub previews.</summary>
    [Code("generate-trickplay")]
    GenerateTrickplay,

    /// <summary>Generates thumbnails and lightweight previews for images.</summary>
    [Code("generate-image-thumbnail")]
    GenerateImageThumbnail,

    /// <summary>Generates the small grid-card cover variant for an entity that already has a cover.</summary>
    [Code("generate-grid-thumbnail")]
    GenerateGridThumbnail,

    /// <summary>Backfills and refreshes grid-card cover variants for every entity whose variants are missing or stale.</summary>
    [Code("grid-thumbnail-sweep")]
    GridThumbnailSweep,

    /// <summary>Generates the cover thumbnail for a single-file book (EPUB/PDF).</summary>
    [Code("generate-book-cover-thumbnail")]
    GenerateBookCoverThumbnail,

    /// <summary>Persists a Book's readable chapter list and recomputes its automatic audiobook chapter map.</summary>
    [Code("map-book-chapters")]
    MapBookChapters,

    /// <summary>Generates waveform peak data for audio playback visualization.</summary>
    [Code("generate-audio-waveform")]
    GenerateAudioWaveform,

    /// <summary>Reconciles embedded and adjacent subtitle tracks into app-owned playback assets.</summary>
    [Code("extract-subtitles")]
    ExtractSubtitles,

    /// <summary>Acquires missing preferred-language subtitles from configured providers.</summary>
    [Code("acquire-subtitles")]
    AcquireSubtitles,

    /// <summary>Downloads and imports one user-selected subtitle candidate.</summary>
    [Code("acquire-subtitle")]
    AcquireSubtitle,

    // ── Metadata / collections ──────────────────────────────────
    /// <summary>Coordinates provider imports and metadata application.</summary>
    [Code("import-metadata")]
    ImportMetadata,

    /// <summary>Re-evaluates dynamic collection rules and updates membership.</summary>
    [Code("refresh-collection")]
    RefreshCollection,

    /// <summary>Validates generated assets and deletes orphaned per-entity cache directories.</summary>
    [Code("library-maintenance")]
    LibraryMaintenance,

    /// <summary>Creates a retained automatic database backup.</summary>
    [Code("database-backup")]
    DatabaseBackup,

    /// <summary>Checks installed plugins for compatible updates and installs newer artifacts.</summary>
    [Code("update-plugins")]
    UpdatePlugins,

    // ── Entity refresh ─────────────────────────────────────────
    /// <summary>Re-runs the processing pipeline for a single entity and its children.</summary>
    [Code("refresh-entity")]
    RefreshEntity,

    // ── Identify ─────────────────────────────────────────────────
    /// <summary>Runs one requested provider search for a single identify queue item.</summary>
    [Code("identify-search")]
    IdentifySearch,

    /// <summary>Runs provider-backed identify expansion for one queued entity graph.</summary>
    [Code("identify-provider-call")]
    IdentifyProviderCall,

    /// <summary>Applies one reviewed identify proposal inside its existing interactive graph.</summary>
    [Code("identify-apply")]
    IdentifyApply,

    /// <summary>Legacy batch identify; retained so historical job rows decode. New batches enqueue one identify-search job per entity.</summary>
    [Code("bulk-identify")]
    BulkIdentify,

    /// <summary>Auto-identifies one scanned entity through the configured plugins and applies the first confident match.</summary>
    [Code("auto-identify")]
    AutoIdentify,

    /// <summary>Legacy identify cascade; retained so historical job rows decode.</summary>
    [Code("identify-cascade")]
    IdentifyCascade,

    // ── Acquisition ─────────────────────────────────────────────
    /// <summary>Searches configured indexers for an acquisition's book and persists scored release candidates.</summary>
    [Code("acquisition-search")]
    AcquisitionSearch,

    /// <summary>Polls active download-client transfers for in-flight acquisitions and advances their status.</summary>
    [Code("acquisition-monitor")]
    AcquisitionMonitor,

    /// <summary>Moves a completed acquisition into a library root and materializes its exact entity scope.</summary>
    [Code("acquisition-import")]
    AcquisitionImport,

    /// <summary>Publishes an acquisition as imported after required entity reconciliation succeeds.</summary>
    [Code("acquisition-finalize")]
    AcquisitionFinalize,

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

    /// <summary>Starts acquisitions for a reviewed container's committed children after the interactive response.</summary>
    [Code("request-acquisition-fanout")]
    RequestAcquisitionFanout,

    /// <summary>Purges recycle-bin entries older than the configured cleanup window.</summary>
    [Code("recycle-bin-cleanup")]
    RecycleBinCleanup
}
