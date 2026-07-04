namespace Prismedia.Application.Settings;

/// <summary>
/// Stable public keys for app-global settings exposed through the settings registry API.
/// </summary>
public static class AppSettingKeys {
    public const string VisibilityDefaultMode = "visibility.defaultMode";
    public const string VisibilityLanAutoEnable = "visibility.lanAutoEnable";

    public const string ScanAutoScanEnabled = "scan.autoScanEnabled";
    public const string ScanIntervalMinutes = "scan.intervalMinutes";

    public const string CollectionsAutoRefreshEnabled = "collections.autoRefreshEnabled";

    public const string MonitoringSearchEnabled = "monitoring.searchEnabled";
    public const string MonitoringIntervalMinutes = "monitoring.intervalMinutes";

    public const string AcquisitionRecycleBinPath = "acquisition.recycleBinPath";
    public const string AcquisitionRecycleBinCleanupDays = "acquisition.recycleBinCleanupDays";
    public const string AcquisitionDownloadPropers = "acquisition.downloadPropers";

    public const string AutoIdentifyEnabled = "autoIdentify.enabled";
    public const string AutoIdentifyProviders = "autoIdentify.providers";
    public const string AutoIdentifyEntityKinds = "autoIdentify.entityKinds";
    public const string AutoIdentifyConfidenceThreshold = "autoIdentify.confidenceThreshold";
    public const string AutoIdentifyUnorganizedOnly = "autoIdentify.unorganizedOnly";

    public const string TaxonomyRemoveOrphanTags = "taxonomy.removeOrphanTags";

    public const string GenerationAutoGenerateMetadata = "generation.autoGenerateMetadata";
    public const string GenerationAutoGenerateOshash = "generation.autoGenerateOshash";
    public const string GenerationAutoGenerateMd5 = "generation.autoGenerateMd5";
    public const string GenerationAutoGeneratePreview = "generation.autoGeneratePreview";
    public const string GenerationGenerateTrickplay = "generation.generateTrickplay";
    public const string GenerationTrickplayIntervalSeconds = "generation.trickplayIntervalSeconds";
    public const string GenerationPreviewClipDurationSeconds = "generation.previewClipDurationSeconds";
    public const string GenerationThumbnailQuality = "generation.thumbnailQuality";
    public const string GenerationTrickplayQuality = "generation.trickplayQuality";
    public const string GenerationMetadataStorageDedicated = "generation.metadataStorageDedicated";

    public const string JobsBackgroundConcurrency = "jobs.backgroundConcurrency";

    public const string PlaybackDefaultMode = "playback.defaultMode";
    public const string PlaybackShowCastControls = "playback.showCastControls";
    public const string PlaybackAudioPreferredLanguages = "playback.audioPreferredLanguages";

    public const string SubtitlesAutoEnable = "subtitles.autoEnable";
    public const string SubtitlesPreferredLanguages = "subtitles.preferredLanguages";
    public const string SubtitlesStyle = "subtitles.style";
    public const string SubtitlesFontScale = "subtitles.fontScale";
    public const string SubtitlesPositionPercent = "subtitles.positionPercent";
    public const string SubtitlesOpacity = "subtitles.opacity";

    public const string HlsTranscoderProfile = "hls.transcoderProfile";
    public const string HlsFfmpegPath = "hls.ffmpegPath";
    public const string HlsVaapiDevice = "hls.vaapiDevice";
    public const string HlsEnableAdaptiveBitrate = "hls.enableAdaptiveBitrate";
    public const string HlsEncodingThreadCount = "hls.encodingThreadCount";
    public const string HlsMaxCacheSizeGb = "hls.maxCacheSizeGb";
}
