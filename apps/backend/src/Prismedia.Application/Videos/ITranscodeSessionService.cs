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
    /// Returns true when a play session is currently registered for the given item.
    /// Jellyfin clients may hand media players a stream URL that carries only this
    /// session id, so stream endpoints use it as item-scoped playback authorization.
    /// </summary>
    /// <param name="playSessionId">Client playback session id.</param>
    /// <param name="itemId">Media item expected to belong to the session.</param>
    bool IsRegisteredForItem(string playSessionId, Guid itemId);

    /// <summary>
    /// Cancels one play session and any associated transcoding work.
    /// </summary>
    Task CancelAsync(string playSessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Cancels all currently tracked encodings and returns how many sessions were cleared.
    /// </summary>
    Task<int> CancelAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns the distinct item ids whose play session has sent a heartbeat within the given window.
    /// Used by the stale-job reaper to decide which encodings still have a live viewer.
    /// </summary>
    /// <param name="within">Maximum age of the last heartbeat for a session to count as live.</param>
    IReadOnlySet<Guid> LiveItemIds(TimeSpan within);

    /// <summary>
    /// Drops play sessions whose last heartbeat is older than the given time-to-live and returns how
    /// many were removed. Cancelling the orphaned encodings themselves is left to the caller, which
    /// reconciles against the live-item set so it also reaps work whose session was never recorded.
    /// </summary>
    /// <param name="ttl">Maximum age of the last heartbeat before a session is considered abandoned.</param>
    int ReapStaleSessions(TimeSpan ttl);
}
