using System.Collections.Concurrent;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// Process-wide cache of resolved library-visibility scopes. Resolving a scope costs up to four
/// queries (hidden roots, request-target visibility, default profiles, hidden wanted entities)
/// and previously ran once per HTTP request for every user with any disabled or denied root —
/// a dashboard load pays it a dozen times. Scopes key on the caller's allowed-root set, so a
/// changed access grant naturally resolves under a new key; root enable/disable flips clear the
/// cache via the settings write path. The wanted-placeholder id set inside a scope was already
/// a point-in-time snapshot per request; the short time-to-live extends that window only
/// marginally, and physical root ownership stays query-backed at read time regardless.
/// </summary>
public sealed class VisibilityScopeCache {
    /// <summary>Upper bound on staleness for cached scopes.</summary>
    public static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    public VisibilityScopeCache(TimeProvider? time = null) {
        _time = time ?? TimeProvider.System;
    }

    private sealed record Entry(Guid[] HiddenRootIds, Guid[] HiddenWantedEntityIds, DateTimeOffset ExpiresAt);

    /// <summary>Builds the cache key for a caller's allowed-root set (null = unrestricted admin).</summary>
    public static string KeyFor(IReadOnlySet<Guid>? allowedRootIds) =>
        allowedRootIds is null
            ? "admin"
            : string.Join(',', allowedRootIds.Order());

    /// <summary>Returns the cached scope arrays for a key, or null when absent/expired.</summary>
    public (Guid[] HiddenRootIds, Guid[] HiddenWantedEntityIds)? TryGet(string key) {
        if (!_entries.TryGetValue(key, out var entry)) {
            return null;
        }

        if (entry.ExpiresAt <= _time.GetUtcNow()) {
            _entries.TryRemove(key, out _);
            return null;
        }

        return (entry.HiddenRootIds, entry.HiddenWantedEntityIds);
    }

    /// <summary>Caches a resolved scope for its allowed-root key.</summary>
    public void Set(string key, Guid[] hiddenRootIds, Guid[] hiddenWantedEntityIds) =>
        _entries[key] = new Entry(hiddenRootIds, hiddenWantedEntityIds, _time.GetUtcNow() + TimeToLive);

    /// <summary>Drops every cached scope (library-root create/update/delete).</summary>
    public void Clear() => _entries.Clear();
}
