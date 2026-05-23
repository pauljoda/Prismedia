using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Settings;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Settings;

/// <summary>
/// EF Core adapter for <see cref="ISettingsPersistence"/>. Stores app-setting overrides
/// as raw JSON in <c>app_settings</c> and owns row ↔ contract translation for watched
/// library roots.
/// </summary>
public sealed class EfSettingsPersistence : ISettingsPersistence {
    private readonly PrismediaDbContext _db;

    public EfSettingsPersistence(PrismediaDbContext db) {
        _db = db;
    }

    public async Task<IReadOnlyDictionary<string, string>> LoadSettingOverridesAsync(CancellationToken cancellationToken) =>
        await _db.AppSettings
            .AsNoTracking()
            .ToDictionaryAsync(row => row.Key, row => row.ValueJson, StringComparer.Ordinal, cancellationToken);

    public async Task SaveSettingOverrideAsync(string key, string valueJson, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var row = await _db.AppSettings.FindAsync([key], cancellationToken);
        if (row is null) {
            _db.AppSettings.Add(new AppSettingRow {
                Key = key,
                ValueJson = valueJson,
                CreatedAt = now,
                UpdatedAt = now,
            });
        } else {
            row.ValueJson = valueJson;
            row.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveSettingOverridesAsync(
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken) {
        if (values.Count == 0) {
            return;
        }

        var keys = values.Keys.ToArray();
        var existing = await _db.AppSettings
            .Where(row => keys.Contains(row.Key))
            .ToDictionaryAsync(row => row.Key, StringComparer.Ordinal, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        foreach (var (key, valueJson) in values) {
            if (existing.TryGetValue(key, out var row)) {
                row.ValueJson = valueJson;
                row.UpdatedAt = now;
            } else {
                _db.AppSettings.Add(new AppSettingRow {
                    Key = key,
                    ValueJson = valueJson,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteSettingOverrideAsync(string key, CancellationToken cancellationToken) {
        var row = await _db.AppSettings.FindAsync([key], cancellationToken);
        if (row is null) {
            return;
        }

        _db.AppSettings.Remove(row);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LibraryRoot>> ListLibraryRootsAsync(CancellationToken cancellationToken) {
        return await _db.LibraryRoots
            .AsNoTracking()
            .OrderBy(root => root.Label)
            .ThenBy(root => root.Path)
            .Select(root => ToContract(root))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<LibraryRoot?> GetLibraryRootAsync(Guid id, CancellationToken cancellationToken) {
        var row = await _db.LibraryRoots.AsNoTracking().FirstOrDefaultAsync(root => root.Id == id, cancellationToken);
        return row is null ? null : ToContract(row);
    }

    public async Task<LibraryRoot> AddLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken) {
        var row = new LibraryRootRow {
            Id = state.Id,
            Path = state.Path,
            Label = state.Label,
            Enabled = state.Enabled,
            Recursive = state.Recursive,
            ScanVideos = state.ScanVideos,
            ScanImages = state.ScanImages,
            ScanAudio = state.ScanAudio,
            ScanBooks = state.ScanBooks,
            IsNsfw = state.IsNsfw,
            LastScannedAt = state.LastScannedAt,
            CreatedAt = state.CreatedAt,
            UpdatedAt = state.UpdatedAt,
        };

        _db.LibraryRoots.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
        return ToContract(row);
    }

    public async Task<LibraryRoot> SaveLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken) {
        var row = await _db.LibraryRoots.FindAsync([state.Id], cancellationToken)
            ?? throw new InvalidOperationException($"Library root '{state.Id}' was not found.");

        row.Path = state.Path;
        row.Label = state.Label;
        row.Enabled = state.Enabled;
        row.Recursive = state.Recursive;
        row.ScanVideos = state.ScanVideos;
        row.ScanImages = state.ScanImages;
        row.ScanAudio = state.ScanAudio;
        row.ScanBooks = state.ScanBooks;
        row.IsNsfw = state.IsNsfw;
        row.LastScannedAt = state.LastScannedAt;
        row.UpdatedAt = state.UpdatedAt;

        await _db.SaveChangesAsync(cancellationToken);
        return ToContract(row);
    }

    public async Task<bool> DeleteLibraryRootAsync(Guid id, CancellationToken cancellationToken) {
        var row = await _db.LibraryRoots.FindAsync([id], cancellationToken);
        if (row is null) {
            return false;
        }

        _db.LibraryRoots.Remove(row);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static LibraryRoot ToContract(LibraryRootRow row) =>
        new(
            row.Id,
            row.Path,
            row.Label,
            row.Enabled,
            row.Recursive,
            row.ScanVideos,
            row.ScanImages,
            row.ScanAudio,
            row.ScanBooks,
            row.IsNsfw,
            row.LastScannedAt,
            row.CreatedAt,
            row.UpdatedAt);
}
