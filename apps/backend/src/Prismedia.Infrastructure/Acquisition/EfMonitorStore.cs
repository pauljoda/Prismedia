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

    /// <summary>Most upgrade replacements attempted before a monitor gives up and fulfills (best-effort cutoff).</summary>
    private const int MaxUpgradeAttempts = 3;

    /// <summary>Most consecutive fruitless upgrade searches (or pre-download failures) before a monitor gives up.</summary>
    private const int MaxBarrenSearches = 6;

    public async Task<IReadOnlyList<DueMonitor>> ListDueMonitorsAsync(int defaultIntervalMinutes, CancellationToken cancellationToken) {
        // The default BOOK profile's cutoff governs upgrades (the upgrade loop speaks book quality
        // vocabulary). Upgrade-seeking is fully automatic, so it requires both the cutoff toggle and
        // auto-grab; without auto-grab there is no path to act on a found upgrade.
        var profile = await db.BookAcquisitionProfiles.AsNoTracking()
            .Where(p => p.Kind == EntityKind.Book)
            .OrderByDescending(p => p.IsDefault).ThenBy(p => p.CreatedAt)
            .Select(p => new { p.UpgradeUntilCutoff, p.AutoPick, p.CutoffSourceTier, p.CutoffFormatTier })
            .FirstOrDefaultAsync(cancellationToken);
        var upgradeEnabled = profile is { UpgradeUntilCutoff: true, AutoPick: true };
        var cutoff = profile is null ? BookQualityRank.Floor : new BookQualityRank(profile.CutoffSourceTier, profile.CutoffFormatTier);

        // Tracked load (we mutate statuses during reconciliation), joined to each acquisition's status and
        // accepted-candidate count, plus the in-flight upgrade child's status when the interlock is set.
        var rows = await (
            from monitor in db.Monitors
            where monitor.Status == MonitorStatus.Active
            join acquisition in db.Acquisitions on monitor.AcquisitionId equals acquisition.Id into joined
            from acquisition in joined.DefaultIfEmpty()
            select new {
                Monitor = monitor,
                AcquisitionStatus = acquisition == null ? (AcquisitionStatus?)null : acquisition.Status,
                OwnedQuality = acquisition == null ? (BookQualityRank?)null : new BookQualityRank(acquisition.OwnedSourceTier, acquisition.OwnedFormatTier),
                Captured = acquisition != null && acquisition.UpgradeQualityCaptured,
                AcceptedCount = acquisition == null ? 0 : db.ReleaseCandidates.Count(candidate => candidate.AcquisitionId == acquisition.Id && candidate.Accepted),
                ChildStatus = monitor.UpgradeChildAcquisitionId == null ? (AcquisitionStatus?)null
                    : db.Acquisitions.Where(c => c.Id == monitor.UpgradeChildAcquisitionId).Select(c => (AcquisitionStatus?)c.Status).FirstOrDefault(),
                ChildAcceptedCount = monitor.UpgradeChildAcquisitionId == null ? 0
                    : db.ReleaseCandidates.Count(candidate => candidate.AcquisitionId == monitor.UpgradeChildAcquisitionId && candidate.Accepted)
            })
            .ToArrayAsync(cancellationToken);

        var interval = TimeSpan.FromMinutes(Math.Max(1, defaultIntervalMinutes));
        var backoffCap = TimeSpan.FromHours(24);
        var now = DateTimeOffset.UtcNow;
        var due = new List<DueMonitor>();
        var changed = false;

        foreach (var row in rows) {
            var monitor = row.Monitor;
            // A container monitor (an author/artist watched for new works) has no acquisition of its
            // own: it is due for a discovery sync on the plain interval.
            if (monitor.AcquisitionId is null && monitor.EntityId is { } watchedEntityId) {
                if (monitor.LastSearchedAt is null || now - monitor.LastSearchedAt >= interval) {
                    due.Add(new DueMonitor(monitor.Id, null, monitor.Title, IsUpgrade: false, EntityId: watchedEntityId));
                }

                continue;
            }

            // The acquisition was hard-deleted (FK set null) — auto-pause; nothing to re-search.
            if (monitor.AcquisitionId is not { } acquisitionId) {
                monitor.Status = MonitorStatus.Paused;
                monitor.UpdatedAt = now;
                changed = true;
                continue;
            }

            // Reconcile an in-flight upgrade child. A SET interlock that the replace handler has not cleared
            // means the child never reached the replace step (its search found nothing, or its grab/download
            // failed). Clear the interlock, count it as a barren attempt, and wait for the next cooldown. A
            // still-in-flight child (anything not terminal/barren) means an upgrade is genuinely in progress —
            // leave it alone (this is the one-upgrade-at-a-time interlock that protects the in-flight grab).
            if (monitor.UpgradeChildAcquisitionId is not null) {
                // Downloaded/Importing are included so a child orphaned by a crash between marking it Downloaded
                // and enqueuing the replace job (or before the replace job ran) can't freeze the interlock
                // forever — the sweep reclaims it as a barren attempt. In the normal path the replace handler
                // clears the interlock first, so the sweep never sees a set interlock for a resolved child; if a
                // sweep does race a live replace job, the swap still completes correctly (it keys off the child,
                // not the interlock) — at worst the attempt is miscounted as barren, which is benign.
                var childSettled = row.ChildStatus is null
                        or AcquisitionStatus.Failed
                        or AcquisitionStatus.Cancelled
                        or AcquisitionStatus.Downloaded
                        or AcquisitionStatus.Importing
                    || (row.ChildStatus == AcquisitionStatus.AwaitingSelection && row.ChildAcceptedCount == 0);
                if (childSettled) {
                    monitor.UpgradeChildAcquisitionId = null;
                    monitor.BarrenSearches += 1;
                    monitor.UpdatedAt = now;
                    changed = true;
                }

                continue; // upgrade attempt in flight (or just reconciled) — never re-search the same book concurrently
            }

            switch (row.AcquisitionStatus) {
                case AcquisitionStatus.Cancelled:
                    monitor.Status = MonitorStatus.Paused; // user gave up on this acquisition
                    monitor.UpdatedAt = now;
                    changed = true;
                    continue;

                case AcquisitionStatus.Imported:
                    // The wanted item is in hand. Books may keep seeking a higher-quality release (the
                    // upgrade loop speaks book quality vocabulary); every other kind fulfills on import.
                    var kindUpgrades = upgradeEnabled && monitor.Kind == EntityKind.Book;
                    if (!kindUpgrades || !row.Captured || row.OwnedQuality is not { } owned) {
                        // Upgrades off for this monitor, or quality not yet captured (a brief post-import
                        // window — leave Active to retry next sweep rather than fulfilling before the loop
                        // can ever run).
                        if (!kindUpgrades) {
                            monitor.Status = MonitorStatus.Fulfilled;
                            monitor.UpdatedAt = now;
                            changed = true;
                        }

                        continue;
                    }

                    var cutoffMet = owned.Source >= cutoff.Source && owned.Format >= cutoff.Format;
                    var capsHit = monitor.UpgradeAttempts >= MaxUpgradeAttempts || monitor.BarrenSearches >= MaxBarrenSearches;
                    if (cutoffMet || capsHit) {
                        monitor.Status = MonitorStatus.Fulfilled; // best quality reached, or best-effort exhausted
                        monitor.UpdatedAt = now;
                        changed = true;
                        continue;
                    }

                    // Exponential backoff keyed on consecutive barren searches, capped, so a book that never
                    // gets a better release does not hammer indexers.
                    var backoff = TimeSpan.FromMinutes(Math.Min(interval.TotalMinutes * Math.Pow(2, monitor.BarrenSearches), backoffCap.TotalMinutes));
                    if (monitor.LastSearchedAt is null || now - monitor.LastSearchedAt >= backoff) {
                        due.Add(new DueMonitor(monitor.Id, acquisitionId, monitor.Title, IsUpgrade: true));
                    }

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

    public async Task<Guid?> CreateUpgradeChildAsync(Guid monitorId, CancellationToken cancellationToken) {
        // Serialized by the MonitoredSearch singleton job, so the null-interlock check is a safe claim.
        var monitor = await db.Monitors.FirstOrDefaultAsync(m => m.Id == monitorId, cancellationToken);
        if (monitor is null || monitor.UpgradeChildAcquisitionId is not null || monitor.AcquisitionId is not { } parentId) {
            return null;
        }

        var parent = await db.Acquisitions.AsNoTracking().FirstOrDefaultAsync(a => a.Id == parentId, cancellationToken);
        if (parent is null) {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var childId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = childId,
            ProfileId = parent.ProfileId,
            TargetLibraryRootId = parent.TargetLibraryRootId,
            Status = AcquisitionStatus.Pending,
            Title = parent.Title,
            Author = parent.Author,
            Series = parent.Series,
            Year = parent.Year,
            PosterUrl = parent.PosterUrl,
            PluginId = parent.PluginId,
            PluginItemId = parent.PluginItemId,
            ExternalIdsJson = parent.ExternalIdsJson,
            SourceUrlsJson = parent.SourceUrlsJson,
            UpgradeOfAcquisitionId = parentId,
            CreatedAt = now,
            UpdatedAt = now
        });
        monitor.UpgradeChildAcquisitionId = childId;
        monitor.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return childId;
    }

    public async Task ResolveUpgradeChildAsync(Guid childId, bool succeeded, CancellationToken cancellationToken) {
        var monitor = await db.Monitors.FirstOrDefaultAsync(m => m.UpgradeChildAcquisitionId == childId, cancellationToken);
        if (monitor is null) {
            return;
        }

        monitor.UpgradeChildAcquisitionId = null;
        if (succeeded) {
            monitor.UpgradeAttempts += 1;
            monitor.BarrenSearches = 0; // a successful upgrade resets the fruitless-search streak
        } else {
            monitor.BarrenSearches += 1;
        }

        monitor.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
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

    public async Task<MonitorView> StartForEntityAsync(Guid entityId, EntityKind kind, string title, AcquisitionTargeting? targeting, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var row = await db.Monitors.FirstOrDefaultAsync(monitor => monitor.EntityId == entityId, cancellationToken);
        if (row is null) {
            row = new MonitorRow {
                Id = Guid.NewGuid(),
                Kind = kind,
                EntityId = entityId,
                Status = MonitorStatus.Active,
                Title = title,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Monitors.Add(row);
        } else {
            // Idempotent: re-activate an existing (possibly paused) container monitor and refresh its label.
            row.Status = MonitorStatus.Active;
            row.Title = title;
            row.UpdatedAt = now;
        }

        // An explicit request's library/profile choices stick to the monitor so later phantom requests
        // inherit them; a caller with no choices (a sync, the monitor toggle) never clears stored ones.
        if (targeting is not null) {
            row.TargetLibraryRootId = targeting.TargetLibraryRootId;
            row.ProfileId = targeting.ProfileId;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToView(row, null);
    }

    public async Task<MonitorView?> GetByEntityAsync(Guid entityId, CancellationToken cancellationToken) {
        var row = await db.Monitors.AsNoTracking().FirstOrDefaultAsync(monitor => monitor.EntityId == entityId, cancellationToken);
        return row is null ? null : ToView(row, null);
    }

    public async Task<AcquisitionTargeting?> GetTargetingByEntityAsync(Guid entityId, CancellationToken cancellationToken) {
        var row = await db.Monitors.AsNoTracking()
            .Where(monitor => monitor.EntityId == entityId)
            .Select(monitor => new { monitor.TargetLibraryRootId, monitor.ProfileId })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : new AcquisitionTargeting(row.TargetLibraryRootId, row.ProfileId);
    }

    private static MonitorView ToView(MonitorRow row, AcquisitionStatus? acquisitionStatus) =>
        new(row.Id, row.Kind, row.AcquisitionId, row.Status, row.Title, row.Author, acquisitionStatus, row.CreatedAt, row.UpdatedAt, row.EntityId);
}
