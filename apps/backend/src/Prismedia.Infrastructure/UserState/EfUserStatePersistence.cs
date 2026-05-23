using Microsoft.EntityFrameworkCore;
using Prismedia.Application.UserState;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.UserState;

/// <summary>
/// EF Core adapter for <see cref="IUserStatePersistence"/>. Reads and writes raw JSON values
/// in the <c>ui_preferences</c> table keyed by a user-state slot name.
/// </summary>
public sealed class EfUserStatePersistence : IUserStatePersistence {
    private readonly PrismediaDbContext _db;

    public EfUserStatePersistence(PrismediaDbContext db) {
        _db = db;
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken) {
        var row = await _db.UiPreferences.AsNoTracking()
            .FirstOrDefaultAsync(pref => pref.Key == key, cancellationToken);
        return string.IsNullOrWhiteSpace(row?.ValueJson) ? null : row.ValueJson;
    }

    public async Task SaveAsync(string key, string valueJson, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var row = await _db.UiPreferences.FindAsync([key], cancellationToken);
        if (row is null) {
            _db.UiPreferences.Add(new UiPreferenceRow {
                Key = key,
                ValueJson = valueJson,
                UpdatedAt = now,
            });
        } else {
            row.ValueJson = valueJson;
            row.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken) {
        var row = await _db.UiPreferences.FindAsync([key], cancellationToken);
        if (row is null) {
            return;
        }

        _db.UiPreferences.Remove(row);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
