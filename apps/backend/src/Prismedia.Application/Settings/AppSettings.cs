using Prismedia.Contracts.Settings;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Settings;

/// <summary>
/// Self-owned definitions for every app-global setting. Adding a public definition field under a
/// public nested group makes it available to runtime lookup and generated clients automatically.
/// </summary>
public static class AppSettings {
    private static readonly IReadOnlyList<SettingOption> QualityPresetOptions = [
        new("1", "Best", "Highest resolution, largest files."),
        new("2", "High", "Good balance of quality and size."),
        new("3", "Medium", "Moderate quality, smaller files."),
        new("4", "Low", "Lower quality, space efficient."),
        new("5", "Lowest", "Smallest files, lowest quality.")
    ];

    public static class Visibility {
        public static readonly SettingGroupDefinition Group = new(
            "visibility", "Content Visibility", "Default adult-content visibility for new browsers.", 10);

        public static readonly SettingDefinition<string> DefaultMode = Select(
            Group, "visibility.defaultMode", "Default NSFW mode",
            "Used when a browser has not chosen its own NSFW mode yet.", "off", 10,
            [
                new("off", "Off (SFW)", "Hide adult content by default."),
                new("show", "Show", "Display all content by default.")
            ]);
    }

    public static class Scan {
        public static readonly SettingGroupDefinition Group = new(
            "scan", "Library Scans", "Control recurring library scans.", 20);

        public static readonly SettingDefinition<bool> AutoScanEnabled = Bool(
            Group, "scan.autoScanEnabled", "Automatic library scans",
            "Queue scans on a recurring interval.", false, 10);
        public static readonly SettingDefinition<int> IntervalMinutes = Int(
            Group, "scan.intervalMinutes", "Scan interval",
            "Minutes between automatic scans.", 60, 20, min: 5, max: 1440, step: 5);
        public static readonly SettingDefinition<int> IntegrityIntervalHours = Int(
            Group, "scan.integrityIntervalHours", "Deep integrity interval",
            "Hours between deep integrity scans, which run the library-wide orphan and " +
            "outside-root cleanups that ordinary scans skip.", 168, 30, min: 24, max: 720, step: 24);

        /// <summary>
        /// Reserved persistence key for the last completed deep integrity sweep. Written by the
        /// scheduler, never surfaced as an editable setting.
        /// </summary>
        public const string LastIntegritySweepAtKey = "scan.lastIntegritySweepAt";
    }

    public static class Collections {
        public static readonly SettingGroupDefinition Group = new(
            "collections", "Collections", "Control recurring collection refresh jobs.", 21);

        public static readonly SettingDefinition<bool> AutoRefreshEnabled = Bool(
            Group, "collections.autoRefreshEnabled", "Auto Refresh Collections",
            "When on, Prismedia queues an hourly job at the top of the hour to evaluate dynamic " +
            "collection rules and update collection membership.", true, 10);
    }

    public static class Monitoring {
        public static readonly SettingGroupDefinition Group = new(
            "monitoring", "Monitoring", "Keep searching for monitored items until they are acquired.", 22);

        public static readonly SettingDefinition<bool> SearchEnabled = Bool(
            Group, "monitoring.searchEnabled", "Re-search monitored items",
            "When on, Prismedia periodically re-runs the release search for monitored items so " +
            "wanted media is fetched once a release appears.", true, 10);
        public static readonly SettingDefinition<int> IntervalMinutes = Int(
            Group, "monitoring.intervalMinutes", "Re-search interval",
            "Minutes between re-searches of monitored items.", 1440, 20, min: 15, max: 10080, step: 15);
    }

    public static class Acquisition {
        public static readonly SettingDefinition<string> RecycleBinPath = Text(
            Monitoring.Group, "acquisition.recycleBinPath", "Recycle bin folder",
            "When set, a file replaced by a quality upgrade is moved here instead of being kept " +
            "beside the new one, and is purged after the cleanup window. Leave blank to keep " +
            "replaced files next to the upgrade as .prismedia-bak.", "", 30);
        public static readonly SettingDefinition<int> RecycleBinCleanupDays = Int(
            Monitoring.Group, "acquisition.recycleBinCleanupDays", "Recycle bin cleanup (days)",
            "Files older than this are deleted from the recycle bin by the daily cleanup.",
            7, 40, min: 1, max: 365, step: 1);
        public static readonly SettingDefinition<string> DownloadPropers = Select(
            Monitoring.Group, "acquisition.downloadPropers", "Download propers and repacks",
            "How PROPER, REPACK, RERIP and anime v2+ re-releases are treated. Stores a " +
            "ProperDownloadPolicy code: prefer-and-upgrade (default) ranks them higher AND upgrades " +
            "an owned copy to a better revision at the same quality; do-not-upgrade ranks them " +
            "higher but never upgrades an owned copy for revision alone; do-not-prefer ignores " +
            "revisions entirely.", ProperDownloadPolicy.PreferAndUpgrade.ToCode(), 50,
            [
                new(ProperDownloadPolicy.PreferAndUpgrade.ToCode(), "Prefer and upgrade",
                    "Rank revisions higher and upgrade an owned copy to a better same-quality revision."),
                new(ProperDownloadPolicy.DoNotUpgrade.ToCode(), "Prefer, do not upgrade",
                    "Rank revisions higher on first grab, but never upgrade an owned copy for revision alone."),
                new(ProperDownloadPolicy.DoNotPrefer.ToCode(), "Do not prefer",
                    "Ignore revisions entirely; only the quality ladder decides.")
            ]);
        public static readonly SettingDefinition<string> PreferredProtocol = Select(
            Monitoring.Group, "acquisition.preferredProtocol", "Preferred download type",
            "When multiple download protocols are enabled, search the preferred type first and use " +
            "the others only when no acceptable preferred release is found.",
            DownloadProtocol.Usenet.ToCode(), 60,
            [
                new(DownloadProtocol.Usenet.ToCode(), "Usenet",
                    "Prefer NZB releases handled by an enabled Usenet client."),
                new(DownloadProtocol.Torrent.ToCode(), "Torrent",
                    "Prefer torrent releases handled by an enabled torrent client."),
                new(DownloadProtocol.Soulseek.ToCode(), "Soulseek",
                    "Prefer peer files handled by an enabled slskd client.")
            ]);
    }

    public static class Taxonomy {
        public static readonly SettingGroupDefinition Group = new(
            "taxonomy", "Library Cleanup", "Automatically prune unused taxonomy entries during scans.", 22);

        public static readonly SettingDefinition<bool> RemoveOrphanTags = Bool(
            Group, "taxonomy.removeOrphanTags", "Remove orphan tags",
            "When on, tags that nothing references are deleted during each library scan, keeping " +
            "the tag list free of stale leftovers. A tag you create but have not applied to anything " +
            "yet counts as orphaned and will be removed on the next scan.", false, 10);
    }

    public static class Identify {
        public static readonly SettingGroupDefinition Group = new(
            "identify", "Identify",
            "Choose the metadata provider each Entity kind starts with in Identify and Request.", 24);

        public static readonly SettingDefinition<IReadOnlyDictionary<string, string>> DefaultProviders = Map(
            Group, "identify.defaultProviders", "Default metadata providers",
            "Maps canonical EntityKind codes to provider ids. A missing or unavailable provider " +
            "falls back to the first compatible enabled provider.", new Dictionary<string, string>(), 10,
            EntityKindRegistry.All.Select(definition => definition.Code).ToArray());
    }

    public static class AutoIdentify {
        public static readonly SettingGroupDefinition Group = new(
            "autoIdentify", "Auto Identify",
            "Let trusted plugins identify and fill new media automatically during scans.", 25);

        public static readonly SettingDefinition<bool> Enabled = Bool(
            Group, "autoIdentify.enabled", "Auto identify during scans",
            "When on, each scanned item runs through your enabled plugins and the first confident " +
            "match is applied automatically — no manual review needed. Requires at least one " +
            "installed plugin and can noticeably increase library scan times.", false, 10);
        public static readonly SettingDefinition<IReadOnlyList<string>> Providers = List(
            Group, "autoIdentify.providers", "Enabled plugins",
            "Plugins tried in order during auto identify. The first one that returns a confident match wins.",
            [], 20);
        public static readonly SettingDefinition<IReadOnlyList<string>> EntityKinds = List(
            Group, "autoIdentify.entityKinds", "Identify these kinds",
            "Which kinds of scanned media auto identify applies to.",
            Enum.GetValues<AutoIdentifySelectorKind>().Select(kind => kind.ToCode()).ToArray(), 30);
        public static readonly SettingDefinition<decimal> ConfidenceThreshold = Decimal(
            Group, "autoIdentify.confidenceThreshold", "Auto-apply confidence",
            "Minimum match confidence (or an exact match) required before a plugin result is applied " +
            "without review.", 90m, 40, min: 0m, max: 100m, step: 1m);
        public static readonly SettingDefinition<bool> UnorganizedOnly = Bool(
            Group, "autoIdentify.unorganizedOnly", "Only un-organized items",
            "When on, skip items already marked organized. Turn off to re-identify every item on each scan.",
            true, 50);
    }

    public static class Generation {
        public static readonly SettingGroupDefinition Group = new(
            "generation", "Generation Pipeline",
            "Control what the worker creates for newly discovered media.", 30);

        public static readonly SettingDefinition<bool> AutoGenerateMetadata = Bool(
            Group, "generation.autoGenerateMetadata", "Technical metadata",
            "Probe runtime, resolution, codec, and bitrate on import.", true, 10);
        public static readonly SettingDefinition<bool> AutoGenerateOshash = Bool(
            Group, "generation.autoGenerateOshash", "OpenSubtitles hash (oshash)",
            "Compute the lightweight oshash from each file's head and tail for fast matching. " +
            "Reads only a small slice of every file.", true, 20);
        public static readonly SettingDefinition<bool> AutoGenerateMd5 = Bool(
            Group, "generation.autoGenerateMd5", "MD5 checksum",
            "Compute a full-file MD5 hash for Stash-compatible checksum matching. Reads every byte " +
            "of every file, so it is slow on large libraries.", false, 25);
        public static readonly SettingDefinition<bool> AutoGeneratePreview = Bool(
            Group, "generation.autoGeneratePreview", "Preview assets",
            "Build thumbnails and short preview clips.", true, 40);
        public static readonly SettingDefinition<bool> GenerateTrickplay = Bool(
            Group, "generation.generateTrickplay", "Trickplay strips",
            "Build sprite sheets for player scrub previews.", true, 50);
        public static readonly SettingDefinition<bool> MetadataStorageDedicated = Bool(
            Group, "generation.metadataStorageDedicated",
            "Store video previews in dedicated cache directory",
            "When on, generated video assets live under the app data volume instead of beside source files.",
            true, 60);
        public static readonly SettingDefinition<int> TrickplayIntervalSeconds = Int(
            Group, "generation.trickplayIntervalSeconds", "Trickplay interval",
            "Seconds between sprite sheet frames.", 10, 70, min: 1, max: 60, step: 1);
        public static readonly SettingDefinition<int> PreviewClipDurationSeconds = Int(
            Group, "generation.previewClipDurationSeconds", "Preview clip length",
            "Duration of generated preview videos in seconds.", 8, 80, min: 2, max: 60, step: 1);
        public static readonly SettingDefinition<string> ThumbnailQuality = Select(
            Group, "generation.thumbnailQuality", "Thumbnail quality",
            "Resolution and JPEG quality preset for generated thumbnails.",
            "2", 90, QualityPresetOptions);
        public static readonly SettingDefinition<string> TrickplayQuality = Select(
            Group, "generation.trickplayQuality", "Trickplay quality",
            "JPEG quality preset for sprite sheets.", "2", 100, QualityPresetOptions);
    }

    public static class Jobs {
        public static readonly SettingGroupDefinition Group = new(
            "jobs", "Background Jobs", "Worker throughput and resource usage.", 40);

        public static readonly SettingDefinition<int> BackgroundConcurrency = Int(
            Group, "jobs.backgroundConcurrency", "Background job concurrency",
            "Parallel jobs per queue in the worker.", 4, 10, min: 1, max: 32, step: 1,
            applyHint: "Applies within about 15 seconds after save.");
    }

    public static class Plugins {
        public static readonly SettingDefinition<bool> AutoUpdateEnabled = Bool(
            Jobs.Group, "plugins.autoUpdateEnabled", "Automatically update plugins",
            "Check for compatible plugin updates when the worker starts and every six hours, then install them automatically.",
            true, 20, applyHint: "Takes effect within about 60 seconds.");
    }

    public static class Playback {
        public static readonly SettingGroupDefinition Group = new(
            "playback", "Playback", "Video player defaults.", 50);

        public static readonly SettingDefinition<string> DefaultMode = Select(
            Group, "playback.defaultMode", "Default playback mode",
            "Direct streams the source file. Adaptive HLS uses the on-demand ffmpeg pipeline.",
            PlaybackMode.Direct.ToCode(), 10,
            [
                new(PlaybackMode.Direct.ToCode(), "Direct", "Fastest seek, no transcode."),
                new(PlaybackMode.Hls.ToCode(), "Adaptive HLS", "Adaptive bitrate via ffmpeg.")
            ]);
        public static readonly SettingDefinition<bool> ShowCastControls = Bool(
            Group, "playback.showCastControls", "Show cast controls",
            "Shows the cast button in the video player.", true, 20);
        public static readonly SettingDefinition<IReadOnlyList<string>> AudioPreferredLanguages = List(
            Group, "playback.audioPreferredLanguages", "Preferred audio languages",
            "Comma-separated priority list used to pick audio tracks.", ["en", "eng", "en-US"], 30);
    }

    public static class Subtitles {
        public static readonly SettingGroupDefinition Group = new(
            "subtitles", "Subtitles", "Default caption behavior and appearance.", 60);

        public static readonly SettingDefinition<bool> AutoEnable = Bool(
            Group, "subtitles.autoEnable", "Auto-enable on load",
            "Turn on subtitles automatically when a matching preferred-language track is available.",
            false, 10);
        public static readonly SettingDefinition<IReadOnlyList<SubtitlePreferenceTerm>> PreferredLanguages = Terms(
            Group, "subtitles.preferredLanguages", "Preferred subtitle terms",
            "Each case-insensitive match adds its weight. Positive weights promote matching tracks; " +
            "negative weights demote them.", [new("English", 100), new("Eng", 80)], 20);
        public static readonly SettingDefinition<bool> AutoDownloadEnabled = Bool(
            Group, "subtitles.autoDownloadEnabled", "Automatically acquire missing subtitles",
            "After local subtitle reconciliation, search configured providers for missing languages " +
            "using strict identity matching.", false, 21);
        public static readonly SettingDefinition<IReadOnlyList<string>> AutoDownloadLanguages = List(
            Group, "subtitles.autoDownloadLanguages", "Automatic download languages",
            "Comma-separated priority list. This is separate from playback selection so enabling " +
            "downloads never changes what the player selects.", ["en"], 22);
        public static readonly SettingDefinition<int> AutoDownloadMinimumConfidence = Int(
            Group, "subtitles.autoDownloadMinimumConfidence", "Minimum match confidence",
            "Automatic downloads require an exact hash or strong episode identity at or above this confidence.",
            90, 23, min: 80, max: 100, step: 1);
        public static readonly SettingDefinition<string> Style = Select(
            Group, "subtitles.style", "Display style", "Default caption visual treatment.",
            "stylized", 30,
            [
                new("stylized", "Stylized", "Outline, shadow, and backing for readability."),
                new("classic", "Classic", "Flat black box with plain white text."),
                new("outline", "Outline", "White text with black stroke and no backing box.")
            ]);
        public static readonly SettingDefinition<decimal> FontScale = Decimal(
            Group, "subtitles.fontScale", "Subtitle text size", "Font scale multiplier.",
            1m, 40, min: .5m, max: 3m, step: .05m);
        public static readonly SettingDefinition<decimal> PositionPercent = Decimal(
            Group, "subtitles.positionPercent", "Subtitle vertical position",
            "Vertical position as a percentage from the top of the video frame.",
            88m, 50, min: 0m, max: 100m, step: 1m);
        public static readonly SettingDefinition<decimal> Opacity = Decimal(
            Group, "subtitles.opacity", "Subtitle transparency", "Overall caption layer opacity.",
            1m, 60, min: .2m, max: 1m, step: .05m);
    }

    public static class Hls {
        public static readonly SettingGroupDefinition Group = new(
            "hls", "HLS Transcoding", "Encoder and tool paths used for adaptive HLS output.", 70);

        public static readonly SettingDefinition<string> TranscoderProfile = Select(
            Group, "hls.transcoderProfile", "HLS transcoder",
            "Encoder used for new adaptive HLS segments.", "Auto", 10,
            [
                new("Auto", "Auto", "Native encoder when safe."),
                new("Software", "Software", "libx264 CPU baseline."),
                new("VideoToolbox", "Apple VT", "macOS hardware path."),
                new("Vaapi", "VA-API", "Intel / AMD Linux."),
                new("Nvenc", "NVENC", "NVIDIA hardware."),
                new("Qsv", "QSV", "Intel Quick Sync.")
            ]);
        public static readonly SettingDefinition<string> FfmpegPath = Text(
            Group, "hls.ffmpegPath", "ffmpeg path", "Command or absolute path used for ffmpeg.",
            "ffmpeg", 20, inputKind: "path", emptyStringUsesDefault: true);
        public static readonly SettingDefinition<string> VaapiDevice = Text(
            Group, "hls.vaapiDevice", "VA-API device",
            "Render device path used by VA-API transcodes.", "/dev/dri/renderD128", 30,
            inputKind: "path", emptyStringUsesDefault: true);
        public static readonly SettingDefinition<bool> EnableAdaptiveBitrate = Bool(
            Group, "hls.enableAdaptiveBitrate", "Adaptive bitrate streaming",
            "When on, the player can switch between quality levels (and may spawn a second " +
            "transcode to do so). Off (the default) serves a single stream, matching the reference " +
            "media server and keeping CPU bounded to one transcode per viewer.", false, 40);
        public static readonly SettingDefinition<int> EncodingThreadCount = Int(
            Group, "hls.encodingThreadCount", "Encoder thread cap",
            "Maximum CPU threads a single software transcode may use. 0 means automatic, which " +
            "leaves one core free so a transcode never freezes the rest of the app.",
            0, 50, min: 0, max: 64, step: 1);
        public static readonly SettingDefinition<int> MaxCacheSizeGb = Int(
            TranscodeCache.Group, "hls.maxCacheSizeGb", "Maximum cache size (GB)",
            "When the transcode cache grows past this, the least-recently-played cached videos are " +
            "removed automatically so it never fills the disk. 0 means no limit. Removing cached " +
            "output is safe — it is regenerated the next time the video is played.",
            10, 10, min: 0, max: 1000, step: 1);
    }

    public static class TranscodeCache {
        public static readonly SettingGroupDefinition Group = new(
            "transcodeCache", "Transcode Cache",
            "On-disk cache of transcoded and remuxed video, kept so repeat plays and seeks are instant.", 75);
    }

    private static SettingDefinition<bool> Bool(
        SettingGroupDefinition group, string key, string label, string description,
        bool defaultValue, int order, string? applyHint = null) =>
        new(key, group, label, description, SettingValueType.Boolean, defaultValue, order,
            SettingValueCodecs.Boolean, applyHint: applyHint);

    private static SettingDefinition<int> Int(
        SettingGroupDefinition group, string key, string label, string description,
        int defaultValue, int order, int min, int max, int step, string? applyHint = null) =>
        new(key, group, label, description, SettingValueType.Integer, defaultValue, order,
            SettingValueCodecs.Integer, new(min, max, step), applyHint: applyHint);

    private static SettingDefinition<decimal> Decimal(
        SettingGroupDefinition group, string key, string label, string description,
        decimal defaultValue, int order, decimal min, decimal max, decimal step) =>
        new(key, group, label, description, SettingValueType.Decimal, defaultValue, order,
            SettingValueCodecs.Decimal, new(min, max, step));

    private static SettingDefinition<string> Text(
        SettingGroupDefinition group, string key, string label, string description,
        string defaultValue, int order, string? inputKind = null, bool emptyStringUsesDefault = false) =>
        new(key, group, label, description, SettingValueType.String, defaultValue, order,
            SettingValueCodecs.String, inputKind: inputKind, emptyStringUsesDefault: emptyStringUsesDefault);

    private static SettingDefinition<IReadOnlyList<string>> List(
        SettingGroupDefinition group, string key, string label, string description,
        IReadOnlyList<string> defaultValue, int order) =>
        new(key, group, label, description, SettingValueType.StringList, defaultValue, order,
            SettingValueCodecs.StringList);

    private static SettingDefinition<IReadOnlyDictionary<string, string>> Map(
        SettingGroupDefinition group, string key, string label, string description,
        IReadOnlyDictionary<string, string> defaultValue, int order, IReadOnlyList<string> allowedKeys) =>
        new(key, group, label, description, SettingValueType.StringMap, defaultValue, order,
            SettingValueCodecs.StringMap, new(MinItems: 0, MaxItems: allowedKeys.Count),
            allowedKeys: allowedKeys);

    private static SettingDefinition<IReadOnlyList<SubtitlePreferenceTerm>> Terms(
        SettingGroupDefinition group, string key, string label, string description,
        IReadOnlyList<SubtitlePreferenceTerm> defaultValue, int order) =>
        new(key, group, label, description, SettingValueType.WeightedTermList, defaultValue, order,
            SettingValueCodecs.WeightedTermList,
            new(Min: -100, Max: 100, Step: 1, MinItems: 0, MaxItems: 32));

    private static SettingDefinition<string> Select(
        SettingGroupDefinition group, string key, string label, string description,
        string defaultValue, int order, IReadOnlyList<SettingOption> options) =>
        new(key, group, label, description, SettingValueType.Select, defaultValue, order,
            SettingValueCodecs.Select, options: options);
}
