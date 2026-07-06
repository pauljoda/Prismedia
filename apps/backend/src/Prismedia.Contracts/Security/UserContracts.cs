using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Security;

/// <summary>Collection of user accounts for the admin management screen.</summary>
public sealed record UsersResponse(IReadOnlyList<UserResponse> Items);

/// <summary>Admin request creating a user account.</summary>
public sealed record UserCreateRequest(
    string Username,
    string Password,
    string? DisplayName = null,
    UserRole Role = UserRole.Member,
    bool AllowSfw = true,
    bool AllowNsfw = false,
    bool CanCreateLibraries = false,
    bool Enabled = true);

/// <summary>Admin request updating a user account; null fields are left unchanged.</summary>
public sealed record UserUpdateRequest(
    string? Username = null,
    string? DisplayName = null,
    UserRole? Role = null,
    bool? AllowSfw = null,
    bool? AllowNsfw = null,
    bool? CanCreateLibraries = null,
    bool? Enabled = null);

/// <summary>Admin request resetting a user's password; the user is signed out everywhere.</summary>
public sealed record AdminSetPasswordRequest(string NewPassword);
