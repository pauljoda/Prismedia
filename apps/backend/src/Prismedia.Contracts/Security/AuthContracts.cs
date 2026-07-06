using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Security;

/// <summary>Whether first-run setup (creating the initial admin) is still required.</summary>
/// <param name="NeedsSetup">True while no enabled admin account exists.</param>
/// <param name="HasUsers">True when accounts exist (migrated installs), so the wizard can offer promoting one.</param>
public sealed record SetupStatusResponse(bool NeedsSetup, bool HasUsers);

/// <summary>Request creating the first admin account during setup.</summary>
public sealed record CreateFirstAdminRequest(string Username, string Password, string? DisplayName = null);

/// <summary>Username/password login request. Optional client fields describe the device for the sessions list.</summary>
public sealed record LoginRequest(
    string Username,
    string Password,
    string? Client = null,
    string? DeviceName = null,
    string? DeviceId = null);

/// <summary>Successful login: the bearer token for native clients plus the signed-in user.</summary>
public sealed record LoginResponse(string AccessToken, UserResponse User);

/// <summary>Request changing the caller's own password.</summary>
public sealed record ChangeOwnPasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>Request updating the caller's own profile.</summary>
public sealed record UpdateOwnProfileRequest(string DisplayName);

/// <summary>One active session (device) of the current user.</summary>
public sealed record UserSessionResponse(
    Guid Id,
    string? Client,
    string? DeviceName,
    string? DeviceId,
    string? ApplicationVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    bool IsCurrent);

/// <summary>The current user's active sessions.</summary>
public sealed record UserSessionsResponse(IReadOnlyList<UserSessionResponse> Items);

/// <summary>A Prismedia user account as exposed to the web portal and admin screens.</summary>
/// <param name="LibraryRootIds">
/// Library roots the user can access; populated on admin listings only and null elsewhere.
/// Admins implicitly access every library regardless of this list.
/// </param>
public sealed record UserResponse(
    Guid Id,
    string Username,
    string DisplayName,
    UserRole Role,
    bool AllowSfw,
    bool AllowNsfw,
    bool CanCreateLibraries,
    bool Enabled,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<Guid>? LibraryRootIds = null);
