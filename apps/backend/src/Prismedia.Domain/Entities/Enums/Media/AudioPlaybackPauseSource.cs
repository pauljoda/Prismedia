namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of Prismedia-owned actions that intentionally pause browser audio.
/// </summary>
[CodeFamily("AUDIO_PLAYBACK_PAUSE_SOURCE", "AudioPlaybackPauseSourceCode")]
public enum AudioPlaybackPauseSource {
    /// <summary>The visible player control or its keyboard shortcut paused playback.</summary>
    [Code("user-control")]
    UserControl,

    /// <summary>An operating-system media control paused playback.</summary>
    [Code("media-session")]
    MediaSession,

    /// <summary>Another Prismedia tab claimed audio playback ownership.</summary>
    [Code("tab-displaced")]
    TabDisplaced,

    /// <summary>The user dismissed the global player.</summary>
    [Code("dismiss")]
    Dismiss,

    /// <summary>The player replaced the active media source while advancing the queue.</summary>
    [Code("track-change")]
    TrackChange,

    /// <summary>No Prismedia pause action owned the media element's pause event.</summary>
    [Code("browser")]
    Browser
}
