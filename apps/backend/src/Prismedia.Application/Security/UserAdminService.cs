using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Security;

/// <summary>
/// Admin-only user management use cases: account CRUD, password resets, and the
/// last-admin guard that keeps the server administrable.
/// </summary>
public sealed class UserAdminService {
    private readonly ISecurityPersistence _persistence;
    private readonly IPasswordHasher _hasher;

    public UserAdminService(ISecurityPersistence persistence, IPasswordHasher hasher) {
        _persistence = persistence;
        _hasher = hasher;
    }

    /// <summary>Lists all user accounts including disabled ones.</summary>
    public Task<IReadOnlyList<User>> ListUsersAsync(CancellationToken cancellationToken) =>
        _persistence.ListUsersAsync(includeDisabled: true, cancellationToken);

    /// <summary>Finds one user account.</summary>
    public Task<User?> GetUserAsync(Guid userId, CancellationToken cancellationToken) =>
        _persistence.GetUserAsync(userId, cancellationToken);

    /// <summary>Creates a user account with an initial password.</summary>
    public async Task<User> CreateUserAsync(
        string username,
        string? displayName,
        string password,
        UserRole role,
        bool allowSfw,
        bool allowNsfw,
        bool canCreateLibraries,
        bool enabled,
        CancellationToken cancellationToken) {
        var normalizedUsername = UserAuthService.ValidateUsername(username);
        UserAuthService.ValidatePassword(password);
        await EnsureUsernameAvailableAsync(normalizedUsername, excludeUserId: null, cancellationToken);

        var resolvedDisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedUsername : displayName.Trim();
        return await _persistence.CreateUserAsync(
            normalizedUsername,
            resolvedDisplayName,
            _hasher.Hash(password),
            role,
            allowSfw,
            allowNsfw,
            canCreateLibraries,
            enabled,
            cancellationToken);
    }

    /// <summary>
    /// Updates a user account. Demoting or disabling the last enabled admin is rejected
    /// so the server always keeps at least one working admin.
    /// </summary>
    public async Task<User?> UpdateUserAsync(
        Guid userId,
        string? username,
        string? displayName,
        UserRole? role,
        bool? allowSfw,
        bool? allowNsfw,
        bool? canCreateLibraries,
        bool? enabled,
        CancellationToken cancellationToken) {
        var current = await _persistence.GetUserAsync(userId, cancellationToken);
        if (current is null) {
            return null;
        }

        string? normalizedUsername = null;
        if (username is not null) {
            normalizedUsername = UserAuthService.ValidateUsername(username);
            await EnsureUsernameAvailableAsync(normalizedUsername, excludeUserId: userId, cancellationToken);
        }

        var losesAdmin = current.Role == UserRole.Admin && current.Enabled &&
            (role is not null && role != UserRole.Admin || enabled == false);
        if (losesAdmin) {
            await EnsureAnotherEnabledAdminExistsAsync(cancellationToken);
        }

        return await _persistence.UpdateUserAsync(
            userId,
            normalizedUsername,
            displayName?.Trim(),
            role,
            allowSfw,
            allowNsfw,
            canCreateLibraries,
            enabled,
            cancellationToken);
    }

    /// <summary>Resets a user's password and signs them out everywhere.</summary>
    public async Task<bool> SetPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken) {
        UserAuthService.ValidatePassword(newPassword);
        if (!await _persistence.SetPasswordHashAsync(userId, _hasher.Hash(newPassword), cancellationToken)) {
            return false;
        }

        await _persistence.InvalidateSessionsAsync(userId, keepSessionId: null, cancellationToken);
        return true;
    }

    /// <summary>Hard-deletes a user account; the last enabled admin cannot be deleted.</summary>
    public async Task<bool> DeleteUserAsync(Guid userId, CancellationToken cancellationToken) {
        var current = await _persistence.GetUserAsync(userId, cancellationToken);
        if (current is null) {
            return false;
        }

        if (current is { Role: UserRole.Admin, Enabled: true }) {
            await EnsureAnotherEnabledAdminExistsAsync(cancellationToken);
        }

        return await _persistence.DeleteUserAsync(userId, cancellationToken);
    }

    private async Task EnsureUsernameAvailableAsync(
        string username,
        Guid? excludeUserId,
        CancellationToken cancellationToken) {
        var existing = await _persistence.FindUserByUsernameAsync(username, cancellationToken);
        if (existing is not null && existing.Id != excludeUserId) {
            throw new SecurityProblemException(
                ApiProblemCodes.UserInvalid,
                "A user with that username already exists.");
        }
    }

    private async Task EnsureAnotherEnabledAdminExistsAsync(CancellationToken cancellationToken) {
        if (await _persistence.CountEnabledAdminsAsync(cancellationToken) <= 1) {
            throw new SecurityProblemException(
                ApiProblemCodes.LastAdminRequired,
                "At least one enabled admin account is required.");
        }
    }
}
