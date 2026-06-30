using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Plugins;

public sealed partial class EntityMetadataApplyService {
    private async Task UpsertDescriptionAsync(Guid entityId, string? value, DateTimeOffset now, CancellationToken cancellationToken) {
        var existing = await _db.EntityDescriptions.FindAsync([entityId], cancellationToken);
        if (string.IsNullOrWhiteSpace(value)) {
            // An explicit empty value clears the description — this is intentional for the manual edit path,
            // which shares this method. (A consequence: an identify pass that returns no description clears a
            // request-time seed; that is acceptable and rare, and must not be "fixed" here without breaking
            // the edit-clear behavior — the seed is a best-effort floor, not a guarantee.)
            if (existing is not null) {
                _db.EntityDescriptions.Remove(existing);
            }
            return;
        }

        if (existing is null) {
            _db.EntityDescriptions.Add(new EntityDescriptionRow { EntityId = entityId, Value = value.Trim(), UpdatedAt = now });
        } else {
            existing.Value = value.Trim();
            existing.UpdatedAt = now;
        }
    }

    private async Task ReplaceUrlsAsync(Guid entityId, IReadOnlyList<string> urls, DateTimeOffset now, CancellationToken cancellationToken) {
        var existing = await _db.EntityUrls
            .Where(row => row.EntityId == entityId)
            .ToArrayAsync(cancellationToken);
        _db.EntityUrls.RemoveRange(existing);

        var order = 0;
        foreach (var url in urls.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)) {
            _db.EntityUrls.Add(new EntityUrlRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                Url = url,
                SortOrder = order++,
                CreatedAt = now
            });
        }
    }

    private async Task ReplaceExternalIdsAsync(
        Guid entityId,
        IReadOnlyDictionary<string, string> externalIds,
        IReadOnlyList<string> urls,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        var existing = await _db.EntityExternalIds
            .Where(row => row.EntityId == entityId)
            .ToArrayAsync(cancellationToken);
        _db.EntityExternalIds.RemoveRange(existing);

        foreach (var (provider, rawValue) in externalIds) {
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(rawValue)) {
                continue;
            }

            var value = rawValue.Trim();
            var url = urls.FirstOrDefault(candidate => candidate.Contains(value, StringComparison.OrdinalIgnoreCase));
            _db.EntityExternalIds.Add(new EntityExternalIdRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                Provider = provider.Trim(),
                Value = value,
                Url = url,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
    }

    private async Task UpsertExternalIdsAsync(
        Guid entityId,
        IReadOnlyDictionary<string, string> externalIds,
        IReadOnlyList<string> urls,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        foreach (var (provider, rawValue) in externalIds) {
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(rawValue)) {
                continue;
            }

            var providerKey = provider.Trim();
            var value = rawValue.Trim();
            var existing = _db.EntityExternalIds.Local.FirstOrDefault(row =>
                row.EntityId == entityId &&
                row.Provider == providerKey &&
                _db.Entry(row).State != EntityState.Deleted) ??
                await _db.EntityExternalIds
                    .FirstOrDefaultAsync(row => row.EntityId == entityId && row.Provider == providerKey, cancellationToken);
            var url = urls.FirstOrDefault(candidate => candidate.Contains(value, StringComparison.OrdinalIgnoreCase));
            if (existing is null) {
                _db.EntityExternalIds.Add(new EntityExternalIdRow {
                    Id = Guid.NewGuid(),
                    EntityId = entityId,
                    Provider = providerKey,
                    Value = value,
                    Url = url,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            } else {
                existing.Value = value;
                existing.Url = url ?? existing.Url;
                existing.UpdatedAt = now;
            }
        }
    }

    private async Task UpsertUrlsAsync(Guid entityId, IReadOnlyList<string> urls, DateTimeOffset now, CancellationToken cancellationToken) {
        var existing = await _db.EntityUrls
            .Where(row => row.EntityId == entityId)
            .Select(row => row.Url)
            .ToArrayAsync(cancellationToken);
        var tracked = _db.EntityUrls.Local
            .Where(row => row.EntityId == entityId && _db.Entry(row).State != EntityState.Deleted)
            .Select(row => row.Url);
        var seen = existing.Concat(tracked).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sortOrder = existing.Length;

        foreach (var url in urls.Where(url => !string.IsNullOrWhiteSpace(url)).Select(url => url.Trim())) {
            if (!seen.Add(url)) {
                continue;
            }

            _db.EntityUrls.Add(new EntityUrlRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                Url = url,
                SortOrder = sortOrder++,
                CreatedAt = now
            });
        }
    }

    private async Task UpsertDatesAsync(Guid entityId, IReadOnlyDictionary<string, string> dates, DateTimeOffset now, CancellationToken cancellationToken) {
        foreach (var (code, value) in dates.Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))) {
            var parsed = EntityDateParser.Parse(value);
            var existing = ReviveIfDeleted(await _db.EntityDates.FindAsync([entityId, code], cancellationToken));
            if (existing is null) {
                _db.EntityDates.Add(new EntityDateRow {
                    EntityId = entityId,
                    Code = code,
                    Value = parsed?.NormalizedValue ?? value.Trim(),
                    SortableValue = parsed?.SortableValue,
                    Precision = parsed?.Precision.ToCode(),
                    UpdatedAt = now
                });
            } else {
                existing.Value = parsed?.NormalizedValue ?? value.Trim();
                existing.SortableValue = parsed?.SortableValue;
                existing.Precision = parsed?.Precision.ToCode();
                existing.UpdatedAt = now;
            }
        }
    }

    private async Task ReplaceDatesAsync(Guid entityId, IReadOnlyDictionary<string, string> dates, DateTimeOffset now, CancellationToken cancellationToken) {
        var incoming = dates
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);
        var existing = await _db.EntityDates
            .Where(row => row.EntityId == entityId)
            .ToArrayAsync(cancellationToken);
        // Re-sent codes are updated in place rather than removed and re-added: a removed row
        // stays Deleted in the change tracker, so the upsert's FindAsync would hand back a
        // doomed instance and the "update" would silently become a delete.
        _db.EntityDates.RemoveRange(existing.Where(row => !incoming.Contains(row.Code)));
        await UpsertDatesAsync(entityId, dates, now, cancellationToken);
    }

    private async Task UpsertStatsAsync(Guid entityId, IReadOnlyDictionary<string, int> stats, DateTimeOffset now, CancellationToken cancellationToken) {
        foreach (var (code, value) in FilterStats(stats)) {
            var existing = ReviveIfDeleted(await _db.EntityStats.FindAsync([entityId, code], cancellationToken));
            if (existing is null) {
                _db.EntityStats.Add(new EntityStatRow { EntityId = entityId, Code = code, Value = value, UpdatedAt = now });
            } else {
                existing.Value = value;
                existing.UpdatedAt = now;
            }
        }
    }

    private async Task ReplaceStatsAsync(Guid entityId, IReadOnlyDictionary<string, int> stats, DateTimeOffset now, CancellationToken cancellationToken) {
        var incoming = FilterStats(stats).Keys.ToHashSet(StringComparer.Ordinal);
        var existing = await _db.EntityStats
            .Where(row => row.EntityId == entityId)
            .ToArrayAsync(cancellationToken);
        // See ReplaceDatesAsync: re-sent codes must update in place, not delete-and-re-add.
        _db.EntityStats.RemoveRange(existing.Where(row => !incoming.Contains(row.Code)));
        await UpsertStatsAsync(entityId, stats, now, cancellationToken);
    }

    private static IReadOnlyDictionary<string, int> FilterStats(IReadOnlyDictionary<string, int> stats) =>
        stats
            .Where(item => !IgnoredStatCodes.Contains(item.Key.Trim()))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);

    private async Task UpsertPositionsAsync(EntityRow entity, IReadOnlyDictionary<string, int> positions, DateTimeOffset now, CancellationToken cancellationToken) {
        foreach (var (code, value) in positions) {
            var existing = ReviveIfDeleted(await _db.EntityPositions.FindAsync([entity.Id, code], cancellationToken));
            if (existing is null) {
                _db.EntityPositions.Add(new EntityPositionRow { EntityId = entity.Id, Code = code, Value = value, UpdatedAt = now });
            } else {
                existing.Value = value;
                existing.UpdatedAt = now;
            }
        }

        await ApplyStructuralSortOrderAsync(entity, positions, now, cancellationToken);
    }

    private async Task ReplacePositionsAsync(EntityRow entity, IReadOnlyDictionary<string, int> positions, DateTimeOffset now, CancellationToken cancellationToken) {
        var incoming = positions.Keys.ToHashSet(StringComparer.Ordinal);
        var existing = await _db.EntityPositions
            .Where(row => row.EntityId == entity.Id)
            .ToArrayAsync(cancellationToken);
        // See ReplaceDatesAsync: re-sent codes must update in place, not delete-and-re-add.
        _db.EntityPositions.RemoveRange(existing.Where(row => !incoming.Contains(row.Code)));
        await UpsertPositionsAsync(entity, positions, now, cancellationToken);
    }

    private async Task ApplyStructuralSortOrderAsync(
        EntityRow entity,
        IReadOnlyDictionary<string, int> positions,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        var sortOrder = EntityMetadataPositionRules.SortOrderFor(entity.KindCode, positions);
        if (sortOrder is null) {
            return;
        }

        entity.SortOrder = sortOrder.Value;
        entity.UpdatedAt = now;
    }

    private async Task UpsertClassificationAsync(Guid entityId, string? value, DateTimeOffset now, CancellationToken cancellationToken) {
        var existing = await _db.EntityClassifications.FindAsync([entityId], cancellationToken);
        if (existing is null) {
            _db.EntityClassifications.Add(new EntityClassificationRow { EntityId = entityId, Value = value, System = "plugin", UpdatedAt = now });
        } else {
            existing.Value = value;
            existing.System = "plugin";
            existing.UpdatedAt = now;
        }
    }

    private async Task ReplaceClassificationAsync(Guid entityId, string? value, DateTimeOffset now, CancellationToken cancellationToken) {
        var existing = await _db.EntityClassifications.FindAsync([entityId], cancellationToken);
        if (string.IsNullOrWhiteSpace(value)) {
            if (existing is not null) {
                _db.EntityClassifications.Remove(existing);
            }
            return;
        }

        if (existing is null) {
            _db.EntityClassifications.Add(new EntityClassificationRow { EntityId = entityId, Value = value.Trim(), System = "manual", UpdatedAt = now });
        } else {
            existing.Value = value.Trim();
            existing.System = "manual";
            existing.UpdatedAt = now;
        }
    }

    private async Task UpsertFlagsAsync(Guid entityId, EntityMetadataFlagsPatch? patch, DateTimeOffset now, CancellationToken cancellationToken) {
        if (patch is null) return;
        var row = await _db.Entities.FindAsync([entityId], cancellationToken);
        if (row is null) return;
        if (patch.IsFavorite.HasValue) row.IsFavorite = patch.IsFavorite.Value;
        if (patch.IsNsfw.HasValue) row.IsNsfw = patch.IsNsfw.Value;
        if (patch.IsOrganized.HasValue) row.IsOrganized = patch.IsOrganized.Value;
        row.UpdatedAt = now;
    }

    /// <summary>
    /// Returns the tracked row in an updatable state. A row removed earlier in the same unit of
    /// work is still in the change tracker as Deleted; mutating it would silently keep the delete,
    /// so it is revived to Modified before the caller writes to it.
    /// </summary>
    private TRow? ReviveIfDeleted<TRow>(TRow? row) where TRow : class {
        if (row is not null && _db.Entry(row).State == EntityState.Deleted) {
            _db.Entry(row).State = EntityState.Modified;
        }

        return row;
    }
}
