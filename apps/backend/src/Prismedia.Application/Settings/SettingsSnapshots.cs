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
