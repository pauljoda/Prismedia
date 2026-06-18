namespace Prismedia.Application.Playback;

/// <summary>
/// Browser-session persistence constants shared by the API edge, application services, and EF adapter.
/// </summary>
public static class BrowserSessionConstants {
    /// <summary>HttpOnly browser cookie carrying the server-issued browser-session id.</summary>
    public const string CookieName = "prismedia-browser-session";

    /// <summary>Per-browser audio-output preferences such as volume, mute, and mini-player placement.</summary>
    public const string AudioOutputSettingKey = "audio.output";

    /// <summary>Per-browser audio playback queue and transport state.</summary>
    public const string AudioPlaybackStateSettingKey = "audio.playback-state";

    /// <summary>Legacy global app-setting key removed by the browser-session migration.</summary>
    public const string LegacyGlobalMusicPlayerStateKey = "ui.music-player-state";

    /// <summary>How long an unchecked browser session remains live.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);
}
