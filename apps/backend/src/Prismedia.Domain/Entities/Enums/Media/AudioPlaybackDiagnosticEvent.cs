namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of browser audio lifecycle transitions reported for playback diagnostics.
/// </summary>
[CodeFamily("AUDIO_PLAYBACK_DIAGNOSTIC_EVENT", "AudioPlaybackDiagnosticEventCode")]
public enum AudioPlaybackDiagnosticEvent {
    /// <summary>The media element has enough data and playback is actively advancing.</summary>
    [Code("playing")]
    Playing,

    /// <summary>The media element entered its paused state.</summary>
    [Code("pause")]
    Pause,

    /// <summary>Playback stopped because the next media data was not ready.</summary>
    [Code("waiting")]
    Waiting,

    /// <summary>The browser stopped receiving media data while trying to play.</summary>
    [Code("stalled")]
    Stalled,

    /// <summary>The media element reported a terminal playback error.</summary>
    [Code("error")]
    Error
}
