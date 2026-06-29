using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>EF-backed store for monitors, including the reconcile + due logic the scheduled sweep relies on.</summary>
public sealed class EfMonitorStore(PrismediaDbContext db) : IMonitorStore {
    public async Task<MonitorView> StartAsync(Guid acquisitionId, EntityKind kind, string title, string? author, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var row = await db.Monitors.FirstOrDefaultAsync(monitor => monitor.AcquisitionId == acquisitionId, cancellationToken);
        if (row is null) {
            row = new MonitorRow {
                Id = Guid.NewGuid(),
                Kind = kind,
                AcquisitionId = acquisitionId,
                Status = MonitorStatus.Active,
                Title = title,
                Author = author,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Monitors.Add(row);
        } else {
            // Idempotent: re-activate an existing (possibly paused/fulfilled) monitor and refresh its label.
            row.Status = MonitorStatus.Active;
            row.Title = title;
            row.Author = author;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        var acquisitionStatus = await db.Acquisitions.AsNoTracking()
            .Where(acquisition => acquisition.Id == acquisitionId)
            .Select(acquisition => (AcquisitionStatus?)acquisition.Status)
            .FirstOrDefaultAsync(cancellationToken);
        return ToView(row, acquisitionStatus);
    }

    public async Task<bool> DeleteAsync(Guid monitorId, CancellationToken cancellationToken) {
        var row = await db.Monitors.FirstOrDefaultAsync(monitor => monitor.Id == monitorId, cancellationToken);
        if (row is null) {
            return false;
        }

        db.Monitors.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetStatusAsync(Guid monitorId, MonitorStatus status, CancellationToken cancellationToken) {
        var row = await db.Monitors.FirstOrDefaultAsync(monitor => monitor.Id == monitorId, cancellationToken);
        if (row is null) {
            return false;
        }

        row.Status = status;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<MonitorView>> ListAsync(CancellationToken cancellationToken) {
        var rows = await (
            from monitor in db.Monitors.AsNoTracking()
            join acquisition in db.Acquisitions.AsNoTracking() on monitor.AcquisitionId equals acquisition.Id into joined
            from acquisition in joined.DefaultIfEmpty()
            orderby monitor.CreatedAt descending
            select new { Monitor = monitor, AcquisitionStatus = acquisition == null ? (AcquisitionStatus?)null : acquisition.Status })
            .ToArrayAsync(cancellationToken);
        return rows.Select(row => ToView(row.Monitor, row.AcquisitionStatus)).ToArray();
    }

    public async Task<MonitorView?> GetByAcquisitionAsync(Guid acquisitionId, CancellationToken cancellationToken) {
        var row = await db.Monitors.AsNoTracking().FirstOrDefaultAsync(monitor => monitor.AcquisitionId == acquisitionId, cancellationToken);
        if (row is null) {
            return null;
        }

        var acquisitionStatus = await db.Acquisitions.AsNoTracking()
            .Where(acquisition => acquisition.Id == acquisitionId)
            .Select(acquisition => (AcquisitionStatus?)acquisition.Status)
            .FirstOrDefaultAsync(cancellationToken);
        return ToView(row, acquisitionStatus);
    }

    public Task<bool> HasActiveMonitorsAsync(CancellationToken cancellationToken) =>
        // Any active monitor (including one whose acquisition was deleted) warrants a sweep — the sweep
        // reconciles orphaned/fulfilled monitors even when none are due for an actual re-search.
        db.Monitors.AnyAsync(monitor => monitor.Status == MonitorStatus.Active, cancellationToken);

    public async Task<IReadOnlyList<DueMonitor>> ListDueMonitorsAsync(int defaultIntervalMinutes, CancellationToken cancellationToken) {
        // Tracked load (we mutate statuses during reconciliation), joined to each acquisition's status and
        // accepted-candidate count (used to tell "found, awaiting the user" apart from "nothing found yet").
        var rows = await (
            from monitor in db.Monitors
            where monitor.Status == MonitorStatus.Active
            join acquisition in db.Acquisitions on monitor.AcquisitionId equals acquisition.Id into joined
            from acquisition in joined.DefaultIfEmpty()
            select new {
                Monitor = monitor,
                AcquisitionStatus = acquisition == null ? (AcquisitionStatus?)null : acquisition.Status,
                AcceptedCount = acquisition == null ? 0 : db.ReleaseCandidates.Count(candidate => candidate.AcquisitionId == acquisition.Id && candidate.Accepted)
            })
            .ToArrayAsync(cancellationToken);

        var interval = TimeSpan.FromMinutes(Math.Max(1, defaultIntervalMinutes));
        var now = DateTimeOffset.UtcNow;
        var due = new List<DueMonitor>();
        var changed = false;

        foreach (var row in rows) {
            var monitor = row.Monitor;
            // The acquisition was hard-deleted (FK set null) — auto-pause; nothing to re-search.
            if (monitor.AcquisitionId is not { } acquisitionId) {
                monitor.Status = MonitorStatus.Paused;
                monitor.UpdatedAt = now;
                changed = true;
                continue;
            }

            switch (row.AcquisitionStatus) {
                case AcquisitionStatus.Imported:
                    monitor.Status = MonitorStatus.Fulfilled; // got it — stop searching
                    monitor.UpdatedAt = now;
                    changed = true;
                    continue;
                case AcquisitionStatus.Cancelled:
                    monitor.Status = MonitorStatus.Paused; // user gave up on this acquisition
                    monitor.UpdatedAt = now;
                    changed = true;
                    continue;
            }

            // Re-search ONLY when the item is genuinely still missing: a failed attempt, or a search that
            // turned up no acceptable release. Every other state — Pending/Searching (a search is already in
            // progress), Queued/Downloading/Downloaded/Importing (a grab is in flight), AwaitingSelection
            // WITH candidates (found, awaiting the user or already auto-grabbed), ManualImportRequired (needs
            // a human) — is left untouched. Re-searching an in-flight item would reset its status and, with
            // auto-pick on, delete the live torrent and re-grab — so this gate is what keeps monitoring safe.
            var stillMissing = row.AcquisitionStatus == AcquisitionStatus.Failed
                || (row.AcquisitionStatus == AcquisitionStatus.AwaitingSelection && row.AcceptedCount == 0);
            if (!stillMissing) {
                continue;
            }

            if (monitor.LastSearchedAt is null || now - monitor.LastSearchedAt >= interval) {
                due.Add(new DueMonitor(monitor.Id, acquisitionId, monitor.Title));
            }
        }

        if (changed) {
            await db.SaveChangesAsync(cancellationToken);
        }

        return due;
    }

    public async Task MarkSearchedAsync(Guid monitorId, CancellationToken cancellationToken) {
        var row = await db.Monitors.FirstOrDefaultAsync(monitor => monitor.Id == monitorId, cancellationToken);
        if (row is null) {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        row.LastSearchedAt = now;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static MonitorView ToView(MonitorRow row, AcquisitionStatus? acquisitionStatus) =>
        new(row.Id, row.Kind, row.AcquisitionId, row.Status, row.Title, row.Author, acquisitionStatus, row.CreatedAt, row.UpdatedAt);
}
