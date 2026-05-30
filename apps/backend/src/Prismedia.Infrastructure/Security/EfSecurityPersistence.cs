using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Security;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Security;

/// <summary>EF-backed persistence for app API key and Jellyfin compatibility profiles.</summary>
public sealed class EfSecurityPersistence : ISecurityPersistence {
    private const int SingletonSecurityId = 1;
    private const string DefaultProfileUsername = "Prismedia";

    private readonly PrismediaDbContext _db;

    public EfSecurityPersistence(PrismediaDbContext db) {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<AppSecurityState> EnsureSecurityAsync(Func<string> keyFactory, CancellationToken cancellationToken) {
        var state = await _db.AppSecurity.FirstOrDefaultAsync(row => row.Id == SingletonSecurityId, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (state is null) {
            state = new AppSecurityRow {
                Id = SingletonSecurityId,
                ServerId = Guid.NewGuid(),
                ApiKey = keyFactory(),
                ApiKeyCreatedAt = now,
                ApiKeyUpdatedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.AppSecurity.Add(state);
        }

        if (!state.DefaultProfileSeeded) {
            var normalized = NormalizeUsername(DefaultProfileUsername);
            var hasDefaultProfile = await _db.JellyfinProfiles
                .AnyAsync(row => row.NormalizedUsername == normalized, cancellationToken);
            if (!hasDefaultProfile) {
                _db.JellyfinProfiles.Add(new JellyfinProfileRow {
                    Id = Guid.NewGuid(),
                    Username = DefaultProfileUsername,
                    NormalizedUsername = normalized,
                    DisplayName = DefaultProfileUsername,
                    AllowNsfw = false,
                    Enabled = true,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            state.DefaultProfileSeeded = true;
            state.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return ToState(state);
    }

    /// <inheritdoc />
    public Task<AppSecurityState> GetSecurityAsync(Func<string> keyFactory, CancellationToken cancellationToken) =>
        EnsureSecurityAsync(keyFactory, cancellationToken);

    /// <inheritdoc />
    public async Task<ApiKeyRotationResult> RotateApiKeyAsync(string apiKey, CancellationToken cancellationToken) {
        var state = await _db.AppSecurity.FirstOrDefaultAsync(row => row.Id == SingletonSecurityId, cancellationToken);
        if (state is null) {
            state = new AppSecurityRow {
                Id = SingletonSecurityId,
                ServerId = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.AppSecurity.Add(state);
        }

        var now = DateTimeOffset.UtcNow;
        state.ApiKey = apiKey;
        state.ApiKeyCreatedAt = now;
        state.ApiKeyUpdatedAt = now;
        state.UpdatedAt = now;

        var activeSessions = await _db.JellyfinSessions
            .Where(row => row.InvalidatedAt == null)
            .ToArrayAsync(cancellationToken);
        foreach (var session in activeSessions) {
            session.InvalidatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new ApiKeyRotationResult(ToState(state), activeSessions.Length);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JellyfinProfile>> ListProfilesAsync(bool includeDisabled, CancellationToken cancellationToken) =>
        await _db.JellyfinProfiles.AsNoTracking()
            .Where(row => includeDisabled || row.Enabled)
            .OrderBy(row => row.Username)
            .Select(row => ToProfile(row))
            .ToArrayAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<JellyfinProfile?> GetProfileAsync(Guid profileId, CancellationToken cancellationToken) =>
        await _db.JellyfinProfiles.AsNoTracking()
            .Where(row => row.Id == profileId)
            .Select(row => ToProfile(row))
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<JellyfinProfile?> FindProfileByUsernameAsync(string username, CancellationToken cancellationToken) {
        var normalized = NormalizeUsername(username);
        return await _db.JellyfinProfiles.AsNoTracking()
            .Where(row => row.NormalizedUsername == normalized)
            .Select(row => ToProfile(row))
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<JellyfinProfile> CreateProfileAsync(
        string username,
        string displayName,
        bool allowNsfw,
        bool enabled,
        CancellationToken cancellationToken) {
        var normalized = NormalizeUsername(username);
        if (await _db.JellyfinProfiles.AnyAsync(row => row.NormalizedUsername == normalized, cancellationToken)) {
            throw new InvalidOperationException("A Jellyfin profile with that username already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var row = new JellyfinProfileRow {
            Id = Guid.NewGuid(),
            Username = username,
            NormalizedUsername = normalized,
            DisplayName = displayName,
            AllowNsfw = allowNsfw,
            Enabled = enabled,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.JellyfinProfiles.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
        return ToProfile(row);
    }

    /// <inheritdoc />
    public async Task<JellyfinProfile?> UpdateProfileAsync(
        Guid profileId,
        string? username,
        string? displayName,
        bool? allowNsfw,
        bool? enabled,
        CancellationToken cancellationToken) {
        var row = await _db.JellyfinProfiles.FirstOrDefaultAsync(profile => profile.Id == profileId, cancellationToken);
        if (row is null) {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(username)) {
            var normalized = NormalizeUsername(username);
            var duplicate = await _db.JellyfinProfiles.AnyAsync(
                profile => profile.Id != profileId && profile.NormalizedUsername == normalized,
                cancellationToken);
            if (duplicate) {
                throw new InvalidOperationException("A Jellyfin profile with that username already exists.");
            }

            row.Username = username;
            row.NormalizedUsername = normalized;
        }

        if (displayName is not null) {
            row.DisplayName = string.IsNullOrWhiteSpace(displayName) ? row.Username : displayName;
        }

        if (allowNsfw is { } allow) {
            row.AllowNsfw = allow;
        }

        if (enabled is { } isEnabled) {
            row.Enabled = isEnabled;
            if (!isEnabled) {
                var nowDisabled = DateTimeOffset.UtcNow;
                await _db.JellyfinSessions
                    .Where(session => session.ProfileId == profileId && session.InvalidatedAt == null)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.InvalidatedAt, nowDisabled), cancellationToken);
            }
        }

        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return ToProfile(row);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteProfileAsync(Guid profileId, CancellationToken cancellationToken) {
        var row = await _db.JellyfinProfiles.FirstOrDefaultAsync(profile => profile.Id == profileId, cancellationToken);
        if (row is null) {
            return false;
        }

        _db.JellyfinProfiles.Remove(row);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<JellyfinSession> CreateSessionAsync(
        Guid profileId,
        string tokenHash,
        JellyfinClientIdentity client,
        CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var row = new JellyfinSessionRow {
            Id = Guid.NewGuid(),
            ProfileId = profileId,
            TokenHash = tokenHash,
            Client = client.Client,
            DeviceName = client.DeviceName,
            DeviceId = client.DeviceId,
            ApplicationVersion = client.ApplicationVersion,
            CreatedAt = now,
            LastSeenAt = now
        };
        _db.JellyfinSessions.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
        return ToSession(row);
    }

    /// <inheritdoc />
    public async Task<JellyfinSessionResolution?> ResolveSessionAsync(string tokenHash, CancellationToken cancellationToken) {
        var result = await (
            from session in _db.JellyfinSessions
            join profile in _db.JellyfinProfiles on session.ProfileId equals profile.Id
            where session.TokenHash == tokenHash && session.InvalidatedAt == null && profile.Enabled
            select new { Session = session, Profile = profile })
            .FirstOrDefaultAsync(cancellationToken);
        if (result is null) {
            return null;
        }

        result.Session.LastSeenAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return new JellyfinSessionResolution(ToSession(result.Session), ToProfile(result.Profile));
    }

    /// <inheritdoc />
    public async Task TouchProfileLoginAsync(Guid profileId, CancellationToken cancellationToken) {
        var profile = await _db.JellyfinProfiles.FirstOrDefaultAsync(row => row.Id == profileId, cancellationToken);
        if (profile is null) {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        profile.LastLoginAt = now;
        profile.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string NormalizeUsername(string username) => username.Trim().ToLowerInvariant();

    private static AppSecurityState ToState(AppSecurityRow row) =>
        new(
            row.Id,
            row.ServerId,
            row.ApiKey,
            row.DefaultProfileSeeded,
            row.ApiKeyCreatedAt,
            row.ApiKeyUpdatedAt,
            row.CreatedAt,
            row.UpdatedAt);

    private static JellyfinProfile ToProfile(JellyfinProfileRow row) =>
        new(
            row.Id,
            row.Username,
            row.DisplayName,
            row.AllowNsfw,
            row.Enabled,
            row.LastLoginAt,
            row.CreatedAt,
            row.UpdatedAt);

    private static JellyfinSession ToSession(JellyfinSessionRow row) =>
        new(
            row.Id,
            row.ProfileId,
            row.TokenHash,
            row.Client,
            row.DeviceName,
            row.DeviceId,
            row.ApplicationVersion,
            row.CreatedAt,
            row.LastSeenAt,
            row.InvalidatedAt);
}
