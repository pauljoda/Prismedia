using System.Security.Cryptography;
using System.Text;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Security;

/// <summary>Setup gate state: whether a first admin must still be created.</summary>
public sealed record SetupStatus(bool NeedsSetup, bool HasUsers);

/// <summary>Result of session-less credential verification (OPDS Basic auth).</summary>
public sealed record CredentialVerificationResult(bool IsThrottled, User? User);

/// <summary>
/// Authentication use cases shared by the web portal, native clients, and OPDS:
/// credential verification, session issue/resolve/revoke,
/// first-run setup, and self-service password/profile changes.
/// </summary>
public sealed class UserAuthService {
    /// <summary>Sliding session lifetime; a session dies after this long without activity.</summary>
    public static readonly TimeSpan SessionSlidingWindow = TimeSpan.FromDays(90);

    /// <summary>Last-seen refresh threshold, so resolution does not write on every request.</summary>
    public static readonly TimeSpan SessionTouchStaleness = TimeSpan.FromMinutes(5);

    /// <summary>Minimum accepted password length; no composition rules beyond it.</summary>
    public const int MinPasswordLength = 8;

    // Verified against when the username is unknown so both paths cost one PBKDF2 pass.
    private static string? _timingEqualizerHash;

    private readonly ISecurityPersistence _persistence;
    private readonly IPasswordHasher _hasher;
    private readonly AuthAttemptThrottle _throttle;
    private readonly SessionResolutionCache? _sessionCache;
    private readonly BasicCredentialCache? _credentialCache;

    public UserAuthService(
        ISecurityPersistence persistence,
        IPasswordHasher hasher,
        AuthAttemptThrottle throttle,
        SessionResolutionCache? sessionCache = null,
        BasicCredentialCache? credentialCache = null) {
        _persistence = persistence;
        _hasher = hasher;
        _throttle = throttle;
        _sessionCache = sessionCache;
        _credentialCache = credentialCache;
    }

    /// <summary>
    /// Authenticates a username/password pair and issues a session for web and native clients.
    /// </summary>
    public async Task<UserAuthenticationResult> AuthenticateAsync(
        string? username,
        string? password,
        ClientIdentity client,
        string bucket,
        CancellationToken cancellationToken) {
        if (_throttle.IsThrottled(bucket)) {
            return new UserAuthenticationResult(false, true, null, null, null);
        }

        var candidate = string.IsNullOrWhiteSpace(username)
            ? null
            : await _persistence.FindUserWithPasswordHashByUsernameAsync(username.Trim(), cancellationToken);

        var verification = VerifyPassword(candidate?.PasswordHash, password);
        if (candidate is null || !candidate.User.Enabled || verification == PasswordVerification.Failed) {
            _throttle.RecordFailure(bucket);
            return new UserAuthenticationResult(false, false, null, null, null);
        }

        if (verification == PasswordVerification.SuccessRehashNeeded) {
            await _persistence.SetPasswordHashAsync(candidate.User.Id, _hasher.Hash(password!), cancellationToken);
        }

        var result = await IssueSessionAsync(candidate.User, client, cancellationToken);
        _throttle.RecordSuccess(bucket);
        return result;
    }

    /// <summary>
    /// Verifies a username/password pair without issuing a session. Used by OPDS Basic
    /// auth, which re-sends credentials on every request and must not accumulate
    /// session rows.
    /// </summary>
    public async Task<CredentialVerificationResult> VerifyCredentialsAsync(
        string? username,
        string? password,
        string bucket,
        CancellationToken cancellationToken) {
        // OPDS clients re-send Basic credentials on every request — including one per cover
        // image — and each PBKDF2 verification costs hundreds of milliseconds of CPU. Recently
        // verified credentials short-circuit both the hash and the user lookup.
        if (_credentialCache?.TryGet(username, password) is { } cachedUser) {
            return new CredentialVerificationResult(false, cachedUser);
        }

        if (_throttle.IsThrottled(bucket)) {
            return new CredentialVerificationResult(true, null);
        }

        var candidate = string.IsNullOrWhiteSpace(username)
            ? null
            : await _persistence.FindUserWithPasswordHashByUsernameAsync(username.Trim(), cancellationToken);

        var verification = VerifyPassword(candidate?.PasswordHash, password);
        if (candidate is null || !candidate.User.Enabled || verification == PasswordVerification.Failed) {
            _throttle.RecordFailure(bucket);
            return new CredentialVerificationResult(false, null);
        }

        if (verification == PasswordVerification.SuccessRehashNeeded) {
            await _persistence.SetPasswordHashAsync(candidate.User.Id, _hasher.Hash(password!), cancellationToken);
        }

        _throttle.RecordSuccess(bucket);
        _credentialCache?.Set(username, password, candidate.User);
        return new CredentialVerificationResult(false, candidate.User);
    }

    /// <summary>Resolves an active session token to its session and user.</summary>
    public async Task<UserSessionResolution?> ResolveSessionAsync(string token, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(token)) {
            return null;
        }

        var tokenHash = HashToken(token);
        if (_sessionCache?.TryGet(tokenHash) is { } cached) {
            // Cached hits report Touched=false so cookie refreshes keep their database cadence.
            return cached with { Touched = false };
        }

        var resolution = await _persistence.ResolveSessionAsync(
            tokenHash,
            SessionSlidingWindow,
            SessionTouchStaleness,
            cancellationToken);
        if (resolution is not null) {
            _sessionCache?.Set(tokenHash, resolution);
        }

        return resolution;
    }

    /// <summary>Invalidates the current session (sign out).</summary>
    public Task<bool> LogoutAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken) {
        _sessionCache?.InvalidateSession(sessionId);
        return _persistence.InvalidateSessionAsync(sessionId, userId, cancellationToken);
    }

    /// <summary>Reports whether first-run setup (creating an admin) is still required.</summary>
    public async Task<SetupStatus> GetSetupStatusAsync(CancellationToken cancellationToken) {
        var hasAdmin = await _persistence.AnyEnabledAdminAsync(cancellationToken);
        var hasUsers = hasAdmin || await _persistence.AnyUsersAsync(cancellationToken);
        return new SetupStatus(!hasAdmin, hasUsers);
    }

    /// <summary>
    /// Creates the first admin account and signs it in. Only valid while no enabled admin
    /// exists. When the username matches an existing user, that user is promoted
    /// to an enabled admin with the new password and keeps its id and sessions.
    /// </summary>
    public async Task<UserAuthenticationResult> CreateFirstAdminAsync(
        string username,
        string? displayName,
        string password,
        ClientIdentity client,
        string bucket,
        CancellationToken cancellationToken) {
        if (_throttle.IsThrottled(bucket)) {
            return new UserAuthenticationResult(false, true, null, null, null);
        }

        if (await _persistence.AnyEnabledAdminAsync(cancellationToken)) {
            throw new SecurityProblemException(
                ApiProblemCodes.SetupAlreadyCompleted,
                "Setup has already been completed on this server.");
        }

        var normalizedUsername = ValidateUsername(username);
        ValidatePassword(password);
        var resolvedDisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedUsername : displayName.Trim();
        var passwordHash = _hasher.Hash(password);

        var existing = await _persistence.FindUserByUsernameAsync(normalizedUsername, cancellationToken);
        User admin;
        if (existing is not null) {
            admin = await _persistence.UpdateUserAsync(
                existing.Id,
                username: null,
                displayName: resolvedDisplayName,
                role: UserRole.Admin,
                allowNsfw: null,
                canCreateLibraries: null,
                canRequestContent: null,
                enabled: true,
                cancellationToken) ?? throw new SecurityProblemException(
                    ApiProblemCodes.UserNotFound,
                    "The account being promoted no longer exists.");
            await _persistence.SetPasswordHashAsync(admin.Id, passwordHash, cancellationToken);
        } else {
            admin = await _persistence.CreateUserAsync(
                normalizedUsername,
                resolvedDisplayName,
                passwordHash,
                UserRole.Admin,
                allowNsfw: true,
                canCreateLibraries: true,
                canRequestContent: true,
                enabled: true,
                cancellationToken);
        }

        _throttle.RecordSuccess(bucket);
        return await IssueSessionAsync(admin, client, cancellationToken);
    }

    /// <summary>
    /// Changes the caller's own password after verifying the current one. Other sessions
    /// are invalidated; the current session stays signed in.
    /// </summary>
    public async Task<bool> ChangeOwnPasswordAsync(
        Guid userId,
        Guid currentSessionId,
        string? currentPassword,
        string newPassword,
        CancellationToken cancellationToken) {
        ValidatePassword(newPassword);
        var user = await _persistence.GetUserAsync(userId, cancellationToken);
        if (user is null) {
            return false;
        }

        var candidate = await _persistence.FindUserWithPasswordHashByUsernameAsync(user.Username, cancellationToken);
        if (VerifyPassword(candidate?.PasswordHash, currentPassword) == PasswordVerification.Failed) {
            return false;
        }

        await _persistence.SetPasswordHashAsync(userId, _hasher.Hash(newPassword), cancellationToken);
        await _persistence.InvalidateSessionsAsync(userId, keepSessionId: currentSessionId, cancellationToken);
        _sessionCache?.InvalidateUser(userId);
        _credentialCache?.InvalidateUser(userId);
        return true;
    }

    /// <summary>Updates the caller's own display name.</summary>
    public async Task<User?> UpdateOwnDisplayNameAsync(Guid userId, string displayName, CancellationToken cancellationToken) {
        var trimmed = displayName.Trim();
        if (trimmed.Length is < 1 or > 128) {
            throw new SecurityProblemException(
                ApiProblemCodes.UserInvalid,
                "Display name must be between 1 and 128 characters.");
        }

        var updated = await _persistence.UpdateUserAsync(
            userId,
            username: null,
            displayName: trimmed,
            role: null,
            allowNsfw: null,
            canCreateLibraries: null,
            canRequestContent: null,
            enabled: null,
            cancellationToken);
        _sessionCache?.InvalidateUser(userId);
        return updated;
    }

    /// <summary>Lists the caller's active sessions.</summary>
    public Task<IReadOnlyList<UserSession>> ListOwnSessionsAsync(Guid userId, CancellationToken cancellationToken) =>
        _persistence.ListSessionsAsync(userId, cancellationToken);

    /// <summary>Revokes one of the caller's sessions.</summary>
    public Task<bool> RevokeOwnSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken) {
        _sessionCache?.InvalidateSession(sessionId);
        return _persistence.InvalidateSessionAsync(sessionId, userId, cancellationToken);
    }

    /// <summary>Validates and normalizes a username; 1-64 characters after trimming.</summary>
    public static string ValidateUsername(string? username) {
        var normalized = username?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 64) {
            throw new SecurityProblemException(
                ApiProblemCodes.UserInvalid,
                "Username must be between 1 and 64 characters.");
        }

        return normalized;
    }

    /// <summary>Validates password policy (minimum length only).</summary>
    public static void ValidatePassword(string? password) {
        if (password is null || password.Length < MinPasswordLength) {
            throw new SecurityProblemException(
                ApiProblemCodes.PasswordInvalid,
                $"Password must be at least {MinPasswordLength} characters.");
        }
    }

    /// <summary>Hashes a session token for storage and lookup.</summary>
    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>Generates an opaque url-safe session token.</summary>
    public static string CreateSessionToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private async Task<UserAuthenticationResult> IssueSessionAsync(
        User user,
        ClientIdentity client,
        CancellationToken cancellationToken) {
        var accessToken = CreateSessionToken();
        var session = await _persistence.CreateSessionAsync(user.Id, HashToken(accessToken), client, cancellationToken);
        await _persistence.TouchUserLoginAsync(user.Id, cancellationToken);
        return new UserAuthenticationResult(true, false, user, session, accessToken);
    }

    private PasswordVerification VerifyPassword(string? passwordHash, string? password) {
        if (passwordHash is null || password is null) {
            // Burn one hash verification so unknown-username and wrong-password paths
            // take comparable time.
            _timingEqualizerHash ??= _hasher.Hash("prismedia-timing-equalizer");
            _hasher.Verify(_timingEqualizerHash, password ?? string.Empty);
            return PasswordVerification.Failed;
        }

        return _hasher.Verify(passwordHash, password);
    }
}
