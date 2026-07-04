namespace Prismedia.Application.Settings;

/// <summary>
/// App-global visibility defaults used when a browser has not chosen a device-local mode.
/// </summary>
public sealed record VisibilitySettings(string DefaultMode, bool LanAutoEnable);

/// <summary>
/// Recurring scan scheduling settings.
/// </summary>
public sealed record ScanSettings(bool AutoScanEnabled, int IntervalMinutes);

/// <summary>
/// Recurring collection refresh scheduling settings.
/// </summary>
public sealed record CollectionRefreshSettings(bool AutoRefreshEnabled);

/// <summary>Cadence for re-searching monitored items so a wanted book is fetched once a release appears.</summary>
public sealed record MonitoredSearchSettings(bool Enabled, int IntervalMinutes);

/// <summary>Recycle-bin settings: a null path means the bin is off (replaced files stay beside the upgrade).</summary>
public sealed record RecycleBinSettings(string? Path, int CleanupDays);

/// <summary>
/// How PROPER/REPACK/RERIP and anime v2+ revisions are treated in ranking and upgrades. Decoded from the
/// stored <see cref="Prismedia.Domain.Entities.ProperDownloadPolicy"/> code; defaults to
/// <see cref="Prismedia.Domain.Entities.ProperDownloadPolicy.PreferAndUpgrade"/> on a missing/unknown value.
/// </summary>
public sealed record ProperDownloadSettings(Prismedia.Domain.Entities.ProperDownloadPolicy Policy);

/// <summary>
/// Auto-identify settings that drive plugin-based identification during library scans.
/// </summary>
/// <param name="Enabled">Whether scanned media is auto-identified through enabled plugins.</param>
/// <param name="Providers">Ordered provider ids tried during auto identify; the first confident match wins.</param>
/// <param name="EntityKinds">Selector kind codes (video, gallery, image, audio, book) auto identify applies to.</param>
/// <param name="ConfidenceThreshold">Minimum confidence as a 0–1 fraction required to auto-apply a non-exact match.</param>
/// <param name="UnorganizedOnly">When true, items already marked organized are skipped.</param>
public sealed record AutoIdentifySettings(
    bool Enabled,
    IReadOnlyList<string> Providers,
    IReadOnlyList<string> EntityKinds,
    double ConfidenceThreshold,
    bool UnorganizedOnly);

/// <summary>
/// Media generation settings used by scan and maintenance jobs.
/// </summary>
public sealed record GenerationSettings(
    bool AutoGenerateMetadata,
    bool AutoGenerateOshash,
    bool AutoGenerateMd5,
    bool AutoGeneratePreview,
    bool GenerateTrickplay,
    int TrickplayIntervalSeconds,
    int PreviewClipDurationSeconds,
    int ThumbnailQuality,
    int TrickplayQuality,
    bool MetadataStorageDedicated);

/// <summary>
/// Worker throughput settings.
/// </summary>
public sealed record WorkerSettings(int BackgroundConcurrency);

/// <summary>
/// Video playback defaults.
/// </summary>
public sealed record PlaybackSettings(
    string DefaultMode,
    bool ShowCastControls,
    IReadOnlyList<string> AudioPreferredLanguages);

/// <summary>
/// Subtitle behavior and appearance defaults.
/// </summary>
public sealed record SubtitleSettings(
    bool AutoEnable,
    IReadOnlyList<string> PreferredLanguages,
    string Style,
    float FontScale,
    float PositionPercent,
    float Opacity);

/// <summary>
/// HLS transcoder and ffmpeg tool settings.
/// </summary>
/// <param name="TranscoderProfile">Encoder profile used for new adaptive HLS segments.</param>
/// <param name="FfmpegPath">Command or absolute path used for ffmpeg.</param>
/// <param name="VaapiDevice">Render device path used by VA-API transcodes.</param>
/// <param name="EnableAdaptiveBitrate">
/// When true the master playlist advertises the full adaptive bitrate ladder so clients can switch
/// quality. When false (the default) it advertises a single rung, matching Jellyfin's single-stream
/// default — this is the primary defence against a quality switch spawning a second concurrent
/// transcode and pinning the CPU.
/// </param>
/// <param name="EncodingThreadCount">
/// Hard cap on ffmpeg encoder threads. 0 (the default) means "auto": leave one core free
/// (<c>ProcessorCount - 1</c>) so a single transcode cannot saturate the whole box.
/// </param>
/// <param name="MaxCacheSizeGb">
/// Maximum size, in gigabytes, of the on-disk transcode/remux cache. When the cache exceeds this,
/// the least-recently-used cached items (that are not currently playing) are evicted. 0 means no
/// limit. Eviction is non-destructive — it only removes cached HLS output, which is regenerated on
/// the next watch.
/// </param>
public sealed record HlsSettings(
    string TranscoderProfile,
    string FfmpegPath,
    string VaapiDevice,
    bool EnableAdaptiveBitrate,
    int EncodingThreadCount,
    int MaxCacheSizeGb);
