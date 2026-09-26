using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Media.Persistence;

public sealed partial class LibraryScanPersistenceService {
    /// <summary>
    /// Checks current protection inside the lifecycle lease, after scanner reads. Scanner-owned facts
    /// can still change, but protected descriptions and titles retain their latest committed values.
    /// Existing Entity concurrency tokens remain intact: unrelated concurrent edits require a fresh scan.
    /// </summary>
    private async Task SaveProtectedScanChangesAsync(CancellationToken token) {
        var titles = _db.ChangeTracker.Entries<EntityRow>()
            .Where(entry => entry.State == EntityState.Added ||
                entry.State == EntityState.Modified && entry.Property(row => row.Title).IsModified).ToArray();
        var descriptions = _db.ChangeTracker.Entries<EntityDescriptionRow>()
            .Where(entry => entry.State == EntityState.Added ||
                entry.State == EntityState.Modified && entry.Property(row => row.Value).IsModified).ToArray();
        var ids = titles.Select(entry => entry.Entity.Id).Concat(descriptions.Select(entry => entry.Entity.EntityId)).Distinct().ToArray();
        if (ids.Length == 0) { await _db.SaveChangesAsync(token); return; }

        // Do not consult cached tracked evidence: the user may have locked or cleared a field since it was read.
        var evidence = await _db.EntityMetadataFields.AsNoTracking().Where(row => ids.Contains(row.EntityId))
            .ToDictionaryAsync(row => (row.EntityId, row.Field), token);
        var titleIds = titles.Where(entry => entry.State != EntityState.Added).Select(entry => entry.Entity.Id).ToArray();
        var savedTitles = await _db.Entities.AsNoTracking().Where(row => titleIds.Contains(row.Id))
            .Select(row => new { row.Id, row.Title }).ToDictionaryAsync(row => row.Id, row => row.Title, token);
        var descriptionIds = descriptions.Select(entry => entry.Entity.EntityId).ToArray();
        var savedDescriptions = await _db.EntityDescriptions.AsNoTracking().Where(row => descriptionIds.Contains(row.EntityId))
            .ToDictionaryAsync(row => row.EntityId, token);
        var trackedEvidence = _db.EntityMetadataFields.Local.ToDictionary(row => (row.EntityId, row.Field));
        var observedAt = DateTimeOffset.UtcNow;

        foreach (var entry in titles) {
            var key = (entry.Entity.Id, MetadataPatchField.Title);
            var current = RefreshEvidence(key);
            var hasSavedTitle = savedTitles.TryGetValue(entry.Entity.Id, out var savedTitle);
            if (current.IsLocked || hasSavedTitle && savedTitle == entry.Entity.Title) {
                if (!hasSavedTitle) throw new DbUpdateConcurrencyException("The protected scanner entity no longer exists.");
                var property = entry.Property(row => row.Title);
                property.CurrentValue = savedTitle!;
                property.OriginalValue = savedTitle!;
                property.IsModified = false;
                continue;
            }
            RecordScan(key, current, string.IsNullOrEmpty(entry.Entity.Title));
        }
        foreach (var entry in descriptions) {
            var key = (entry.Entity.EntityId, MetadataPatchField.Description);
            var current = RefreshEvidence(key);
            savedDescriptions.TryGetValue(entry.Entity.EntityId, out var saved);
            // Descriptions are fill-only during scanning. A concurrently populated value also wins.
            if (current.IsLocked || !string.IsNullOrWhiteSpace(saved?.Value)) {
                if (saved is null) entry.State = EntityState.Detached;
                else {
                    entry.CurrentValues.SetValues(saved);
                    entry.OriginalValues.SetValues(saved);
                    entry.State = EntityState.Unchanged;
                }
                continue;
            }
            if (saved is not null && entry.State == EntityState.Added) {
                entry.State = EntityState.Unchanged;
                entry.OriginalValues.SetValues(saved);
            }
            if (saved?.Value != entry.Entity.Value) RecordScan(key, current, string.IsNullOrEmpty(entry.Entity.Value));
        }
        await _db.SaveChangesAsync(token);

        MetadataFieldEvidence RefreshEvidence((Guid Id, MetadataPatchField Field) key) {
            evidence.TryGetValue(key, out var saved);
            trackedEvidence.TryGetValue(key, out var tracked);
            if (tracked is not null) {
                if (saved is null) { _db.Entry(tracked).State = EntityState.Detached; trackedEvidence.Remove(key); }
                else {
                    _db.Entry(tracked).CurrentValues.SetValues(saved);
                    _db.Entry(tracked).OriginalValues.SetValues(saved);
                    _db.Entry(tracked).State = EntityState.Unchanged;
                }
            }
            return saved?.Evidence() ?? MetadataFieldEvidence.Unknown;
        }

        void RecordScan((Guid Id, MetadataPatchField Field) key, MetadataFieldEvidence current, bool cleared) {
            trackedEvidence.TryGetValue(key, out var row);
            if (row is null) {
                if (evidence.TryGetValue(key, out row)) _db.EntityMetadataFields.Attach(row);
                else { row = new() { EntityId = key.Id, Field = key.Field }; _db.EntityMetadataFields.Add(row); }
                trackedEvidence[key] = row;
            }
            row.Apply(current.WrittenByScan(cleared, observedAt));
        }
    }
}
