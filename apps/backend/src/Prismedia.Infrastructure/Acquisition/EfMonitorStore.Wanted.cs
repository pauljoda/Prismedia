using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Current-policy wanted-list projections and pagination for acquisition monitors.</summary>
public sealed partial class EfMonitorStore {
    public async Task<WantedPage> ListMissingAsync(int page, int pageSize, EntityKind? kind, CancellationToken cancellationToken) {
        var take = Math.Clamp(pageSize <= 0 ? DefaultWantedPageSize : pageSize, 1, MaxWantedPageSize);
        var skip = Math.Max(0, page - 1) * take;

        // A "missing" row is an ACTIVE monitor whose acquisition is present but NOT Imported. The
        // AcquisitionId != null gate excludes Entity-only stable intent and legacy orphans (the SetNull FK
        // nulls AcquisitionId when the acquisition is hard-deleted, and the sweep
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
        var skip = (long)(Math.Max(1, page) - 1) * take;

        var policies = await ResolveUpgradePoliciesAsync(cancellationToken);

        // Profile and subtitle-aware cutoff decisions must precede pagination. Stream the scalar projection
        // once and retain only the requested page, sharing the monitor sweep's policy math without loading
        // an entire catalog or issuing per-item queries. Count only actual cutoff-unmet matches.
        var measuredVideos = OwnedVideoEvidence.CurrentSources(db);
        var query =
            from monitor in db.Monitors.AsNoTracking()
            where monitor.Status == MonitorStatus.Active && monitor.AcquisitionId != null
            join acquisition in db.Acquisitions.AsNoTracking() on monitor.AcquisitionId equals acquisition.Id into joined
            from acquisition in joined
            join measured in measuredVideos on monitor.EntityId equals measured.EntityId into measurements
            from measured in measurements.DefaultIfEmpty()
            where acquisition.Status == AcquisitionStatus.Imported
            where kind == null || monitor.Kind == kind
            orderby monitor.CreatedAt descending, monitor.Id
            select new {
                MeasuredWidth = measured == null ? null : measured.Width,
                MeasuredHeight = measured == null ? null : measured.Height,
                monitor.Id,
                monitor.AcquisitionId,
                monitor.EntityId,
                monitor.Kind,
                monitor.Title,
                MonitorStatus = monitor.Status,
                monitor.LastSearchedAt,
                monitor.BarrenSearches,
                monitor.Author,
                acquisition.ProfileId,
                acquisition.OwnedSourceTier,
                acquisition.OwnedFormatTier,
                acquisition.OwnedMediaQuality,
                acquisition.OwnedFormatScore,
                acquisition.UpgradeQualityCaptured,
                HasSubtitles = monitor.EntityId != null
                    && db.EntitySubtitles.Any(subtitle => subtitle.EntityId == monitor.EntityId),
                SubtitleStatusKnown = monitor.EntityId == null
                    || db.EntitySubtitles.Any(subtitle => subtitle.EntityId == monitor.EntityId)
                    || db.EntitySubtitleStates.Any(state => state.EntityId == monitor.EntityId && state.SubtitlesExtractedAt != null),
                acquisition.PosterUrl
            };

        var total = 0;
        var items = new List<WantedListItem>(take);
        await foreach (var row in query.AsAsyncEnumerable().WithCancellation(cancellationToken)) {
            var policy = policies.Resolve(row.ProfileId, row.Kind);
            var verdict = EvaluateCutoff(
                row.Kind, policy, row.UpgradeQualityCaptured,
                new BookQualityRank(row.OwnedSourceTier, row.OwnedFormatTier),
                row.OwnedMediaQuality,
                row.OwnedFormatScore,
                row.EntityId is not null,
                row.SubtitleStatusKnown,
                row.HasSubtitles,
                VideoPayloadProfileValidation.ResolutionTier(row.MeasuredWidth, row.MeasuredHeight));

            // Drop rows the sweep would (or already did) fulfill: kinds that never upgrade, copies at/above
            // cutoff. A not-yet-captured copy stays — it is genuinely below any cutoff until proven otherwise,
            // matching the sweep leaving it Active.
            if (!verdict.KindUpgrades || (verdict.HaveOwned && verdict.CutoffMet)) {
                continue;
            }
            var position = total++;
            if (position < skip || items.Count >= take) {
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
                NextSearchAt: row.LastSearchedAt is { } last && !ProfileChangedSinceSearch(policy, last)
                    ? last + BackoffFor(WantedBaseInterval, row.BarrenSearches) : null,
                verdict.OwnedQuality,
                verdict.CutoffQuality,
                row.BarrenSearches,
                row.PosterUrl,
                row.Author));
        }

        return new WantedPage(items, total);
    }

}
