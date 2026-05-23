namespace Prismedia.Application.Videos;

/// <summary>
/// Tracks active ffmpeg work by play session so clients can ping, stop, or cancel encodings.
/// </summary>
public interface ITranscodeSessionService {
    /// <summary>
    /// Registers a playback session that may own transcoded HLS assets.
    /// </summary>
    void Register(string playSessionId, Guid itemId);

    /// <summary>
    /// Updates the heartbeat timestamp for a play session.
    /// </summary>
    void Ping(string playSessionId);

    /// <summary>
    /// Cancels one play session and any associated transcoding work.
    /// </summary>
    Task CancelAsync(string playSessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Cancels all currently tracked encodings and returns how many sessions were cleared.
    /// </summary>
    Task<int> CancelAllAsync(CancellationToken cancellationToken);
}
