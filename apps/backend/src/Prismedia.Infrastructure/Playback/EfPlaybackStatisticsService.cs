using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Playback;
using Prismedia.Application.Security;
using Prismedia.Contracts.Playback;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Playback;

/// <summary>
/// EF Core read projection for playback statistics, scoped to the requested user's events
/// unless an administrator has explicitly requested the all-users projection.
/// </summary>
public sealed class EfPlaybackStatisticsService(PrismediaDbContext db, ICurrentUserContext currentUser) : IPlaybackStatisticsService {
    private const int TopEntityLimit = 12;
    private const int RecentEventLimit = 30;

    /// <summary>Largest wall-clock offset from UTC any real time zone uses, in minutes.</summary>
    private const int MaxUtcOffsetMinutes = 16 * 60;

    /// <inheritdoc />
    public async Task<PlaybackStatisticsResponse> GetAsync(
        PlaybackStatisticsQuery query,
        CancellationToken cancellationToken) {
        var offset = TimeSpan.FromMinutes(Math.Clamp(query.UtcOffsetMinutes, -MaxUtcOffsetMinutes, MaxUtcOffsetMinutes));
        var rows = QueryRows(query);
        var counts = await rows
            .GroupBy(_ => 1)
            .Select(group => new PlaybackStatisticsCounts(
                group.Count(),
                group.Count(row => row.Kind == PlaybackEventKind.Completed),
                group.Count(row => row.Kind == PlaybackEventKind.Skipped),
                group.Select(row => row.EntityId).Distinct().Count()))
            .SingleOrDefaultAsync(cancellationToken) ?? PlaybackStatisticsCounts.Empty;

        var topEntityRows = await rows
            .GroupBy(row => new { row.EntityId, row.EntityKindCode, row.EntityTitle })
            .Select(group => new {
                group.Key.EntityId,
                group.Key.EntityKindCode,
                group.Key.EntityTitle,
                CompletedCount = group.Count(row => row.Kind == PlaybackEventKind.Completed),
                SkippedCount = group.Count(row => row.Kind == PlaybackEventKind.Skipped),
                FirstEventAt = group.Min(row => row.OccurredAt),
                LastEventAt = group.Max(row => row.OccurredAt)
            })
            .OrderByDescending(entity => entity.CompletedCount)
            .ThenByDescending(entity => entity.SkippedCount)
            .ThenByDescending(entity => entity.LastEventAt)
            .Take(TopEntityLimit)
            .ToArrayAsync(cancellationToken);

        var recentEventRows = await rows
            .Take(RecentEventLimit)
            .ToArrayAsync(cancellationToken);

        var coverByEntity = await LoadCoverPathsAsync(
            topEntityRows.Select(row => row.EntityId)
                .Concat(recentEventRows.Select(row => row.EntityId))
                .Distinct()
                .ToArray(),
            cancellationToken);

        // Day, weekday/hour, watch-time, and family projections all fold the same event set, so
        // the window is materialized once with only the columns those folds need.
        var foldRows = await rows
            .Select(row => new PlaybackStatisticsFoldRow(
                row.EntityId,
                row.EntityKindCode,
                row.Kind,
                row.OccurredAt,
                row.PositionSeconds,
                row.DurationSeconds))
            .ToArrayAsync(cancellationToken);

        var watchSecondsByEntity = foldRows
            .GroupBy(row => row.EntityId)
            .ToDictionary(group => group.Key, group => group.Sum(WatchSeconds));

        var topEntities = topEntityRows
            .Select(row => new PlaybackStatisticsEntity(
                row.EntityId,
                row.EntityKindCode.DecodeAs<EntityKind>(),
                row.EntityTitle,
                coverByEntity.GetValueOrDefault(row.EntityId),
                row.CompletedCount,
                row.SkippedCount,
                watchSecondsByEntity.GetValueOrDefault(row.EntityId),
                row.FirstEventAt,
                row.LastEventAt))
            .ToArray();

        var recentEvents = recentEventRows
            .Select(row => new PlaybackStatisticsEvent(
                row.EventId,
                row.EntityId,
                row.EntityKindCode.DecodeAs<EntityKind>(),
                row.EntityTitle,
                coverByEntity.GetValueOrDefault(row.EntityId),
                row.Kind,
                row.OccurredAt,
                row.PositionSeconds,
                row.DurationSeconds))
            .ToArray();

        var dailyEvents = foldRows
            .GroupBy(row => DateOnly.FromDateTime(row.OccurredAt.ToOffset(offset).Date))
            .Select(group => new PlaybackStatisticsBucket(
                group.Key,
                group.Count(row => row.Kind == PlaybackEventKind.Completed),
                group.Count(row => row.Kind == PlaybackEventKind.Skipped),
                group.Sum(WatchSeconds)))
            .OrderBy(bucket => bucket.Date)
            .ToArray();

        var rhythm = foldRows
            .GroupBy(row => {
                var local = row.OccurredAt.ToOffset(offset);
                return new { DayOfWeek = (int)local.DayOfWeek, local.Hour };
            })
            .Select(group => new PlaybackStatisticsRhythmCell(
                group.Key.DayOfWeek,
                group.Key.Hour,
                group.Count(row => row.Kind == PlaybackEventKind.Completed),
                group.Count(row => row.Kind == PlaybackEventKind.Skipped),
                group.Sum(WatchSeconds)))
            .OrderBy(cell => cell.DayOfWeek)
            .ThenBy(cell => cell.Hour)
            .ToArray();

        var kindBreakdown = foldRows
            .GroupBy(row => row.EntityKindCode)
            .Select(group => new PlaybackStatisticsKindSlice(
                group.Key.DecodeAs<EntityKind>(),
                group.Count(),
                group.Count(row => row.Kind == PlaybackEventKind.Completed),
                group.Count(row => row.Kind == PlaybackEventKind.Skipped),
                group.Select(row => row.EntityId).Distinct().Count(),
                group.Sum(WatchSeconds)))
            .OrderByDescending(slice => slice.TotalEvents)
            .ThenByDescending(slice => slice.WatchSeconds)
            .ThenBy(slice => slice.Kind)
            .ToArray();

        return new PlaybackStatisticsResponse(
            query.From,
            query.To,
            counts.TotalEvents,
            counts.CompletedCount,
            counts.SkippedCount,
            counts.DistinctEntityCount,
            foldRows.Sum(WatchSeconds),
            topEntities,
            recentEvents,
            dailyEvents,
            kindBreakdown,
            rhythm);
    }

    /// <summary>
    /// Observed playback seconds for a single event. The reported position is the furthest point
    /// playback reached, clamped to the reported duration so a stale or overshot position cannot
    /// inflate totals.
    /// </summary>
    private static double WatchSeconds(PlaybackStatisticsFoldRow row) {
        var position = row.PositionSeconds ?? 0d;
        if (double.IsNaN(position) || position <= 0d) {
            return 0d;
        }

        return row.DurationSeconds is { } duration && duration > 0d
            ? Math.Min(position, duration)
            : position;
    }

    private IQueryable<PlaybackStatisticsRow> QueryRows(PlaybackStatisticsQuery query) {
        var events = db.EntityPlaybackEvents.AsNoTracking()
            .Where(evt => evt.OccurredAt >= query.From && evt.OccurredAt < query.To);
        if (!query.AllUsers) {
            var userId = query.UserId ?? currentUser.UserId;
            events = events.Where(evt => evt.UserId == userId);
        }

        if (query.EventKind is { } eventKind) {
            events = events.Where(evt => evt.Kind == eventKind);
        }

        var allEntities = db.Entities.AsNoTracking();
        var catalogEntities = allEntities.ExcludeBookOwnedAudioTracks(allEntities);
        var rows =
            from evt in events
            join entity in catalogEntities on evt.EntityId equals entity.Id
            where !query.HideNsfw || !entity.IsNsfw
            select new {
                EventId = evt.Id,
                evt.EntityId,
                EntityKindCode = entity.KindCode,
                EntityTitle = entity.Title,
                evt.Kind,
                evt.OccurredAt,
                evt.PositionSeconds,
                evt.DurationSeconds
            };

        if (query.Kind is { } kind) {
            var kindCode = kind.ToCode();
            rows = rows.Where(row => row.EntityKindCode == kindCode);
        }

        return rows
            .OrderByDescending(row => row.OccurredAt)
            .ThenByDescending(row => row.EventId)
            .Select(row => new PlaybackStatisticsRow {
                EventId = row.EventId,
                EntityId = row.EntityId,
                EntityKindCode = row.EntityKindCode,
                EntityTitle = row.EntityTitle,
                Kind = row.Kind,
                OccurredAt = row.OccurredAt,
                PositionSeconds = row.PositionSeconds,
                DurationSeconds = row.DurationSeconds
            });
    }

    private async Task<IReadOnlyDictionary<Guid, string>> LoadCoverPathsAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken) {
        if (entityIds.Count == 0) {
            return new Dictionary<Guid, string>();
        }

        var files = await db.EntityFiles.AsNoTracking()
            .Where(file => entityIds.Contains(file.EntityId))
            .Where(file => EntityCoverSelection.CoverRoles.Contains(file.Role))
            .ToArrayAsync(cancellationToken);

        return files
            .GroupBy(file => file.EntityId)
            .Select(group => new { EntityId = group.Key, File = EntityCoverSelection.Select(group) })
            .Where(item => item.File is not null)
            .ToDictionary(item => item.EntityId, item => item.File!.Path);
    }

    private sealed class PlaybackStatisticsRow {
        public Guid EventId { get; init; }
        public Guid EntityId { get; init; }
        public string EntityKindCode { get; init; } = string.Empty;
        public string EntityTitle { get; init; } = string.Empty;
        public PlaybackEventKind Kind { get; init; }
        public DateTimeOffset OccurredAt { get; init; }
        public double? PositionSeconds { get; init; }
        public double? DurationSeconds { get; init; }
    }

    private sealed record PlaybackStatisticsCounts(
        int TotalEvents,
        int CompletedCount,
        int SkippedCount,
        int DistinctEntityCount) {
        public static PlaybackStatisticsCounts Empty { get; } = new(0, 0, 0, 0);
    }

    private sealed record PlaybackStatisticsFoldRow(
        Guid EntityId,
        string EntityKindCode,
        PlaybackEventKind Kind,
        DateTimeOffset OccurredAt,
        double? PositionSeconds,
        double? DurationSeconds);
}
