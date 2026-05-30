namespace Prismedia.Contracts.Security;

/// <summary>Current app API key state shown in Settings.</summary>
public sealed record ApiKeyResponse(
    string ApiKey,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Response returned after rotating the app API key.</summary>
public sealed record ApiKeyRegenerateResponse(
    string ApiKey,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int InvalidatedSessions);

/// <summary>Jellyfin-compatible fake user profile configured by the Prismedia owner.</summary>
public sealed record JellyfinProfileResponse(
    Guid Id,
    string Username,
    string DisplayName,
    bool AllowNsfw,
    bool Enabled,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Request for creating a Jellyfin-compatible fake user profile.</summary>
public sealed record JellyfinProfileCreateRequest(
    string Username,
    string? DisplayName,
    bool AllowNsfw,
    bool Enabled = true);

/// <summary>Request for updating a Jellyfin-compatible fake user profile.</summary>
public sealed record JellyfinProfileUpdateRequest(
    string? Username,
    string? DisplayName,
    bool? AllowNsfw,
    bool? Enabled);

/// <summary>Collection of Jellyfin-compatible fake user profiles.</summary>
public sealed record JellyfinProfilesResponse(IReadOnlyList<JellyfinProfileResponse> Items);
