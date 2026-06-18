using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Playback;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Playback;

/// <summary>
/// EF Core adapter for browser-scoped transient playback and UI setting persistence.
/// </summary>
public sealed class EfBrowserSessionPersistence(PrismediaDbContext db) : IBrowserSessionPersistence {
    /// <inheritdoc />
    public async Task<BrowserSessionState> EnsureAsync(
        Guid? requestedSessionId,
        DateTimeOffset now,
        DateTimeOffset staleBefore,
        CancellationToken cancellationToken) {
        await PruneStaleAsync(staleBefore, cancellationToken);

        BrowserSessionRow? row = null;
        if (requestedSessionId is { } id && id != Guid.Empty) {
            row = await db.BrowserSessions.FindAsync([id], cancellationToken);
            if (row is not null && row.LastSeenAt < staleBefore) {
                db.BrowserSessions.Remove(row);
                row = null;
            }
        }

        if (row is null) {
            row = new BrowserSessionRow {
                Id = Guid.NewGuid(),
                CreatedAt = now,
                LastSeenAt = now,
                UpdatedAt = now,
            };
            db.BrowserSessions.Add(row);
        } else {
            row.LastSeenAt = now;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new BrowserSessionState(row.Id, row.CreatedAt, row.LastSeenAt);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> LoadSettingsAsync(
        Guid sessionId,
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken) {
        if (keys.Count == 0) {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return await db.BrowserSessionSettings
            .AsNoTracking()
            .Where(row => row.BrowserSessionId == sessionId && keys.Contains(row.Key))
            .ToDictionaryAsync(row => row.Key, row => row.ValueJson, StringComparer.Ordinal, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReplaceSettingsAsync(
        Guid sessionId,
        IReadOnlyDictionary<string, string> upserts,
        IReadOnlyCollection<string> deletes,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        var deleteKeys = deletes
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.Ordinal);
        var keys = upserts.Keys.Concat(deleteKeys).Distinct(StringComparer.Ordinal).ToArray();
        if (keys.Length == 0) {
            return;
        }

        var existing = await db.BrowserSessionSettings
            .Where(row => row.BrowserSessionId == sessionId && keys.Contains(row.Key))
            .ToDictionaryAsync(row => row.Key, StringComparer.Ordinal, cancellationToken);

        foreach (var key in deleteKeys.Where(key => !upserts.ContainsKey(key))) {
            if (existing.TryGetValue(key, out var row)) {
                db.BrowserSessionSettings.Remove(row);
            }
        }

        foreach (var (key, valueJson) in upserts) {
            if (existing.TryGetValue(key, out var row)) {
                row.ValueJson = valueJson;
                row.UpdatedAt = now;
            } else {
                db.BrowserSessionSettings.Add(new BrowserSessionSettingRow {
                    BrowserSessionId = sessionId,
                    Key = key,
                    ValueJson = valueJson,
                    UpdatedAt = now,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task PruneStaleAsync(DateTimeOffset staleBefore, CancellationToken cancellationToken) {
        var staleRows = await db.BrowserSessions
            .Where(row => row.LastSeenAt < staleBefore)
            .ToArrayAsync(cancellationToken);
        if (staleRows.Length == 0) {
            return;
        }

        db.BrowserSessions.RemoveRange(staleRows);
    }
}
