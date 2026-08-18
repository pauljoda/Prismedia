using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Prismedia.Application.Security;

/// <summary>
/// Short-lived cache of successfully verified Basic-auth credentials for the OPDS surface.
/// OPDS clients re-send credentials on every request — a catalog page triggers one request per
/// cover — and each verification is a deliberately expensive PBKDF2 pass. Entries key on the
/// username plus a SHA-256 digest of the presented password (the password itself is never
/// stored), live for <see cref="TimeToLive"/>, and are invalidated on password or user changes
/// so a rotated credential stops authenticating in this process immediately. Only successful
/// verifications are cached; failures always take the full-cost path and count against the
/// auth throttle.
/// </summary>
public sealed class BasicCredentialCache {
    /// <summary>How long a verified credential may be reused before full re-verification.</summary>
    public static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(5);

    private const int PruneThreshold = 128;

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    public BasicCredentialCache(TimeProvider? time = null) {
        _time = time ?? TimeProvider.System;
    }

    private sealed record Entry(User User, DateTimeOffset ExpiresAt);

    /// <summary>Returns the verified user for these credentials, or null when absent/expired.</summary>
    public User? TryGet(string? username, string? password) {
        if (KeyFor(username, password) is not { } key ||
            !_entries.TryGetValue(key, out var entry)) {
            return null;
        }

        if (entry.ExpiresAt <= _time.GetUtcNow()) {
            _entries.TryRemove(key, out _);
            return null;
        }

        return entry.User;
    }

    /// <summary>Caches a successful credential verification.</summary>
    public void Set(string? username, string? password, User user) {
        if (KeyFor(username, password) is not { } key) {
            return;
        }

        if (_entries.Count >= PruneThreshold) {
            PruneExpired();
        }

        _entries[key] = new Entry(user, _time.GetUtcNow() + TimeToLive);
    }

    /// <summary>Drops every cached credential for one user (password change, admin edit/delete).</summary>
    public void InvalidateUser(Guid userId) {
        foreach (var (key, entry) in _entries) {
            if (entry.User.Id == userId) {
                _entries.TryRemove(key, out _);
            }
        }
    }

    private static string? KeyFor(string? username, string? password) {
        if (string.IsNullOrWhiteSpace(username) || password is null) {
            return null;
        }

        var digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(password)));
        return $"{username.Trim().ToLowerInvariant()}:{digest}";
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
