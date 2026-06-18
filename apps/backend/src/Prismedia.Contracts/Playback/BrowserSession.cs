namespace Prismedia.Contracts.Playback;

/// <summary>
/// Response returned after the server has ensured a browser-scoped persistence session.
/// </summary>
/// <param name="SessionId">Opaque browser-session identifier assigned by Prismedia.</param>
/// <param name="ExpiresAt">UTC time when the browser-session cookie will expire without another check-in.</param>
public sealed record BrowserSessionResponse(Guid SessionId, DateTimeOffset ExpiresAt);
