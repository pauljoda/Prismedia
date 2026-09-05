using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Alternates exhausted individual episode searches with bounded season-pack recovery.</summary>
public sealed partial class EfMonitorStore {
    private static readonly TimeSpan SeasonPackRetryInterval = TimeSpan.FromHours(6);
    private static readonly AcquisitionStatus[] WorkingPackStatuses = [
        AcquisitionStatus.Pending, AcquisitionStatus.Searching, AcquisitionStatus.Queued,
        AcquisitionStatus.WaitingForDownloadClient, AcquisitionStatus.Downloading,
        AcquisitionStatus.Downloaded, AcquisitionStatus.Importing, AcquisitionStatus.Stopping
    ];

    /// <inheritdoc />
    public async Task<Guid?> CreateSeasonPackRetryAsync(Guid monitorId, CancellationToken cancellationToken) {
        var entityId = await db.Monitors.AsNoTracking()
            .Where(row => row.Id == monitorId && row.Kind == EntityKind.VideoSeason)
            .Select(row => row.EntityId).FirstOrDefaultAsync(cancellationToken);
        if (entityId is not { } seasonId) {
            return null;
        }

        Guid? createdId = null;
        await lifecycle.ExecuteAsync(seasonId, async token => {
            var monitor = await db.Monitors.AsNoTracking().SingleOrDefaultAsync(
                row => row.Id == monitorId && row.Status == MonitorStatus.Active
                    && row.EntityId == seasonId && row.UpgradeChildAcquisitionId == null, token);
            if (monitor is null || await HasDestructiveDescendantClaimAsync(seasonId, monitorId, token)) {
                return;
            }

            var previous = await db.Acquisitions.AsNoTracking()
                .Where(row => row.EntityId == seasonId && row.Kind == EntityKind.VideoSeason)
                .OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id)
                .FirstOrDefaultAsync(token);
            var now = DateTimeOffset.UtcNow;
            if (previous is null || now - previous.CreatedAt < SeasonPackRetryInterval
                || previous.Status is not (AcquisitionStatus.Imported or AcquisitionStatus.Failed or AcquisitionStatus.AwaitingSelection)
                || monitor.AcquisitionId is { } linkedId && linkedId != previous.Id
                || await db.AcquisitionImportHints.AnyAsync(row => row.AcquisitionId == previous.Id && !row.Consumed, token)
                || previous.Status == AcquisitionStatus.AwaitingSelection
                    && await db.ReleaseCandidates.AnyAsync(row => row.AcquisitionId == previous.Id && row.Accepted, token)
                || await HasWorkingSeasonPackAsync(seasonId, token)) {
                return;
            }

            var episodeKind = EntityKind.VideoEpisode.ToCode();
            var missingIds = await db.Entities.AsNoTracking()
                .Where(row => row.ParentEntityId == seasonId && row.KindCode == episodeKind && row.IsWanted
                    && !db.EntityFiles.Any(file => file.EntityId == row.Id && file.Role == EntityFileRole.Source))
                .Select(row => row.Id).ToArrayAsync(token);
            if (missingIds.Length == 0) {
                return;
            }

            // A paused child, unresolved review, held import, or any live attempt is never evidence that
            // individual searching is exhausted. Each missing child must have tried since the prior pack.
            var childMonitors = await db.Monitors.AsNoTracking()
                .Where(row => row.EntityId != null && missingIds.Contains(row.EntityId.Value))
                .ToArrayAsync(token);
            if (childMonitors.Any(row => row.Status != MonitorStatus.Active)
                || missingIds.Any(id => !childMonitors.Any(row => row.EntityId == id))) {
                return;
            }
            var attempts = await db.Acquisitions.AsNoTracking()
                .Where(row => row.EntityId != null && missingIds.Contains(row.EntityId.Value))
                .Select(row => new {
                    row.Id, row.EntityId, row.Status, row.CreatedAt, row.UpdatedAt,
                    HasAccepted = db.ReleaseCandidates.Any(candidate => candidate.AcquisitionId == row.Id && candidate.Accepted)
                }).ToArrayAsync(token);
            foreach (var id in missingIds) {
                var childAttempts = attempts.Where(row => row.EntityId == id).ToArray();
                var latest = childAttempts.OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id).FirstOrDefault();
                if (latest is null || latest.UpdatedAt < previous.CreatedAt
                    || !(latest.Status == AcquisitionStatus.Failed
                        || latest.Status == AcquisitionStatus.AwaitingSelection && !latest.HasAccepted)
                    || childAttempts.Any(row => IsWorkingPackStatus(row.Status, row.HasAccepted)
                        || row.Status == AcquisitionStatus.ManualImportRequired)) {
                    return;
                }
            }

            var retry = new AcquisitionRow {
                Id = Guid.NewGuid(), Kind = EntityKind.VideoSeason, EntityId = seasonId,
                ProfileId = monitor.ProfileId ?? previous.ProfileId,
                TargetLibraryRootId = monitor.TargetLibraryRootId ?? previous.TargetLibraryRootId,
                Status = AcquisitionStatus.Searching,
                StatusMessage = "Individual episode searches were exhausted. Searching season packs for the remaining gaps.",
                Title = previous.Title, Author = previous.Author, Series = previous.Series,
                SeasonNumber = previous.SeasonNumber, Year = previous.Year, PosterUrl = previous.PosterUrl,
                Description = previous.Description, IdentityNamespace = previous.IdentityNamespace,
                IdentityValue = previous.IdentityValue, ExternalIdsJson = previous.ExternalIdsJson,
                SourceUrlsJson = previous.SourceUrlsJson, CreatedAt = now, UpdatedAt = now
            };
            db.Acquisitions.Add(retry);
            await db.SaveChangesAsync(token);
            if (db.Database.IsRelational()) {
                var updated = await db.Monitors.Where(row => row.Id == monitorId
                        && row.Status == MonitorStatus.Active && row.AcquisitionId == monitor.AcquisitionId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(row => row.AcquisitionId, retry.Id)
                        .SetProperty(row => row.UpdatedAt, now), token);
                if (updated == 0) {
                    db.Acquisitions.Remove(retry);
                    await db.SaveChangesAsync(token);
                    return;
                }
            } else {
                var tracked = await db.Monitors.SingleAsync(row => row.Id == monitorId, token);
                tracked.AcquisitionId = retry.Id;
                tracked.UpdatedAt = now;
                await db.SaveChangesAsync(token);
            }
            createdId = retry.Id;
        }, cancellationToken);
        return createdId;
    }

    /// <inheritdoc />
    public async Task<bool> TryStartEpisodeSearchAsync(
        Guid monitorId, Guid acquisitionId, IAcquisitionLifecycleStore acquisitions,
        CancellationToken cancellationToken) {
        var entityId = await db.Acquisitions.AsNoTracking()
            .Where(row => row.Id == acquisitionId && row.Kind == EntityKind.VideoEpisode)
            .Select(row => row.EntityId).FirstOrDefaultAsync(cancellationToken);
        if (entityId is not { } episodeId) {
            return false;
        }

        var started = false;
        // The ancestry lease shares the season monitor/Entity locks with pack recovery. Whichever search
        // claims first becomes visible before the other can inspect exhaustion or publish Searching.
        await lifecycle.ExecuteAsync(episodeId, async token => {
            if (!await db.Monitors.AsNoTracking().AnyAsync(row => row.Id == monitorId
                    && row.Status == MonitorStatus.Active && row.AcquisitionId == acquisitionId, token)
                || await EpisodeHasWorkingSeasonPackAsync(episodeId, token)) {
                return;
            }
            var status = await acquisitions.GetStatusAsync(acquisitionId, token);
            started = status is { } current && AcquisitionSearchJobHandler.CanScheduleSearch(current)
                && (current == AcquisitionStatus.Searching || await acquisitions.TryTransitionStatusAsync(
                    acquisitionId, [current], AcquisitionStatus.Searching, null, token));
        }, cancellationToken);
        return started;
    }

    private async Task<bool> EpisodeHasWorkingSeasonPackAsync(Guid episodeId, CancellationToken cancellationToken) {
        var parentId = await db.Entities.AsNoTracking().Where(row => row.Id == episodeId)
            .Select(row => row.ParentEntityId).FirstOrDefaultAsync(cancellationToken);
        return parentId is { } seasonId && await HasWorkingSeasonPackAsync(seasonId, cancellationToken);
    }

    private async Task<bool> HasWorkingSeasonPackAsync(Guid seasonId, CancellationToken cancellationToken) {
        return await db.Acquisitions.AsNoTracking().AnyAsync(row => row.EntityId == seasonId
            && row.Kind == EntityKind.VideoSeason && (WorkingPackStatuses.Contains(row.Status)
                || row.Status == AcquisitionStatus.AwaitingSelection
                    && db.ReleaseCandidates.Any(candidate => candidate.AcquisitionId == row.Id && candidate.Accepted)), cancellationToken);
    }

    private async Task<HashSet<Guid>> EpisodesCoveredByWorkingPacksAsync(Guid[] episodeIds, CancellationToken cancellationToken) =>
        episodeIds.Length == 0 ? [] : await db.Entities.AsNoTracking()
            .Where(episode => episodeIds.Contains(episode.Id) && episode.ParentEntityId != null
                && db.Acquisitions.Any(pack => pack.EntityId == episode.ParentEntityId
                    && pack.Kind == EntityKind.VideoSeason && (WorkingPackStatuses.Contains(pack.Status)
                        || pack.Status == AcquisitionStatus.AwaitingSelection
                            && db.ReleaseCandidates.Any(candidate => candidate.AcquisitionId == pack.Id && candidate.Accepted))))
            .Select(episode => episode.Id).ToHashSetAsync(cancellationToken);

    private static bool IsWorkingPackStatus(AcquisitionStatus status, bool hasAccepted) =>
        WorkingPackStatuses.Contains(status)
        || status == AcquisitionStatus.AwaitingSelection && hasAccepted;
}
