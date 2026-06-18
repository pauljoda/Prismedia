using Prismedia.Contracts.Playback;

namespace Prismedia.Application.Playback;

/// <summary>
/// Application service that owns browser-session lifetime and rolling check-ins.
/// </summary>
public sealed class BrowserSessionService {
    private readonly IBrowserSessionPersistence _persistence;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates the service over browser-session persistence.
    /// </summary>
    public BrowserSessionService(IBrowserSessionPersistence persistence, TimeProvider? timeProvider = null) {
        _persistence = persistence;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Ensures a browser session for the optional existing cookie id and returns the refreshed cookie expiry.
    /// </summary>
    public async Task<BrowserSessionResponse> CheckInAsync(
        Guid? requestedSessionId,
        CancellationToken cancellationToken) {
        var now = _timeProvider.GetUtcNow();
        var session = await _persistence.EnsureAsync(
            requestedSessionId,
            now,
            now - BrowserSessionConstants.Retention,
            cancellationToken);

        return new BrowserSessionResponse(session.Id, now + BrowserSessionConstants.Retention);
    }
}
