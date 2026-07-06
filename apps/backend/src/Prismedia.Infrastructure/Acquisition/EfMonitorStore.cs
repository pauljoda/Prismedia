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
            select new {
                Monitor = monitor,
                AcquisitionStatus = acquisition == null ? (AcquisitionStatus?)null : acquisition.Status,
                // A per-item monitor carries no EntityId of its own, but its acquisition targets the wanted
                // entity — surfacing it lets clients (the Season Pass editor) index monitors by entity id.
                AcquisitionEntityId = acquisition == null ? (Guid?)null : acquisition.EntityId
            })
            .ToArrayAsync(cancellationToken);
        return rows.Select(row => ToView(row.Monitor, row.AcquisitionStatus, row.AcquisitionEntityId)).ToArray();
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

    /// <summary>Default page size for the Wanted lists.</summary>
    private const int DefaultWantedPageSize = 50;

    /// <summary>Ceiling on a Wanted-list page size — keeps a single query cheap at Sonarr scale (~5k missing).</summary>
    private const int MaxWantedPageSize = 200;

    /// <summary>The backoff cap the due sweep and the Wanted lists share: an item that never gets a better release is retried at most daily.</summary>
    private static readonly TimeSpan BackoffCap = TimeSpan.FromHours(24);

    /// <summary>
    /// The base re-search interval the Wanted lists use to project each row's next-search ETA. The due sweep
    /// itself is driven by the configured <c>monitoring.intervalMinutes</c> setting; these read-only list
    /// projections do not inject settings, so they use a fixed base that leaves headroom under
    /// <see cref="BackoffCap"/> so the exponential backoff doubling is visible in the projected ETA. The ETA
    /// is only a display hint — the actual re-search is always scheduled by the sweep against the live
    /// setting — so a base that differs from a customized interval never causes an early or missed search,
    /// only a slightly-off displayed ETA.
    /// </summary>
    private static readonly TimeSpan WantedBaseInterval = TimeSpan.FromMinutes(360);

    /// <summary>
    /// The upgrade-relevant fields of one profile kind's governing profile, projected for the due sweep:
    /// whether the loop is on (<see cref="UpgradeUntilCutoff"/> plus <see cref="AutoPick"/>) and the cutoff in
    /// both vocabularies (the book source/format tiers, and the media ladder <see cref="CutoffQuality"/> code).
    /// </summary>
    private sealed record UpgradePolicy(
        EntityKind Kind,
        bool UpgradeUntilCutoff,
        bool AutoPick,
        BookSourceTier CutoffSourceTier,
        BookFormatTier CutoffFormatTier,
        string? CutoffQuality,
        int? CutoffFormatScore);

    /// <summary>
    /// The next-search cadence for a barren monitor: the base interval scaled by exponential backoff keyed on
    /// consecutive barren searches, capped at <see cref="BackoffCap"/>. Shared by the due sweep (which uses it
    /// to decide whether a monitor is due) and the Wanted lists (which surface it as each row's next-search
    /// ETA), so the surfaced ETA can never drift from the schedule the sweep actually follows. A monitor with
    /// no barren searches is retried on the plain interval (2^0 = 1).
    /// </summary>
    private static TimeSpan BackoffFor(TimeSpan interval, int barrenSearches) =>
        TimeSpan.FromMinutes(Math.Min(interval.TotalMinutes * Math.Pow(2, barrenSearches), BackoffCap.TotalMinutes));

    /// <summary>
    /// Resolves the governing <see cref="UpgradePolicy"/> per profile kind the same way the profile store
    /// does — the default profile of a kind first, then the oldest — and returns a lookup keyed by profile
    /// kind (the first row per kind wins). Shared by the due sweep and the cutoff-unmet list so both judge
    /// cutoffs against the same profiles.
    /// </summary>
    private async Task<Dictionary<EntityKind, UpgradePolicy>> ResolveUpgradePoliciesAsync(CancellationToken cancellationToken) {
        var profiles = await db.BookAcquisitionProfiles.AsNoTracking()
            .OrderByDescending(p => p.IsDefault).ThenBy(p => p.CreatedAt)
            .Select(p => new UpgradePolicy(p.Kind, p.UpgradeUntilCutoff, p.AutoPick, p.CutoffSourceTier, p.CutoffFormatTier, p.CutoffQuality, p.CutoffFormatScore))
            .ToArrayAsync(cancellationToken);
        var policyByKind = new Dictionary<EntityKind, UpgradePolicy>();
        foreach (var policy in profiles) {
            policyByKind.TryAdd(policy.Kind, policy);
        }

        return policyByKind;
    }

    /// <summary>
    /// The cutoff verdict for an imported acquisition, expressed in the vocabulary of its <paramref name="kind"/>.
    /// The single source of truth for "is this owned copy at/above cutoff?", shared by the due sweep (which
    /// fulfills a monitor once cutoff is met) and the cutoff-unmet list (which lists exactly the ones that are
    /// NOT yet met). Books compare per-axis source/format tiers; media compare both the ladder position and
    /// the custom-format score. When the kind does not upgrade (upgrade off, or a multi-file/unsupported kind)
    /// the copy is treated as at cutoff (nothing to chase). When owned quality has not been captured yet the
    /// verdict is <c>OwnedQuality</c> null and <c>CutoffMet</c> false — the caller decides whether "not yet
    /// judgeable" belongs in the list.
    /// </summary>
    private static (bool KindUpgrades, bool HaveOwned, bool CutoffMet, string? OwnedQuality, string? CutoffQuality) EvaluateCutoff(
        EntityKind kind,
        UpgradePolicy? policy,
        bool captured,
        BookQualityRank ownedBookQuality,
        string? ownedMediaQuality,
        int ownedFormatScore) {
        var upgradeEnabled = policy is { UpgradeUntilCutoff: true, AutoPick: true };
        var isBook = kind == EntityKind.Book;
        var kindUpgrades = upgradeEnabled && (isBook || MediaQualityLadder.IsUpgradeCapableKind(kind));
        if (!kindUpgrades) {
            return (KindUpgrades: false, HaveOwned: false, CutoffMet: true, OwnedQuality: null, CutoffQuality: null);
        }

        if (isBook) {
            var cutoff = policy is null ? BookQualityRank.Floor : new BookQualityRank(policy.CutoffSourceTier, policy.CutoffFormatTier);
            var cutoffText = $"{cutoff.Source.ToCode()}/{cutoff.Format.ToCode()}";
            if (!captured) {
                return (KindUpgrades: true, HaveOwned: false, CutoffMet: false, OwnedQuality: null, CutoffQuality: cutoffText);
            }

            var cutoffMet = ownedBookQuality.Source >= cutoff.Source && ownedBookQuality.Format >= cutoff.Format;
            var ownedText = $"{ownedBookQuality.Source.ToCode()}/{ownedBookQuality.Format.ToCode()}";
            return (KindUpgrades: true, HaveOwned: true, cutoffMet, ownedText, cutoffText);
        }

        // Media: owned quality must be captured AND a real ladder code before it can be judged.
        var haveOwned = captured && !string.IsNullOrWhiteSpace(ownedMediaQuality);
        var cutoffCode = policy?.CutoffQuality;
        if (!haveOwned) {
            return (KindUpgrades: true, HaveOwned: false, CutoffMet: false, OwnedQuality: null, CutoffQuality: cutoffCode);
        }

        var ownedPosition = MediaQualityLadder.PositionOf(kind, ownedMediaQuality);
        var cutoffPosition = MediaQualityLadder.PositionOf(kind, cutoffCode);
        var ladderCutoffMet = cutoffPosition == 0 || ownedPosition >= cutoffPosition;
        var formatCutoffMet = policy?.CutoffFormatScore is not { } formatCutoff || ownedFormatScore >= formatCutoff;
        return (KindUpgrades: true, HaveOwned: true, CutoffMet: ladderCutoffMet && formatCutoffMet, ownedMediaQuality, cutoffCode);
    }

    public async Task<IReadOnlyList<DueMonitor>> ListDueMonitorsAsync(int defaultIntervalMinutes, CancellationToken cancellationToken) {
        // The default profile of each kind governs its upgrades. Upgrade-seeking is fully automatic, so it
        // requires both the cutoff toggle and auto-grab; without auto-grab there is no path to act on a found
        // upgrade. Books gate on the source/format cutoff tiers; media kinds (movies, single episodes) gate on
        // the ladder cutoff-quality code. Resolved per profile kind (default first, then oldest), the same way
        // the profile store resolves rules.
        var policyByKind = await ResolveUpgradePoliciesAsync(cancellationToken);

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
                OwnedMediaQuality = acquisition == null ? null : acquisition.OwnedMediaQuality,
                OwnedFormatScore = acquisition == null ? 0 : acquisition.OwnedFormatScore,
                Captured = acquisition != null && acquisition.UpgradeQualityCaptured,
                AcceptedCount = acquisition == null ? 0 : db.ReleaseCandidates.Count(candidate => candidate.AcquisitionId == acquisition.Id && candidate.Accepted),
                ChildStatus = monitor.UpgradeChildAcquisitionId == null ? (AcquisitionStatus?)null
                    : db.Acquisitions.Where(c => c.Id == monitor.UpgradeChildAcquisitionId).Select(c => (AcquisitionStatus?)c.Status).FirstOrDefault(),
                ChildAcceptedCount = monitor.UpgradeChildAcquisitionId == null ? 0
                    : db.ReleaseCandidates.Count(candidate => candidate.AcquisitionId == monitor.UpgradeChildAcquisitionId && candidate.Accepted)
            })
            .ToArrayAsync(cancellationToken);

        var interval = TimeSpan.FromMinutes(Math.Max(1, defaultIntervalMinutes));
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
                    // The wanted item is in hand. An upgrade-capable kind (a book, or a single-file movie/
                    // episode) with its profile's upgrade loop on keeps seeking a higher-quality release; every
                    // other kind — a season pack, an album, or any kind whose profile has upgrades off —
                    // fulfills on import. The cutoff comparison speaks each kind's own vocabulary; the shared
                    // evaluator below owns that vocabulary so the due sweep and the cutoff-unmet list agree.
                    // DIVERGENCE FROM SONARR (deliberate): once cutoff is met this monitor fulfills, so a
                    // PROPER/REPACK revision upgrade only ever happens while the owned copy is still BELOW
                    // cutoff (via MediaUpgradeSpecification's same-quality-higher-revision accept). Sonarr keeps
                    // chasing propers even past cutoff (its cutoff gates quality, not revision); Prismedia does
                    // not re-open a fulfilled monitor purely to acquire a better revision, keeping the loop
                    // bounded and avoiding late re-grabs of content the user has likely already consumed.
                    var policy = policyByKind.GetValueOrDefault(AcquisitionProfileKinds.For(monitor.Kind));
                    var verdict = EvaluateCutoff(
                        monitor.Kind, policy, row.Captured, row.OwnedQuality ?? BookQualityRank.Floor, row.OwnedMediaQuality, row.OwnedFormatScore);
                    if (!verdict.KindUpgrades) {
                        // Upgrades off for this kind — fulfill immediately (the original on-import behavior).
                        monitor.Status = MonitorStatus.Fulfilled;
                        monitor.UpdatedAt = now;
                        changed = true;
                        continue;
                    }

                    // Quality must be captured (and, for media, a real ladder code) before the loop can judge
                    // the owned copy — leave it Active and retry next sweep rather than fulfilling too early.
                    if (!verdict.HaveOwned) {
                        continue;
                    }

                    var capsHit = monitor.UpgradeAttempts >= MaxUpgradeAttempts || monitor.BarrenSearches >= MaxBarrenSearches;
                    if (verdict.CutoffMet || capsHit) {
                        monitor.Status = MonitorStatus.Fulfilled; // best quality reached, or best-effort exhausted
                        monitor.UpdatedAt = now;
                        changed = true;
                        continue;
                    }

                    // Exponential backoff keyed on consecutive barren searches, capped, so an item that never
                    // gets a better release does not hammer indexers.
                    var backoff = BackoffFor(interval, monitor.BarrenSearches);
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

    public async Task<WantedPage> ListMissingAsync(int page, int pageSize, EntityKind? kind, CancellationToken cancellationToken) {
        var take = Math.Clamp(pageSize <= 0 ? DefaultWantedPageSize : pageSize, 1, MaxWantedPageSize);
        var skip = Math.Max(0, page - 1) * take;

        // A "missing" row is an ACTIVE, per-item monitor whose acquisition is present but NOT Imported. The
        // AcquisitionId != null gate excludes both container discovery follows (EntityId set, no acquisition)
        // and orphans (the SetNull FK nulls AcquisitionId when the acquisition is hard-deleted, and the sweep
        // then pauses them). The left join's null-acquisition branch is defensive only — with the SetNull FK
        // an Active monitor always has a live acquisition — so it costs nothing yet never surprises. The
        // filter, the count, and the page slice all run in SQL so the query stays cheap at ~5k scale.
        var query =
            from monitor in db.Monitors.AsNoTracking()
            where monitor.Status == MonitorStatus.Active && monitor.AcquisitionId != null
            join acquisition in db.Acquisitions.AsNoTracking() on monitor.AcquisitionId equals acquisition.Id into joined
            from acquisition in joined.DefaultIfEmpty()
            where acquisition == null || acquisition.Status != AcquisitionStatus.Imported
            where kind == null || monitor.Kind == kind
            orderby monitor.CreatedAt descending
            select new {
                monitor.Id,
                monitor.AcquisitionId,
                monitor.EntityId,
                monitor.Kind,
                monitor.Title,
                MonitorStatus = monitor.Status,
                monitor.LastSearchedAt,
                monitor.BarrenSearches,
                monitor.Author,
                AcquisitionStatus = acquisition == null ? (AcquisitionStatus?)null : acquisition.Status,
                PosterUrl = acquisition == null ? null : acquisition.PosterUrl
            };

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.Skip(skip).Take(take).ToArrayAsync(cancellationToken);

        var items = rows.Select(row => new WantedListItem(
            row.Id,
            row.AcquisitionId,
            row.EntityId,
            row.Kind,
            row.Title,
            row.MonitorStatus,
            row.AcquisitionStatus,
            row.LastSearchedAt,
            // A missing item is re-searched on the plain interval (its owned copy is nonexistent, so the
            // barren-search backoff only governs upgrade re-searches). Newer barren counts still stretch the
            // ETA, mirroring the sweep's exponential backoff.
            NextSearchAt: row.LastSearchedAt is { } last ? last + BackoffFor(WantedBaseInterval, row.BarrenSearches) : null,
            OwnedQuality: null,
            CutoffQuality: null,
            row.BarrenSearches,
            row.PosterUrl,
            row.Author)).ToArray();

        return new WantedPage(items, total);
    }

    public async Task<WantedPage> ListCutoffUnmetAsync(int page, int pageSize, EntityKind? kind, CancellationToken cancellationToken) {
        var take = Math.Clamp(pageSize <= 0 ? DefaultWantedPageSize : pageSize, 1, MaxWantedPageSize);
        var skip = Math.Max(0, page - 1) * take;

        var policyByKind = await ResolveUpgradePoliciesAsync(cancellationToken);

        // The SQL filter is "active monitor whose acquisition IS Imported" (of the requested kind). That is a
        // superset of the true cutoff-unmet set — it also holds fulfilled-but-not-yet-swept monitors and
        // at-cutoff copies — so Total is an UPPER BOUND (documented on the port). The exact cutoff verdict
        // needs per-kind profiles and the owned-quality math, which we run in memory over the materialized
        // page only (never over the whole 5k set).
        var query =
            from monitor in db.Monitors.AsNoTracking()
            where monitor.Status == MonitorStatus.Active && monitor.AcquisitionId != null
            join acquisition in db.Acquisitions.AsNoTracking() on monitor.AcquisitionId equals acquisition.Id into joined
            from acquisition in joined
            where acquisition.Status == AcquisitionStatus.Imported
            where kind == null || monitor.Kind == kind
            orderby monitor.CreatedAt descending
            select new {
                monitor.Id,
                monitor.AcquisitionId,
                monitor.EntityId,
                monitor.Kind,
                monitor.Title,
                MonitorStatus = monitor.Status,
                monitor.LastSearchedAt,
                monitor.BarrenSearches,
                monitor.Author,
                acquisition.OwnedSourceTier,
                acquisition.OwnedFormatTier,
                acquisition.OwnedMediaQuality,
                acquisition.OwnedFormatScore,
                acquisition.UpgradeQualityCaptured,
                acquisition.PosterUrl
            };

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.Skip(skip).Take(take).ToArrayAsync(cancellationToken);

        var items = new List<WantedListItem>(rows.Length);
        foreach (var row in rows) {
            var policy = policyByKind.GetValueOrDefault(AcquisitionProfileKinds.For(row.Kind));
            var verdict = EvaluateCutoff(
                row.Kind, policy, row.UpgradeQualityCaptured,
                new BookQualityRank(row.OwnedSourceTier, row.OwnedFormatTier), row.OwnedMediaQuality, row.OwnedFormatScore);

            // Drop rows the sweep would (or already did) fulfill: kinds that never upgrade, copies at/above
            // cutoff. A not-yet-captured copy stays — it is genuinely below any cutoff until proven otherwise,
            // matching the sweep leaving it Active.
            if (!verdict.KindUpgrades || (verdict.HaveOwned && verdict.CutoffMet)) {
                continue;
            }

            items.Add(new WantedListItem(
                row.Id,
                row.AcquisitionId,
                row.EntityId,
                row.Kind,
                row.Title,
                row.MonitorStatus,
                AcquisitionStatus.Imported,
                row.LastSearchedAt,
                // Cutoff-unmet re-searches are upgrade searches, governed by the barren-search backoff.
                NextSearchAt: row.LastSearchedAt is { } last ? last + BackoffFor(WantedBaseInterval, row.BarrenSearches) : null,
                verdict.OwnedQuality,
                verdict.CutoffQuality,
                row.BarrenSearches,
                row.PosterUrl,
                row.Author));
        }

        return new WantedPage(items, total);
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
            // The child must inherit the parent's kind so its search runs the parent's decision engine and
            // category range, and the replace/import paths speak the right vocabulary. (Books happened to work
            // on the default kind; a movie/episode upgrade child would misbehave without this.)
            Kind = parent.Kind,
            ProfileId = parent.ProfileId,
            TargetLibraryRootId = parent.TargetLibraryRootId,
            Status = AcquisitionStatus.Pending,
            Title = parent.Title,
            Author = parent.Author,
            Series = parent.Series,
            SeasonNumber = parent.SeasonNumber,
            EpisodeNumber = parent.EpisodeNumber,
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

    public async Task<MonitorView> StartForEntityAsync(Guid entityId, EntityKind kind, string title, AcquisitionTargeting? targeting, MonitorPreset? preset, CancellationToken cancellationToken) {
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

        // Likewise the monitoring preset: an explicit request records the chosen preset (governing whether
        // future syncs auto-monitor new works); a sync passes null and never overwrites what was chosen.
        if (preset is { } chosenPreset) {
            row.Preset = chosenPreset;
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

    public async Task<MonitorPreset?> GetPresetByEntityAsync(Guid entityId, CancellationToken cancellationToken) {
        var row = await db.Monitors.AsNoTracking()
            .Where(monitor => monitor.EntityId == entityId)
            .Select(monitor => new { monitor.Preset })
            .FirstOrDefaultAsync(cancellationToken);
        return row?.Preset;
    }

    /// <summary>
    /// Projects a monitor row to its view. <paramref name="acquisitionEntityId"/> is the wanted entity the
    /// monitor's acquisition targets (a per-item monitor has none of its own): the view's EntityId prefers
    /// the container monitor's own EntityId, then the acquisition's, so both container and per-item monitors
    /// surface an entity id clients can index by.
    /// </summary>
    private static MonitorView ToView(MonitorRow row, AcquisitionStatus? acquisitionStatus, Guid? acquisitionEntityId = null) =>
        new(row.Id, row.Kind, row.AcquisitionId, row.Status, row.Title, row.Author, acquisitionStatus, row.CreatedAt, row.UpdatedAt, row.EntityId ?? acquisitionEntityId, row.Preset);
}
