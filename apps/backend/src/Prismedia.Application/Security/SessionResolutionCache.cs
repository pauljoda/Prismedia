using System.Collections.Concurrent;

namespace Prismedia.Application.Security;

/// <summary>
/// Short-lived in-process cache for session-token resolution. Every authenticated request
/// resolves its bearer token; without this cache each resolution is a database join. Entries
/// live for <see cref="TimeToLive"/> and are additionally invalidated by every session/user
/// mutation path (logout, revoke, password change, profile/admin edits, user delete), so a
/// revoked session stops resolving immediately in this process. The 90-day sliding session
/// window and the 5-minute last-seen touch cadence both dwarf the TTL, so neither is affected.
/// </summary>
public sealed class SessionResolutionCache {
    /// <summary>How long a cached resolution may be served before re-reading the database.</summary>
    public static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(60);

    private const int PruneThreshold = 512;

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    public SessionResolutionCache(TimeProvider? time = null) {
        _time = time ?? TimeProvider.System;
    }

    private sealed record Entry(UserSessionResolution Resolution, DateTimeOffset ExpiresAt);

    /// <summary>Returns the cached resolution for a token hash, or null when absent/expired.</summary>
    public UserSessionResolution? TryGet(string tokenHash) {
        if (!_entries.TryGetValue(tokenHash, out var entry)) {
            return null;
        }

        if (entry.ExpiresAt <= _time.GetUtcNow()) {
            _entries.TryRemove(tokenHash, out _);
            return null;
        }

        return entry.Resolution;
    }

    /// <summary>Caches a successful resolution for its token hash.</summary>
    public void Set(string tokenHash, UserSessionResolution resolution) {
        if (_entries.Count >= PruneThreshold) {
            PruneExpired();
        }

        _entries[tokenHash] = new Entry(resolution, _time.GetUtcNow() + TimeToLive);
    }

    /// <summary>Drops any cached resolution for one session (logout, admin revoke).</summary>
    public void InvalidateSession(Guid sessionId) {
        foreach (var (key, entry) in _entries) {
            if (entry.Resolution.Session.Id == sessionId) {
                _entries.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// Drops every cached resolution for one user (password change, profile edit, admin
    /// update/delete), so role/enabled/NSFW changes take effect on the next request.
    /// </summary>
    public void InvalidateUser(Guid userId) {
        foreach (var (key, entry) in _entries) {
            if (entry.Resolution.User.Id == userId) {
                _entries.TryRemove(key, out _);
            }
        }
    }

    private void PruneExpired() {
        var now = _time.GetUtcNow();
        foreach (var (key, entry) in _entries) {
            if (entry.ExpiresAt <= now) {
                _entries.TryRemove(key, out _);
            }
        }
    }
}
