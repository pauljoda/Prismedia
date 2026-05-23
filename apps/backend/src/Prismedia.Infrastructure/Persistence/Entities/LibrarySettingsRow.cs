using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Persistence.Entities;

public sealed class LibrarySettingsRow {
    public Guid Id { get; set; }

    public bool AutoScanEnabled { get; set; }

    public int ScanIntervalMinutes { get; set; } = 60;

    public bool AutoGenerateMetadata { get; set; } = true;

    public bool AutoGenerateFingerprints { get; set; } = true;

    public bool GeneratePhash { get; set; }

    public bool AutoGeneratePreview { get; set; } = true;

    public bool GenerateTrickplay { get; set; } = true;

    public int TrickplayIntervalSeconds { get; set; } = 10;

    public int PreviewClipDurationSeconds { get; set; } = 8;

    public int ThumbnailQuality { get; set; } = 2;

    public int TrickplayQuality { get; set; } = 2;

    public int BackgroundWorkerConcurrency { get; set; } = 1;

    public bool NsfwLanAutoEnable { get; set; }

    public bool HideNsfw { get; set; }

    public bool MetadataStorageDedicated { get; set; } = true;

    public bool SubtitlesAutoEnable { get; set; }

    public string SubtitlesPreferredLanguages { get; set; } = "en,eng";

    public string AudioPreferredLanguages { get; set; } = "en,eng,en-US";

    public SubtitleStyle SubtitleStyle { get; set; } = SubtitleStyle.Stylized;

    public float SubtitleFontScale { get; set; } = 1;

    public float SubtitlePositionPercent { get; set; } = 88;

    public float SubtitleOpacity { get; set; } = 1;

    public PlaybackMode DefaultPlaybackMode { get; set; } = PlaybackMode.Direct;

    public bool ShowCastControls { get; set; } = true;

    public string HlsTranscoderProfile { get; set; } = "Software";

    public string HlsFfmpegPath { get; set; } = "ffmpeg";

    public string HlsVaapiDevice { get; set; } = "/dev/dri/renderD128";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
