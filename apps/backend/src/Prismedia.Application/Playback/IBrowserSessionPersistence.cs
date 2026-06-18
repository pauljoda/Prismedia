namespace Prismedia.Application.Playback;

/// <summary>
/// Persistence port for browser-scoped transient UI and playback state.
/// </summary>
public interface IBrowserSessionPersistence {
    /// <summary>
    /// Ensures the requested browser session exists and is fresh, creating a new one when missing,
    /// invalid, or stale. Also prunes sessions older than <paramref name="staleBefore"/>.
    /// </summary>
    Task<BrowserSessionState> EnsureAsync(
        Guid? requestedSessionId,
        DateTimeOffset now,
        DateTimeOffset staleBefore,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads selected setting JSON documents for a browser session.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> LoadSettingsAsync(
        Guid sessionId,
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken);

    /// <summary>
    /// Applies setting upserts and deletes for a browser session in one persistence operation.
    /// </summary>
    Task ReplaceSettingsAsync(
        Guid sessionId,
        IReadOnlyDictionary<string, string> upserts,
        IReadOnlyCollection<string> deletes,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

/// <summary>
/// Persisted browser-session metadata returned by the persistence adapter.
/// </summary>
/// <param name="Id">Server-issued browser-session id.</param>
/// <param name="CreatedAt">UTC creation timestamp.</param>
/// <param name="LastSeenAt">UTC timestamp of the most recent check-in.</param>
public sealed record BrowserSessionState(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt);
