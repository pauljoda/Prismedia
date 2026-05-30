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
    bool GeneratePhash,
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
public sealed record HlsSettings(
    string TranscoderProfile,
    string FfmpegPath,
    string VaapiDevice);
