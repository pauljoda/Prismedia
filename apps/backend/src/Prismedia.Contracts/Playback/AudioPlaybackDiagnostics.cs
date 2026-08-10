using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Playback;

/// <summary>
/// One low-volume browser audio lifecycle transition used to diagnose intermittent playback stalls.
/// </summary>
/// <param name="Event">Media lifecycle transition being reported.</param>
/// <param name="TrackId">Audio track loaded by the media element.</param>
/// <param name="PositionSeconds">Media playhead position when the transition occurred.</param>
/// <param name="DurationSeconds">Known media duration, when finite.</param>
/// <param name="BufferedAheadSeconds">Contiguous buffered media available after the playhead.</param>
/// <param name="ReadyState">Browser HTMLMediaElement readyState numeric value.</param>
/// <param name="NetworkState">Browser HTMLMediaElement networkState numeric value.</param>
/// <param name="Paused">Whether the media element reports itself paused.</param>
/// <param name="Ended">Whether the media element reached its natural end.</param>
/// <param name="PlayIntent">Whether Prismedia still intends playback to be active.</param>
/// <param name="DocumentVisible">Whether the player document is visible.</param>
/// <param name="DocumentHasFocus">Whether the player document has focus.</param>
/// <param name="PauseSource">Prismedia action owning a pause, or Browser when no action owns it.</param>
/// <param name="InterruptionMilliseconds">Elapsed time from the first waiting signal until playback recovered.</param>
/// <param name="MediaErrorCode">HTMLMediaElement error code, when an error exists.</param>
public sealed record AudioPlaybackDiagnosticRequest(
    AudioPlaybackDiagnosticEvent Event,
    Guid TrackId,
    double PositionSeconds,
    double? DurationSeconds,
    double BufferedAheadSeconds,
    int ReadyState,
    int NetworkState,
    bool Paused,
    bool Ended,
    bool PlayIntent,
    bool DocumentVisible,
    bool DocumentHasFocus,
    AudioPlaybackPauseSource? PauseSource,
    long? InterruptionMilliseconds,
    int? MediaErrorCode);
