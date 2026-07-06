using Prismedia.Domain.Entities;

namespace Prismedia.Application.Security;

/// <summary>A user together with its stored password hash, for credential verification only.</summary>
public sealed record UserWithPasswordHash(User User, string? PasswordHash);

/// <summary>Infrastructure persistence port for users, sessions, and app security state.</summary>
public interface ISecurityPersistence {
    /// <summary>Gets the singleton app security state (server identity), creating it when absent.</summary>
    Task<AppSecurityState> EnsureAppSecurityAsync(CancellationToken cancellationToken);

    /// <summary>Lists user accounts ordered by username.</summary>
    Task<IReadOnlyList<User>> ListUsersAsync(bool includeDisabled, CancellationToken cancellationToken);

    /// <summary>Finds one user by id.</summary>
    Task<User?> GetUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Finds one user by username (case-insensitive).</summary>
    Task<User?> FindUserByUsernameAsync(string username, CancellationToken cancellationToken);

    /// <summary>Finds one user with its password hash for credential verification.</summary>
    Task<UserWithPasswordHash?> FindUserWithPasswordHashByUsernameAsync(string username, CancellationToken cancellationToken);

    /// <summary>True when at least one enabled admin account exists (setup is complete).</summary>
    Task<bool> AnyEnabledAdminAsync(CancellationToken cancellationToken);

    /// <summary>True when any user account exists (distinguishes fresh installs from upgrades).</summary>
    Task<bool> AnyUsersAsync(CancellationToken cancellationToken);

    /// <summary>Counts enabled admin accounts, for the last-admin guard.</summary>
    Task<int> CountEnabledAdminsAsync(CancellationToken cancellationToken);

    /// <summary>Creates a user account.</summary>
    Task<User> CreateUserAsync(
        string username,
        string displayName,
        string? passwordHash,
        UserRole role,
        bool allowSfw,
        bool allowNsfw,
        bool canCreateLibraries,
        bool enabled,
        CancellationToken cancellationToken);

    /// <summary>Updates a user account; disabling invalidates its active sessions.</summary>
    Task<User?> UpdateUserAsync(
        Guid userId,
        string? username,
        string? displayName,
        UserRole? role,
        bool? allowSfw,
        bool? allowNsfw,
        bool? canCreateLibraries,
        bool? enabled,
        CancellationToken cancellationToken);

    /// <summary>Replaces a user's password hash.</summary>
    Task<bool> SetPasswordHashAsync(Guid userId, string passwordHash, CancellationToken cancellationToken);

    /// <summary>Hard-deletes a user account; sessions cascade.</summary>
    Task<bool> DeleteUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Creates a session for a user.</summary>
    Task<UserSession> CreateSessionAsync(
        Guid userId,
        string tokenHash,
        JellyfinClientIdentity client,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves an active session by token hash. A session is active while not invalidated
    /// and last seen within <paramref name="slidingWindow"/>; the last-seen timestamp is
    /// refreshed (and <see cref="UserSessionResolution.Touched"/> reported) only when it is
    /// staler than <paramref name="touchStaleness"/>, to avoid a write per request.
    /// </summary>
    Task<UserSessionResolution?> ResolveSessionAsync(
        string tokenHash,
        TimeSpan slidingWindow,
        TimeSpan touchStaleness,
        CancellationToken cancellationToken);

    /// <summary>Lists a user's sessions, most recently seen first (active only).</summary>
    Task<IReadOnlyList<UserSession>> ListSessionsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Invalidates one session owned by the given user.</summary>
    Task<bool> InvalidateSessionAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Invalidates all of a user's sessions except an optional one to keep.</summary>
    Task<int> InvalidateSessionsAsync(Guid userId, Guid? keepSessionId, CancellationToken cancellationToken);

    /// <summary>Records a successful login.</summary>
    Task TouchUserLoginAsync(Guid userId, CancellationToken cancellationToken);
}
