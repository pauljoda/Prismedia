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
    /// <summary>Computes MD5, oshash, and optional perceptual hash for a video.</summary>
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

    // ── Entity refresh ─────────────────────────────────────────
    /// <summary>Re-runs the processing pipeline for a single entity and its children.</summary>
    [Code("refresh-entity")]
    RefreshEntity,

    // ── Identify ─────────────────────────────────────────────────
    /// <summary>Identifies multiple entities in batch via a provider plugin.</summary>
    [Code("bulk-identify")]
    BulkIdentify,

    /// <summary>Auto-identifies one scanned entity through the configured plugins and applies the first confident match.</summary>
    [Code("auto-identify")]
    AutoIdentify
}
