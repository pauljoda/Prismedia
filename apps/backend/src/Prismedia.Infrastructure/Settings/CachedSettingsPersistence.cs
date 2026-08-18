using Prismedia.Application.Settings;
using Prismedia.Contracts.Settings;

namespace Prismedia.Infrastructure.Settings;

/// <summary>
/// Process-wide cached state for <see cref="CachedSettingsPersistence"/>. Holds the last
/// loaded setting-override snapshot and library-root list. Snapshots are invalidated
/// immediately by same-process writes; the short time-to-live bounds staleness from writes
/// made by the other process (API vs worker) sharing the database.
/// </summary>
public sealed class SettingsSnapshotCache {
    /// <summary>Upper bound on cross-process staleness for cached reads.</summary>
    public static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(15);

    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private IReadOnlyDictionary<string, string>? _overrides;
    private DateTimeOffset _overridesExpireAt;
    private IReadOnlyList<LibraryRoot>? _roots;
    private DateTimeOffset _rootsExpireAt;

    public SettingsSnapshotCache(TimeProvider? time = null) {
        _time = time ?? TimeProvider.System;
    }

    internal IReadOnlyDictionary<string, string>? TryGetOverrides() {
        lock (_gate) {
            return _overridesExpireAt > _time.GetUtcNow() ? _overrides : null;
        }
    }

    internal void SetOverrides(IReadOnlyDictionary<string, string> overrides) {
        lock (_gate) {
            _overrides = overrides;
            _overridesExpireAt = _time.GetUtcNow() + TimeToLive;
        }
    }

    internal IReadOnlyList<LibraryRoot>? TryGetRoots() {
        lock (_gate) {
            return _rootsExpireAt > _time.GetUtcNow() ? _roots : null;
        }
    }

    internal void SetRoots(IReadOnlyList<LibraryRoot> roots) {
        lock (_gate) {
            _roots = roots;
            _rootsExpireAt = _time.GetUtcNow() + TimeToLive;
        }
    }

    /// <summary>Drops the cached override snapshot after any override write.</summary>
    public void InvalidateOverrides() {
        lock (_gate) {
            _overrides = null;
            _overridesExpireAt = DateTimeOffset.MinValue;
        }
    }

    /// <summary>Drops the cached root list after any library-root write.</summary>
    public void InvalidateRoots() {
        lock (_gate) {
            _roots = null;
            _rootsExpireAt = DateTimeOffset.MinValue;
        }
    }
}

/// <summary>
/// Caching decorator over <see cref="EfSettingsPersistence"/>. The override snapshot and the
/// library-root list are read on nearly every page load (visibility mode, navigation layout)
/// and by every scan, and each uncached read materializes a whole table. Every mutation on
/// this interface invalidates the corresponding snapshot, so same-process reads never observe
/// their own writes stale. Code that constructs <see cref="EfSettingsPersistence"/> directly
/// bypasses the cache entirely and stays uncached but always-fresh.
/// </summary>
public sealed class CachedSettingsPersistence : ISettingsPersistence {
    private readonly EfSettingsPersistence _inner;
    private readonly SettingsSnapshotCache _cache;
    private readonly Entities.VisibilityScopeCache? _visibilityScopes;

    public CachedSettingsPersistence(
        EfSettingsPersistence inner,
        SettingsSnapshotCache cache,
        Entities.VisibilityScopeCache? visibilityScopes = null) {
        _inner = inner;
        _cache = cache;
        _visibilityScopes = visibilityScopes;
    }

    private void InvalidateRootDependents() {
        _cache.InvalidateRoots();
        // Hidden-root sets derive from root enabled flags; a root write must drop cached scopes.
        _visibilityScopes?.Clear();
    }

    public async Task<IReadOnlyDictionary<string, string>> LoadSettingOverridesAsync(CancellationToken cancellationToken) {
        if (_cache.TryGetOverrides() is { } cached) {
            return cached;
        }

        var loaded = await _inner.LoadSettingOverridesAsync(cancellationToken);
        _cache.SetOverrides(loaded);
        return loaded;
    }

    public async Task SaveSettingOverrideAsync(string key, string valueJson, CancellationToken cancellationToken) {
        await _inner.SaveSettingOverrideAsync(key, valueJson, cancellationToken);
        _cache.InvalidateOverrides();
    }

    public async Task SaveSettingOverridesAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken) {
        await _inner.SaveSettingOverridesAsync(values, cancellationToken);
        _cache.InvalidateOverrides();
    }

    public async Task ReplaceSettingOverridesAsync(
        IReadOnlyDictionary<string, string> upserts,
        IReadOnlyCollection<string> deletes,
        CancellationToken cancellationToken) {
        await _inner.ReplaceSettingOverridesAsync(upserts, deletes, cancellationToken);
        _cache.InvalidateOverrides();
    }

    public async Task DeleteSettingOverrideAsync(string key, CancellationToken cancellationToken) {
        await _inner.DeleteSettingOverrideAsync(key, cancellationToken);
        _cache.InvalidateOverrides();
    }

    public async Task<IReadOnlyList<LibraryRoot>> ListLibraryRootsAsync(CancellationToken cancellationToken) {
        if (_cache.TryGetRoots() is { } cached) {
            return cached;
        }

        var loaded = await _inner.ListLibraryRootsAsync(cancellationToken);
        _cache.SetRoots(loaded);
        return loaded;
    }

    public Task<LibraryRoot?> GetLibraryRootAsync(Guid id, CancellationToken cancellationToken) =>
        _inner.GetLibraryRootAsync(id, cancellationToken);

    public async Task<LibraryRoot> AddLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken) {
        var added = await _inner.AddLibraryRootAsync(state, cancellationToken);
        InvalidateRootDependents();
        return added;
    }

    public async Task<LibraryRoot> SaveLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken) {
        var saved = await _inner.SaveLibraryRootAsync(state, cancellationToken);
        InvalidateRootDependents();
        return saved;
    }

    public async Task<bool> DeleteLibraryRootAsync(Guid id, CancellationToken cancellationToken) {
        var deleted = await _inner.DeleteLibraryRootAsync(id, cancellationToken);
        if (deleted) {
            InvalidateRootDependents();
        }

        return deleted;
    }
}
