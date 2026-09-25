using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs;
using Prismedia.Contracts.Jobs;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Queue;

public sealed partial class JobQueueService : IJobActivityReader {
    #region Static Variables

    private const int MaximumActivityHours = 24 * 7;

    #endregion

    #region Actions - Activity

    /// <inheritdoc />
    /// <remarks>
    /// Only the three timestamps and the target of each run inside the window leave the database, so a
    /// busy day costs one narrow read rather than the dashboard's capped run list. The NSFW wall mirrors
    /// the row-level filter: a run is hidden when its target is an NSFW entity or library root.
    /// </remarks>
    public async Task<JobActivityResponse> ListActivityAsync(int hours, bool hideNsfw, CancellationToken cancellationToken) {
        var window = Math.Clamp(hours, 1, MaximumActivityHours);
        var now = DateTimeOffset.UtcNow;
        var since = now.AddHours(-window);
        var rows = await _db.JobRuns.AsNoTracking()
            .Where(row => row.Status == JobRunStatus.Running
                || (row.FinishedAt ?? row.StartedAt ?? row.CreatedAt) >= since)
            .Select(row => new {
                row.Type,
                row.Status,
                Moment = row.FinishedAt ?? row.StartedAt ?? row.CreatedAt,
                row.TargetEntityId
            })
            .ToListAsync(cancellationToken);
        if (hideNsfw && rows.Count > 0) {
            var targetIds = rows
                .Select(row => Guid.TryParse(row.TargetEntityId, out var id) ? id : (Guid?)null)
                .Where(id => id is not null)
                .Select(id => id!.Value)
                .Distinct()
                .ToArray();
            if (targetIds.Length > 0) {
                var hidden = (await _db.Entities.AsNoTracking()
                        .Where(entity => entity.IsNsfw && targetIds.Contains(entity.Id))
                        .Select(entity => entity.Id)
                        .ToArrayAsync(cancellationToken))
                    .Concat(await _db.LibraryRoots.AsNoTracking()
                        .Where(root => root.IsNsfw && targetIds.Contains(root.Id))
                        .Select(root => root.Id)
                        .ToArrayAsync(cancellationToken))
                    .ToHashSet();
                rows = rows
                    .Where(row => !Guid.TryParse(row.TargetEntityId, out var id) || !hidden.Contains(id))
                    .ToList();
            }
        }

        var buckets = rows
            .GroupBy(row => (row.Type, Start: HourStart(row.Status == JobRunStatus.Running ? now : row.Moment)))
            .Select(group => new JobActivityBucket(
                group.Key.Type,
                group.Key.Start,
                group.Count(),
                group.Count(row => row.Status == JobRunStatus.Failed),
                group.Count(row => row.Status == JobRunStatus.Running)))
            .OrderBy(bucket => bucket.Type)
            .ThenBy(bucket => bucket.Start)
            .ToArray();
        return new JobActivityResponse(window, now, buckets);
    }

    private static DateTimeOffset HourStart(DateTimeOffset moment) {
        var utc = moment.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
    }

    #endregion
}
