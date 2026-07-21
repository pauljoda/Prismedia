using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

public sealed partial class EfMonitorStore {
    /// <inheritdoc />
    public async Task<IReadOnlyList<MonitorView>> StartForEntitiesAsync(
        IReadOnlyCollection<EntityMonitorStart> starts,
        CancellationToken cancellationToken) {
        var intents = starts.DistinctBy(start => start.EntityId).ToArray();
        if (intents.Length == 0) {
            return [];
        }

        var entityIds = intents.Select(intent => intent.EntityId).ToArray();
        var acquisitionTargets = await db.Acquisitions.AsNoTracking()
            .Where(acquisition => acquisition.EntityId != null
                && entityIds.Contains(acquisition.EntityId.Value))
            .Select(acquisition => new {
                acquisition.Id,
                EntityId = acquisition.EntityId!.Value,
                acquisition.BookRendition
            })
            .ToArrayAsync(cancellationToken);
        var acquisitionIds = acquisitionTargets.Select(acquisition => acquisition.Id).ToArray();
        var rows = await db.Monitors
            .Where(monitor => monitor.EntityId != null && entityIds.Contains(monitor.EntityId.Value)
                || monitor.AcquisitionId != null && acquisitionIds.Contains(monitor.AcquisitionId.Value))
            .ToArrayAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var selectedRows = new Dictionary<Guid, MonitorRow>();
        foreach (var intent in intents) {
            var rendition = intent.Kind == EntityKind.Book ? BookRendition.Ebook : (BookRendition?)null;
            var entityAcquisitions = acquisitionTargets
                .Where(acquisition => acquisition.EntityId == intent.EntityId)
                .ToArray();
            var matchingAcquisitionIds = entityAcquisitions
                .Where(acquisition => MatchesRendition(acquisition.BookRendition, rendition))
                .Select(acquisition => acquisition.Id)
                .ToHashSet();
            var allAcquisitionIds = entityAcquisitions.Select(acquisition => acquisition.Id).ToHashSet();
            var candidates = rows.Where(monitor =>
                monitor.EntityId == intent.EntityId && MatchesRendition(monitor.BookRendition, rendition)
                || monitor.AcquisitionId is { } acquisitionId && matchingAcquisitionIds.Contains(acquisitionId)
                || (monitor.Status is MonitorStatus.Stopping or MonitorStatus.DeletingFiles)
                    && (monitor.EntityId == intent.EntityId
                        || monitor.AcquisitionId is { } claimedAcquisitionId
                            && allAcquisitionIds.Contains(claimedAcquisitionId)))
                .ToArray();
            if (candidates.Any(monitor => monitor.Status is MonitorStatus.Stopping or MonitorStatus.DeletingFiles)) {
                throw LifecycleClaimConflict();
            }

            var row = candidates.FirstOrDefault(monitor =>
                    monitor.EntityId == intent.EntityId && monitor.BookRendition == rendition)
                ?? candidates.FirstOrDefault(monitor => rendition == BookRendition.Ebook
                    && monitor.EntityId == intent.EntityId && monitor.BookRendition == null)
                ?? candidates.FirstOrDefault();
            if (row is null) {
                row = new MonitorRow {
                    Id = Guid.NewGuid(),
                    CreatedAt = now
                };
                db.Monitors.Add(row);
            }

            row.Kind = intent.Kind;
            row.BookRendition = rendition;
            row.EntityId = intent.EntityId;
            row.Status = MonitorStatus.Active;
            row.Title = intent.Title;
            row.UpdatedAt = now;
            if (intent.Targeting is { } targeting) {
                row.TargetLibraryRootId = targeting.TargetLibraryRootId;
                row.ProfileId = targeting.ProfileId;
            }
            if (intent.Preset is { } preset) {
                row.Preset = preset;
            }

            var duplicates = candidates.Where(candidate => candidate.Id != row.Id).ToArray();
            if (duplicates.Length > 0) {
                db.Monitors.RemoveRange(duplicates);
            }
            selectedRows[intent.EntityId] = row;
        }

        try {
            await db.SaveChangesAsync(cancellationToken);
        } catch (DbUpdateConcurrencyException) {
            db.ChangeTracker.Clear();
            throw LifecycleClaimConflict();
        }
        return intents.Select(intent => ToView(selectedRows[intent.EntityId], null)).ToArray();
    }

    private static bool MatchesRendition(BookRendition? candidate, BookRendition? expected) =>
        candidate == expected || expected == BookRendition.Ebook && candidate is null;
}
