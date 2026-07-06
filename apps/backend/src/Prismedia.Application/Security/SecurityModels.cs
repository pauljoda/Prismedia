using Prismedia.Domain.Entities;

namespace Prismedia.Application.Security;

/// <summary>Singleton app security state persisted by infrastructure (server identity).</summary>
public sealed record AppSecurityState(
    int Id,
    Guid ServerId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Application model for a Prismedia user account.</summary>
public sealed record User(
    Guid Id,
    string Username,
    string DisplayName,
    UserRole Role,
    bool AllowSfw,
    bool AllowNsfw,
    bool CanCreateLibraries,
    bool Enabled,
    bool HasPassword,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Application model for one authenticated user session (web, native, Jellyfin, or OPDS).</summary>
public sealed record UserSession(
    Guid Id,
    Guid UserId,
    string TokenHash,
    string? Client,
    string? DeviceName,
    string? DeviceId,
    string? ApplicationVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? InvalidatedAt);

/// <summary>
/// Resolved session plus its owning user. <paramref name="Touched"/> is true when this
/// resolution refreshed the session's sliding last-seen timestamp, which signals the
/// middleware to re-issue the browser cookie.
/// </summary>
public sealed record UserSessionResolution(UserSession Session, User User, bool Touched);

/// <summary>Device/client metadata supplied during login (Jellyfin wire concept, reused for web logins).</summary>
public sealed record JellyfinClientIdentity(
    string? Client,
    string? DeviceName,
    string? DeviceId,
    string? ApplicationVersion);

/// <summary>Result for username/password authentication across web, Jellyfin, and OPDS.</summary>
public sealed record UserAuthenticationResult(
    bool Succeeded,
    bool IsThrottled,
    User? User,
    UserSession? Session,
    string? AccessToken);
