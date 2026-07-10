using System.Text.Json;
using Prismedia.Contracts.Settings;

namespace Prismedia.Application.Settings;

/// <summary>
/// Central registry for Prismedia's app-global settings.
/// </summary>
public static class AppSettingsRegistry {
    private const string Visibility = "visibility";
    private const string Scan = "scan";
    private const string Collections = "collections";
    private const string Monitoring = "monitoring";
    private const string Taxonomy = "taxonomy";
    private const string AutoIdentify = "autoIdentify";
    private const string Generation = "generation";
    private const string Jobs = "jobs";
    private const string Playback = "playback";
    private const string Subtitles = "subtitles";
    private const string Requests = "requests";
    private const string Hls = "hls";
    private const string TranscodeCache = "transcodeCache";

    private static readonly IReadOnlyDictionary<string, SettingDefinition> ByKey;

    static AppSettingsRegistry() {
        Definitions = BuildDefinitions();
        ByKey = Definitions.ToDictionary(definition => definition.Key, StringComparer.Ordinal);
    }

    /// <summary>
    /// All registry definitions in stable display order.
    /// </summary>
    public static IReadOnlyList<SettingDefinition> Definitions { get; }

    /// <summary>
    /// Finds one setting definition by key.
    /// </summary>
    /// <param name="key">Stable dotted setting key.</param>
    public static SettingDefinition? Find(string key) =>
        ByKey.TryGetValue(key, out var definition) ? definition : null;

    private static IReadOnlyList<SettingDefinition> BuildDefinitions() {
        var definitions = new List<SettingDefinition> {
            Select(
                AppSettingKeys.VisibilityDefaultMode,
                Visibility,
                "Content Visibility",
                "Default adult-content visibility for new browsers.",
                10,
                "Default NSFW mode",
                "Used when a browser has not chosen its own NSFW mode yet.",
                "off",
                10,
                [
                    new SettingOption("off", "Off (SFW)", "Hide adult content by default."),
                    new SettingOption("show", "Show", "Display all content by default.")
                ]),

            Boolean(
                AppSettingKeys.ScanAutoScanEnabled,
                Scan,
                "Library Scans",
                "Control recurring library scans.",
                20,
                "Automatic library scans",
                "Queue scans on a recurring interval.",
                false,
                10),
            Integer(
                AppSettingKeys.ScanIntervalMinutes,
                Scan,
                "Library Scans",
                "Control recurring library scans.",
                20,
                "Scan interval",
                "Minutes between automatic scans.",
                60,
                20,
                min: 5,
                max: 1440,
                step: 5),

            Boolean(
                AppSettingKeys.CollectionsAutoRefreshEnabled,
                Collections,
                "Collections",
                "Control recurring collection refresh jobs.",
                21,
                "Auto Refresh Collections",
                "When on, Prismedia queues an hourly job at the top of the hour to evaluate dynamic collection rules and update collection membership.",
                true,
                10),

            Boolean(
                AppSettingKeys.MonitoringSearchEnabled,
                Monitoring,
                "Monitoring",
                "Keep searching for monitored items until they are acquired.",
                22,
                "Re-search monitored items",
                "When on, Prismedia periodically re-runs the release search for monitored items so wanted media is fetched once a release appears.",
                true,
                10),
            Integer(
                AppSettingKeys.MonitoringIntervalMinutes,
                Monitoring,
                "Monitoring",
                "Keep searching for monitored items until they are acquired.",
                22,
                "Re-search interval",
                "Minutes between re-searches of monitored items.",
                1440,
                20,
                min: 15,
                max: 10080,
                step: 15),
            String(
                AppSettingKeys.AcquisitionRecycleBinPath,
                Monitoring,
                "Monitoring",
                "Keep searching for monitored items until they are acquired.",
                22,
                "Recycle bin folder",
                "When set, a file replaced by a quality upgrade is moved here instead of being kept beside the new one, and is purged after the cleanup window. Leave blank to keep replaced files next to the upgrade as .prismedia-bak.",
                "",
                30),
            Integer(
                AppSettingKeys.AcquisitionRecycleBinCleanupDays,
                Monitoring,
                "Monitoring",
                "Keep searching for monitored items until they are acquired.",
                22,
                "Recycle bin cleanup (days)",
                "Files older than this are deleted from the recycle bin by the daily cleanup.",
                7,
                40,
                min: 1,
                max: 365,
                step: 1),
            Select(
                AppSettingKeys.AcquisitionDownloadPropers,
                Monitoring,
                "Monitoring",
                "Keep searching for monitored items until they are acquired.",
                22,
                "Download propers and repacks",
                "How PROPER, REPACK, RERIP and anime v2+ re-releases are treated. Stores a ProperDownloadPolicy code: prefer-and-upgrade (default) ranks them higher AND upgrades an owned copy to a better revision at the same quality; do-not-upgrade ranks them higher but never upgrades an owned copy for revision alone; do-not-prefer ignores revisions entirely.",
                "prefer-and-upgrade",
                50,
                [
                    new SettingOption("prefer-and-upgrade", "Prefer and upgrade", "Rank revisions higher and upgrade an owned copy to a better same-quality revision."),
                    new SettingOption("do-not-upgrade", "Prefer, do not upgrade", "Rank revisions higher on first grab, but never upgrade an owned copy for revision alone."),
                    new SettingOption("do-not-prefer", "Do not prefer", "Ignore revisions entirely; only the quality ladder decides.")
                ]),

            Boolean(
                AppSettingKeys.TaxonomyRemoveOrphanTags,
                Taxonomy,
                "Library Cleanup",
                "Automatically prune unused taxonomy entries during scans.",
                22,
                "Remove orphan tags",
                "When on, tags that nothing references are deleted during each library scan, keeping the tag list free of stale leftovers. A tag you create but have not applied to anything yet counts as orphaned and will be removed on the next scan.",
                false,
                10),

            Boolean(
                AppSettingKeys.AutoIdentifyEnabled,
                AutoIdentify,
                "Auto Identify",
                "Let trusted plugins identify and fill new media automatically during scans.",
                25,
                "Auto identify during scans",
                "When on, each scanned item runs through your enabled plugins and the first confident match is applied automatically — no manual review needed. Requires at least one installed plugin and can noticeably increase library scan times.",
                false,
                10),
            StringList(
                AppSettingKeys.AutoIdentifyProviders,
                AutoIdentify,
                "Auto Identify",
                "Let trusted plugins identify and fill new media automatically during scans.",
                25,
                "Enabled plugins",
                "Plugins tried in order during auto identify. The first one that returns a confident match wins.",
                [],
                20),
            StringList(
                AppSettingKeys.AutoIdentifyEntityKinds,
                AutoIdentify,
                "Auto Identify",
                "Let trusted plugins identify and fill new media automatically during scans.",
                25,
                "Identify these kinds",
                "Which kinds of scanned media auto identify applies to.",
                ["video", "gallery", "image", "audio", "book"],
                30),
            Decimal(
                AppSettingKeys.AutoIdentifyConfidenceThreshold,
                AutoIdentify,
                "Auto Identify",
                "Let trusted plugins identify and fill new media automatically during scans.",
                25,
                "Auto-apply confidence",
                "Minimum match confidence (or an exact match) required before a plugin result is applied without review.",
                90m,
                40,
                min: 0m,
                max: 100m,
                step: 1m),
            Boolean(
                AppSettingKeys.AutoIdentifyUnorganizedOnly,
                AutoIdentify,
                "Auto Identify",
                "Let trusted plugins identify and fill new media automatically during scans.",
                25,
                "Only un-organized items",
                "When on, skip items already marked organized. Turn off to re-identify every item on each scan.",
                true,
                50),

            Boolean(
                AppSettingKeys.GenerationAutoGenerateMetadata,
                Generation,
                "Generation Pipeline",
                "Control what the worker creates for newly discovered media.",
                30,
                "Technical metadata",
                "Probe runtime, resolution, codec, and bitrate on import.",
                true,
                10),
            Boolean(
                AppSettingKeys.GenerationAutoGenerateOshash,
                Generation,
                "Generation Pipeline",
                "Control what the worker creates for newly discovered media.",
                30,
                "OpenSubtitles hash (oshash)",
                "Compute the lightweight oshash from each file's head and tail for fast matching. Reads only a small slice of every file.",
                true,
                20),
            Boolean(
                AppSettingKeys.GenerationAutoGenerateMd5,
                Generation,
                "Generation Pipeline",
                "Control what the worker creates for newly discovered media.",
                30,
                "MD5 checksum",
                "Compute a full-file MD5 hash for Stash-compatible checksum matching. Reads every byte of every file, so it is slow on large libraries.",
                false,
                25),
            Boolean(
                AppSettingKeys.GenerationAutoGeneratePreview,
                Generation,
                "Generation Pipeline",
                "Control what the worker creates for newly discovered media.",
                30,
                "Preview assets",
                "Build thumbnails and short preview clips.",
                true,
                40),
            Boolean(
                AppSettingKeys.GenerationGenerateTrickplay,
                Generation,
                "Generation Pipeline",
                "Control what the worker creates for newly discovered media.",
                30,
                "Trickplay strips",
                "Build sprite sheets for player scrub previews.",
                true,
                50),
            Boolean(
                AppSettingKeys.GenerationMetadataStorageDedicated,
                Generation,
                "Generation Pipeline",
                "Control what the worker creates for newly discovered media.",
                30,
                "Store video previews in dedicated cache directory",
                "When on, generated video assets live under the app data volume instead of beside source files.",
                true,
                60),
            Integer(
                AppSettingKeys.GenerationTrickplayIntervalSeconds,
                Generation,
                "Generation Pipeline",
                "Control what the worker creates for newly discovered media.",
                30,
                "Trickplay interval",
                "Seconds between sprite sheet frames.",
                10,
                70,
                min: 1,
                max: 60,
                step: 1),
            Integer(
                AppSettingKeys.GenerationPreviewClipDurationSeconds,
                Generation,
                "Generation Pipeline",
                "Control what the worker creates for newly discovered media.",
                30,
                "Preview clip length",
                "Duration of generated preview videos in seconds.",
                8,
                80,
                min: 2,
                max: 60,
                step: 1),
            Select(
                AppSettingKeys.GenerationThumbnailQuality,
                Generation,
                "Generation Pipeline",
                "Control what the worker creates for newly discovered media.",
                30,
                "Thumbnail quality",
                "Resolution and JPEG quality preset for generated thumbnails.",
                "2",
                90,
                QualityPresetOptions()),
            Select(
                AppSettingKeys.GenerationTrickplayQuality,
                Generation,
                "Generation Pipeline",
                "Control what the worker creates for newly discovered media.",
                30,
                "Trickplay quality",
                "JPEG quality preset for sprite sheets.",
                "2",
                100,
                QualityPresetOptions()),

            Integer(
                AppSettingKeys.JobsBackgroundConcurrency,
                Jobs,
                "Background Jobs",
                "Worker throughput and resource usage.",
                40,
                "Background job concurrency",
                "Parallel jobs per queue in the worker.",
                4,
                10,
                min: 1,
                max: 32,
                step: 1,
                applyHint: "Applies within about 15 seconds after save."),

            Select(
                AppSettingKeys.PlaybackDefaultMode,
                Playback,
                "Playback",
                "Video player defaults.",
                50,
                "Default playback mode",
                "Direct streams the source file. Adaptive HLS uses the on-demand ffmpeg pipeline.",
                "direct",
                10,
                [
                    new SettingOption("direct", "Direct", "Fastest seek, no transcode."),
                    new SettingOption("hls", "Adaptive HLS", "Adaptive bitrate via ffmpeg.")
                ]),
            Boolean(
                AppSettingKeys.PlaybackShowCastControls,
                Playback,
                "Playback",
                "Video player defaults.",
                50,
                "Show cast controls",
                "Shows the cast button in the video player.",
                true,
                20),
            StringList(
                AppSettingKeys.PlaybackAudioPreferredLanguages,
                Playback,
                "Playback",
                "Video player defaults.",
                50,
                "Preferred audio languages",
                "Comma-separated priority list used to pick audio tracks.",
                ["en", "eng", "en-US"],
                30),

            Boolean(
                AppSettingKeys.SubtitlesAutoEnable,
                Subtitles,
                "Subtitles",
                "Default caption behavior and appearance.",
                60,
                "Auto-enable on load",
                "Turn on subtitles automatically when a matching preferred-language track is available.",
                false,
                10),
            StringList(
                AppSettingKeys.SubtitlesPreferredLanguages,
                Subtitles,
                "Subtitles",
                "Default caption behavior and appearance.",
                60,
                "Preferred languages",
                "Comma-separated priority list for subtitle tracks.",
                ["en", "eng"],
                20),
            Select(
                AppSettingKeys.SubtitlesStyle,
                Subtitles,
                "Subtitles",
                "Default caption behavior and appearance.",
                60,
                "Display style",
                "Default caption visual treatment.",
                "stylized",
                30,
                [
                    new SettingOption("stylized", "Stylized", "Outline, shadow, and backing for readability."),
                    new SettingOption("classic", "Classic", "Flat black box with plain white text."),
                    new SettingOption("outline", "Outline", "White text with black stroke and no backing box.")
                ]),
            Decimal(
                AppSettingKeys.SubtitlesFontScale,
                Subtitles,
                "Subtitles",
                "Default caption behavior and appearance.",
                60,
                "Subtitle text size",
                "Font scale multiplier.",
                1m,
                40,
                min: 0.5m,
                max: 3m,
                step: 0.05m),
            Decimal(
                AppSettingKeys.SubtitlesPositionPercent,
                Subtitles,
                "Subtitles",
                "Default caption behavior and appearance.",
                60,
                "Subtitle vertical position",
                "Vertical position as a percentage from the top of the video frame.",
                88m,
                50,
                min: 0m,
                max: 100m,
                step: 1m),
            Decimal(
                AppSettingKeys.SubtitlesOpacity,
                Subtitles,
                "Subtitles",
                "Default caption behavior and appearance.",
                60,
                "Subtitle transparency",
                "Overall caption layer opacity.",
                1m,
                60,
                min: 0.2m,
                max: 1m,
                step: 0.05m),

            Select(
                AppSettingKeys.HlsTranscoderProfile,
                Hls,
                "HLS Transcoding",
                "Encoder and tool paths used for adaptive HLS output.",
                70,
                "HLS transcoder",
                "Encoder used for new adaptive HLS segments.",
                "Auto",
                10,
                [
                    new SettingOption("Auto", "Auto", "Native encoder when safe."),
                    new SettingOption("Software", "Software", "libx264 CPU baseline."),
                    new SettingOption("VideoToolbox", "Apple VT", "macOS hardware path."),
                    new SettingOption("Vaapi", "VA-API", "Intel / AMD Linux."),
                    new SettingOption("Nvenc", "NVENC", "NVIDIA hardware."),
                    new SettingOption("Qsv", "QSV", "Intel Quick Sync.")
                ]),
            String(
                AppSettingKeys.HlsFfmpegPath,
                Hls,
                "HLS Transcoding",
                "Encoder and tool paths used for adaptive HLS output.",
                70,
                "ffmpeg path",
                "Command or absolute path used for ffmpeg.",
                "ffmpeg",
                20,
                inputKind: "path",
                emptyStringUsesDefault: true),
            String(
                AppSettingKeys.HlsVaapiDevice,
                Hls,
                "HLS Transcoding",
                "Encoder and tool paths used for adaptive HLS output.",
                70,
                "VA-API device",
                "Render device path used by VA-API transcodes.",
                "/dev/dri/renderD128",
                30,
                inputKind: "path",
                emptyStringUsesDefault: true),
            Boolean(
                AppSettingKeys.HlsEnableAdaptiveBitrate,
                Hls,
                "HLS Transcoding",
                "Encoder and tool paths used for adaptive HLS output.",
                70,
                "Adaptive bitrate streaming",
                "When on, the player can switch between quality levels (and may spawn a second transcode to do so). Off (the default) serves a single stream, matching the reference media server and keeping CPU bounded to one transcode per viewer.",
                false,
                40),
            Integer(
                AppSettingKeys.HlsEncodingThreadCount,
                Hls,
                "HLS Transcoding",
                "Encoder and tool paths used for adaptive HLS output.",
                70,
                "Encoder thread cap",
                "Maximum CPU threads a single software transcode may use. 0 means automatic, which leaves one core free so a transcode never freezes the rest of the app.",
                0,
                50,
                min: 0,
                max: 64,
                step: 1),

            Integer(
                AppSettingKeys.HlsMaxCacheSizeGb,
                TranscodeCache,
                "Transcode Cache",
                "On-disk cache of transcoded and remuxed video, kept so repeat plays and seeks are instant.",
                75,
                "Maximum cache size (GB)",
                "When the transcode cache grows past this, the least-recently-played cached videos are removed automatically so it never fills the disk. 0 means no limit. Removing cached output is safe — it is regenerated the next time the video is played.",
                10,
                10,
                min: 0,
                max: 1000,
                step: 1),
        };

        return definitions
            .OrderBy(definition => definition.GroupOrder)
            .ThenBy(definition => definition.Order)
            .ToArray();
    }

    private static SettingDefinition Boolean(
        string key,
        string groupKey,
        string groupLabel,
        string groupDescription,
        int groupOrder,
        string label,
        string description,
        bool defaultValue,
        int order,
        string? applyHint = null) =>
        new(
            key,
            groupKey,
            groupLabel,
            groupDescription,
            groupOrder,
            label,
            description,
            SettingValueType.Boolean,
            JsonSerializer.SerializeToElement(defaultValue),
            order,
            applyHint: applyHint);

    private static SettingDefinition Integer(
        string key,
        string groupKey,
        string groupLabel,
        string groupDescription,
        int groupOrder,
        string label,
        string description,
        int defaultValue,
        int order,
        int min,
        int max,
        int step,
        string? applyHint = null) =>
        new(
            key,
            groupKey,
            groupLabel,
            groupDescription,
            groupOrder,
            label,
            description,
            SettingValueType.Integer,
            JsonSerializer.SerializeToElement(defaultValue),
            order,
            new SettingConstraints(min, max, step),
            applyHint: applyHint);

    private static SettingDefinition Decimal(
        string key,
        string groupKey,
        string groupLabel,
        string groupDescription,
        int groupOrder,
        string label,
        string description,
        decimal defaultValue,
        int order,
        decimal min,
        decimal max,
        decimal step) =>
        new(
            key,
            groupKey,
            groupLabel,
            groupDescription,
            groupOrder,
            label,
            description,
            SettingValueType.Decimal,
            JsonSerializer.SerializeToElement(defaultValue),
            order,
            new SettingConstraints(min, max, step));

    private static SettingDefinition String(
        string key,
        string groupKey,
        string groupLabel,
        string groupDescription,
        int groupOrder,
        string label,
        string description,
        string defaultValue,
        int order,
        string? inputKind = null,
        bool emptyStringUsesDefault = false) =>
        new(
            key,
            groupKey,
            groupLabel,
            groupDescription,
            groupOrder,
            label,
            description,
            SettingValueType.String,
            JsonSerializer.SerializeToElement(defaultValue),
            order,
            inputKind: inputKind,
            emptyStringUsesDefault: emptyStringUsesDefault);

    private static SettingDefinition StringList(
        string key,
        string groupKey,
        string groupLabel,
        string groupDescription,
        int groupOrder,
        string label,
        string description,
        IReadOnlyList<string> defaultValue,
        int order) =>
        new(
            key,
            groupKey,
            groupLabel,
            groupDescription,
            groupOrder,
            label,
            description,
            SettingValueType.StringList,
            JsonSerializer.SerializeToElement(defaultValue),
            order);

    private static IReadOnlyList<SettingOption> QualityPresetOptions() => [
        new SettingOption("1", "Best", "Highest resolution, largest files."),
        new SettingOption("2", "High", "Good balance of quality and size."),
        new SettingOption("3", "Medium", "Moderate quality, smaller files."),
        new SettingOption("4", "Low", "Lower quality, space efficient."),
        new SettingOption("5", "Lowest", "Smallest files, lowest quality."),
    ];

    private static SettingDefinition Select(
        string key,
        string groupKey,
        string groupLabel,
        string groupDescription,
        int groupOrder,
        string label,
        string description,
        string defaultValue,
        int order,
        IReadOnlyList<SettingOption> options) =>
        new(
            key,
            groupKey,
            groupLabel,
            groupDescription,
            groupOrder,
            label,
            description,
            SettingValueType.Select,
            JsonSerializer.SerializeToElement(defaultValue),
            order,
            options: options);
}
