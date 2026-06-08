namespace Prismedia.Application.Security;

/// <summary>Singleton app security state persisted by infrastructure.</summary>
public sealed record AppSecurityState(
    int Id,
    Guid ServerId,
    string ApiKey,
    bool DefaultProfileSeeded,
    DateTimeOffset ApiKeyCreatedAt,
    DateTimeOffset ApiKeyUpdatedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Application model for a Jellyfin-compatible fake user profile.</summary>
public sealed record JellyfinProfile(
    Guid Id,
    string Username,
    string DisplayName,
    bool AllowSfw,
    bool AllowNsfw,
    bool Enabled,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Application model for one authenticated Jellyfin-compatible session.</summary>
public sealed record JellyfinSession(
    Guid Id,
    Guid ProfileId,
    string TokenHash,
    string? Client,
    string? DeviceName,
    string? DeviceId,
    string? ApplicationVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? InvalidatedAt);

/// <summary>Resolved Jellyfin session plus the profile that owns it.</summary>
public sealed record JellyfinSessionResolution(JellyfinSession Session, JellyfinProfile Profile);

/// <summary>Device/client metadata supplied by Jellyfin-compatible clients during login.</summary>
public sealed record JellyfinClientIdentity(
    string? Client,
    string? DeviceName,
    string? DeviceId,
    string? ApplicationVersion);

/// <summary>Result returned when rotating the app key.</summary>
public sealed record ApiKeyRotationResult(AppSecurityState State, int InvalidatedSessions);

/// <summary>Result for validating an app API key attempt.</summary>
public sealed record ApiKeyValidationResult(bool IsValid, bool IsThrottled);

/// <summary>Result for Jellyfin username/password authentication.</summary>
public sealed record JellyfinProfileAuthenticationResult(
    bool Succeeded,
    bool IsThrottled,
    JellyfinProfile? Profile,
    JellyfinSession? Session,
    string? AccessToken);
