using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Security;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Security;

/// <summary>EF-backed persistence for users, sessions, and app security state.</summary>
public sealed class EfSecurityPersistence : ISecurityPersistence {
    private const int SingletonSecurityId = 1;
    private const int ClientMaxLength = 128;
    private const int DeviceNameMaxLength = 128;
    private const int DeviceIdMaxLength = 256;
    private const int ApplicationVersionMaxLength = 64;

    private readonly PrismediaDbContext _db;

    public EfSecurityPersistence(PrismediaDbContext db) {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<AppSecurityState> EnsureAppSecurityAsync(CancellationToken cancellationToken) {
        var state = await _db.AppSecurity.FirstOrDefaultAsync(row => row.Id == SingletonSecurityId, cancellationToken);
        if (state is null) {
            var now = DateTimeOffset.UtcNow;
            state = new AppSecurityRow {
                Id = SingletonSecurityId,
                ServerId = Guid.NewGuid(),
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.AppSecurity.Add(state);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return ToState(state);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<User>> ListUsersAsync(bool includeDisabled, CancellationToken cancellationToken) =>
        await _db.Users.AsNoTracking()
            .Where(row => includeDisabled || row.Enabled)
            .OrderBy(row => row.Username)
            .Select(row => ToUser(row))
            .ToArrayAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<User?> GetUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await _db.Users.AsNoTracking()
            .Where(row => row.Id == userId)
            .Select(row => ToUser(row))
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<User?> FindUserByUsernameAsync(string username, CancellationToken cancellationToken) {
        var normalized = NormalizeUsername(username);
        return await _db.Users.AsNoTracking()
            .Where(row => row.NormalizedUsername == normalized)
            .Select(row => ToUser(row))
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<UserWithPasswordHash?> FindUserWithPasswordHashByUsernameAsync(
        string username,
        CancellationToken cancellationToken) {
        var normalized = NormalizeUsername(username);
        var row = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(user => user.NormalizedUsername == normalized, cancellationToken);
        return row is null ? null : new UserWithPasswordHash(ToUser(row), row.PasswordHash);
    }

    /// <inheritdoc />
    public Task<bool> AnyEnabledAdminAsync(CancellationToken cancellationToken) =>
        _db.Users.AnyAsync(
            row => row.Role == UserRole.Admin && row.Enabled && row.PasswordHash != null,
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> AnyUsersAsync(CancellationToken cancellationToken) =>
        _db.Users.AnyAsync(cancellationToken);

    /// <inheritdoc />
    public Task<int> CountEnabledAdminsAsync(CancellationToken cancellationToken) =>
        _db.Users.CountAsync(
            row => row.Role == UserRole.Admin && row.Enabled && row.PasswordHash != null,
            cancellationToken);

    /// <inheritdoc />
    public async Task<User> CreateUserAsync(
        string username,
        string displayName,
        string? passwordHash,
        UserRole role,
        bool allowSfw,
        bool allowNsfw,
        bool canCreateLibraries,
        bool enabled,
        CancellationToken cancellationToken) {
        var normalized = NormalizeUsername(username);
        if (await _db.Users.AnyAsync(row => row.NormalizedUsername == normalized, cancellationToken)) {
            throw new InvalidOperationException("A user with that username already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var row = new UserRow {
            Id = Guid.NewGuid(),
            Username = username,
            NormalizedUsername = normalized,
            DisplayName = displayName,
            PasswordHash = passwordHash,
            PasswordUpdatedAt = passwordHash is null ? null : now,
            Role = role,
            AllowSfw = allowSfw,
            AllowNsfw = allowNsfw,
            CanCreateLibraries = canCreateLibraries,
            Enabled = enabled,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.Users.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
        return ToUser(row);
    }

    /// <inheritdoc />
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
        var row = await _db.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);
        if (row is null) {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(username)) {
            var normalized = NormalizeUsername(username);
            var duplicate = await _db.Users.AnyAsync(
                user => user.Id != userId && user.NormalizedUsername == normalized,
                cancellationToken);
            if (duplicate) {
                throw new InvalidOperationException("A user with that username already exists.");
            }

            row.Username = username;
            row.NormalizedUsername = normalized;
        }

        if (displayName is not null) {
            row.DisplayName = string.IsNullOrWhiteSpace(displayName) ? row.Username : displayName;
        }

        if (role is { } newRole) {
            row.Role = newRole;
        }

        if (allowSfw is { } allowSafe) {
            row.AllowSfw = allowSafe;
        }

        if (allowNsfw is { } allow) {
            row.AllowNsfw = allow;
        }

        if (canCreateLibraries is { } canCreate) {
            row.CanCreateLibraries = canCreate;
        }

        if (enabled is { } isEnabled) {
            row.Enabled = isEnabled;
            if (!isEnabled) {
                await InvalidateSessionRowsAsync(userId, keepSessionId: null, cancellationToken);
            }
        }

        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return ToUser(row);
    }

    /// <inheritdoc />
    public async Task<bool> SetPasswordHashAsync(Guid userId, string passwordHash, CancellationToken cancellationToken) {
        var row = await _db.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);
        if (row is null) {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        row.PasswordHash = passwordHash;
        row.PasswordUpdatedAt = now;
        row.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteUserAsync(Guid userId, CancellationToken cancellationToken) {
        var row = await _db.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);
        if (row is null) {
            return false;
        }

        _db.Users.Remove(row);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<UserSession> CreateSessionAsync(
        Guid userId,
        string tokenHash,
        JellyfinClientIdentity client,
        CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var row = new UserSessionRow {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            Client = Truncate(client.Client, ClientMaxLength),
            DeviceName = Truncate(client.DeviceName, DeviceNameMaxLength),
            DeviceId = Truncate(client.DeviceId, DeviceIdMaxLength),
            ApplicationVersion = Truncate(client.ApplicationVersion, ApplicationVersionMaxLength),
            CreatedAt = now,
            LastSeenAt = now
        };
        _db.UserSessions.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
        return ToSession(row);
    }

    /// <inheritdoc />
    public async Task<UserSessionResolution?> ResolveSessionAsync(
        string tokenHash,
        TimeSpan slidingWindow,
        TimeSpan touchStaleness,
        CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var expiryFloor = now - slidingWindow;
        var result = await (
            from session in _db.UserSessions
            join user in _db.Users on session.UserId equals user.Id
            where session.TokenHash == tokenHash &&
                session.InvalidatedAt == null &&
                session.LastSeenAt > expiryFloor &&
                user.Enabled
            select new { Session = session, User = user })
            .FirstOrDefaultAsync(cancellationToken);
        if (result is null) {
            return null;
        }

        var touched = now - result.Session.LastSeenAt > touchStaleness;
        if (touched) {
            result.Session.LastSeenAt = now;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new UserSessionResolution(ToSession(result.Session), ToUser(result.User), touched);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserSession>> ListSessionsAsync(Guid userId, CancellationToken cancellationToken) =>
        await _db.UserSessions.AsNoTracking()
            .Where(row => row.UserId == userId && row.InvalidatedAt == null)
            .OrderByDescending(row => row.LastSeenAt)
            .Select(row => ToSession(row))
            .ToArrayAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> InvalidateSessionAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken) {
        var row = await _db.UserSessions.FirstOrDefaultAsync(
            session => session.Id == sessionId && session.UserId == userId && session.InvalidatedAt == null,
            cancellationToken);
        if (row is null) {
            return false;
        }

        row.InvalidatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<int> InvalidateSessionsAsync(Guid userId, Guid? keepSessionId, CancellationToken cancellationToken) {
        var invalidated = await InvalidateSessionRowsAsync(userId, keepSessionId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return invalidated;
    }

    /// <inheritdoc />
    public async Task TouchUserLoginAsync(Guid userId, CancellationToken cancellationToken) {
        var row = await _db.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);
        if (row is null) {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        row.LastLoginAt = now;
        row.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> InvalidateSessionRowsAsync(Guid userId, Guid? keepSessionId, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var sessions = await _db.UserSessions
            .Where(session => session.UserId == userId && session.InvalidatedAt == null &&
                (keepSessionId == null || session.Id != keepSessionId))
            .ToArrayAsync(cancellationToken);
        foreach (var session in sessions) {
            session.InvalidatedAt = now;
        }

        return sessions.Length;
    }

    internal static string NormalizeUsername(string username) => username.Trim().ToLowerInvariant();

    private static string? Truncate(string? value, int maxLength) =>
        value is { Length: > 0 }
            ? value.Length <= maxLength ? value : value[..maxLength]
            : value;

    private static AppSecurityState ToState(AppSecurityRow row) =>
        new(row.Id, row.ServerId, row.CreatedAt, row.UpdatedAt);

    private static User ToUser(UserRow row) =>
        new(
            row.Id,
            row.Username,
            row.DisplayName,
            row.Role,
            row.AllowSfw,
            row.AllowNsfw,
            row.CanCreateLibraries,
            row.Enabled,
            row.PasswordHash != null,
            row.LastLoginAt,
            row.CreatedAt,
            row.UpdatedAt);

    private static UserSession ToSession(UserSessionRow row) =>
        new(
            row.Id,
            row.UserId,
            row.TokenHash,
            row.Client,
            row.DeviceName,
            row.DeviceId,
            row.ApplicationVersion,
            row.CreatedAt,
            row.LastSeenAt,
            row.InvalidatedAt);
}
