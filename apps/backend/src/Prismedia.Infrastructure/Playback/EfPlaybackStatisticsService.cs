using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Playback;
using Prismedia.Application.Security;
using Prismedia.Contracts.Playback;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Playback;

/// <summary>
/// EF Core read projection over discrete consumption events and bounded daily-duration buckets.
/// Cached state and daily rows avoid summing an ever-growing heartbeat history.
/// </summary>
public sealed class EfPlaybackStatisticsService(
    PrismediaDbContext db,
    ICurrentUserContext currentUser,
    EfEntityLibraryVisibilityFilter? libraryVisibility = null) : IPlaybackStatisticsService {
    private const int TopEntityLimit = 12;
    private const int RecentEventLimit = 30;
    private const int MaxUtcOffsetMinutes = 16 * 60;
    private readonly EfEntityLibraryVisibilityFilter _libraryVisibility =
        libraryVisibility ?? new EfEntityLibraryVisibilityFilter(db, currentUser);

    /// <inheritdoc />
    public async Task<PlaybackStatisticsResponse> GetAsync(
        PlaybackStatisticsQuery query,
        CancellationToken cancellationToken) {
        var offset = TimeSpan.FromMinutes(Math.Clamp(
            query.UtcOffsetMinutes,
            -MaxUtcOffsetMinutes,
            MaxUtcOffsetMinutes));
        var enforceLibraryVisibility = await _libraryVisibility.RequiresCurrentUserVisibilityAsync(cancellationToken);
        var eventRows = await QueryEvents(query, enforceLibraryVisibility).ToArrayAsync(cancellationToken);
        var dayRows = query.EventKind is null
            ? await QueryDays(query, enforceLibraryVisibility, offset).ToArrayAsync(cancellationToken)
            : [];

        var eventEntities = eventRows.Select(row => new EntityKey(row.EntityId, row.EntityKindCode, row.EntityTitle));
        var dayEntities = dayRows.Select(row => new EntityKey(row.EntityId, row.EntityKindCode, row.EntityTitle));
        var entityKeys = eventEntities.Concat(dayEntities).Distinct().ToArray();
        var topRows = entityKeys
            .Select(key => {
                var events = eventRows.Where(row => row.EntityId == key.EntityId).ToArray();
                var days = dayRows.Where(row => row.EntityId == key.EntityId).ToArray();
                var first = EventBoundary(events, days, latest: false);
                var last = EventBoundary(events, days, latest: true);
                return new PlaybackStatisticsEntityFold(
                    key.EntityId,
                    key.EntityKindCode,
                    key.EntityTitle,
                    events.Count(row => row.Kind == ConsumptionEventKind.Accessed),
                    events.Count(row => row.Kind == ConsumptionEventKind.Completed),
                    events.Count(row => row.Kind == ConsumptionEventKind.Skipped),
                    days.Sum(row => row.DurationSeconds),
                    first,
                    last);
            })
            .OrderByDescending(row => row.ActiveSeconds)
            .ThenByDescending(row => row.AccessedCount)
            .ThenByDescending(row => row.CompletedCount)
            .ThenByDescending(row => row.LastEventAt)
            .Take(TopEntityLimit)
            .ToArray();
        var recentEventRows = eventRows.Take(RecentEventLimit).ToArray();
        var coverByEntity = await LoadCoverPathsAsync(
            topRows.Select(row => row.EntityId)
                .Concat(recentEventRows.Select(row => row.EntityId))
                .Distinct()
                .ToArray(),
            cancellationToken);

        var topEntities = topRows.Select(row => new PlaybackStatisticsEntity(
            row.EntityId,
            row.EntityKindCode.DecodeAs<EntityKind>(),
            row.EntityTitle,
            coverByEntity.GetValueOrDefault(row.EntityId),
            row.AccessedCount,
            row.CompletedCount,
            row.SkippedCount,
            row.ActiveSeconds,
            row.FirstEventAt,
            row.LastEventAt)).ToArray();
        var recentEvents = recentEventRows.Select(row => new PlaybackStatisticsEvent(
            row.EventId,
            row.EntityId,
            row.EntityKindCode.DecodeAs<EntityKind>(),
            row.EntityTitle,
            coverByEntity.GetValueOrDefault(row.EntityId),
            row.Kind,
            row.OccurredAt,
            row.PositionSeconds,
            row.DurationSeconds)).ToArray();

        var eventDays = eventRows.GroupBy(row => DateOnly.FromDateTime(row.OccurredAt.ToOffset(offset).Date))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var activityDays = dayRows.GroupBy(row => row.ActivityDate)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var daily = eventDays.Keys.Concat(activityDays.Keys).Distinct().Order().Select(date => {
            var events = eventDays.GetValueOrDefault(date) ?? [];
            var activities = activityDays.GetValueOrDefault(date) ?? [];
            return new PlaybackStatisticsBucket(
                date,
                events.Count(row => row.Kind == ConsumptionEventKind.Accessed),
                events.Count(row => row.Kind == ConsumptionEventKind.Completed),
                events.Count(row => row.Kind == ConsumptionEventKind.Skipped),
                activities.Sum(row => row.DurationSeconds),
                activities.Where(row => row.Kind == ConsumptionActivityKind.Viewing).Sum(row => row.DurationSeconds),
                activities.Where(row => row.Kind == ConsumptionActivityKind.Listening).Sum(row => row.DurationSeconds),
                activities.Where(row => row.Kind == ConsumptionActivityKind.Reading).Sum(row => row.DurationSeconds));
        }).ToArray();

        var kindBreakdown = entityKeys.GroupBy(key => key.EntityKindCode).Select(group => {
            var ids = group.Select(key => key.EntityId).ToHashSet();
            var events = eventRows.Where(row => ids.Contains(row.EntityId)).ToArray();
            var days = dayRows.Where(row => ids.Contains(row.EntityId)).ToArray();
            return new PlaybackStatisticsKindSlice(
                group.Key.DecodeAs<EntityKind>(),
                events.Length,
                events.Count(row => row.Kind == ConsumptionEventKind.Accessed),
                events.Count(row => row.Kind == ConsumptionEventKind.Completed),
                events.Count(row => row.Kind == ConsumptionEventKind.Skipped),
                ids.Count,
                days.Sum(row => row.DurationSeconds));
        }).OrderByDescending(slice => slice.ActiveSeconds)
          .ThenByDescending(slice => slice.TotalEvents)
          .ThenBy(slice => slice.Kind)
          .ToArray();
        var rhythm = eventRows.GroupBy(row => {
            var local = row.OccurredAt.ToOffset(offset);
            return new { DayOfWeek = (int)local.DayOfWeek, local.Hour };
        }).Select(group => new PlaybackStatisticsRhythmCell(
            group.Key.DayOfWeek,
            group.Key.Hour,
            group.Count(row => row.Kind == ConsumptionEventKind.Accessed),
            group.Count(row => row.Kind == ConsumptionEventKind.Completed),
            group.Count(row => row.Kind == ConsumptionEventKind.Skipped)))
          .OrderBy(cell => cell.DayOfWeek)
          .ThenBy(cell => cell.Hour)
          .ToArray();

        return new PlaybackStatisticsResponse(
            query.From,
            query.To,
            eventRows.Length,
            eventRows.Count(row => row.Kind == ConsumptionEventKind.Accessed),
            eventRows.Count(row => row.Kind == ConsumptionEventKind.Completed),
            eventRows.Count(row => row.Kind == ConsumptionEventKind.Skipped),
            entityKeys.Length,
            dayRows.Sum(row => row.DurationSeconds),
            dayRows.Where(row => row.Kind == ConsumptionActivityKind.Viewing).Sum(row => row.DurationSeconds),
            dayRows.Where(row => row.Kind == ConsumptionActivityKind.Reading).Sum(row => row.DurationSeconds),
            dayRows.Where(row => row.Kind == ConsumptionActivityKind.Listening).Sum(row => row.DurationSeconds),
            topEntities,
            recentEvents,
            daily,
            kindBreakdown,
            rhythm);
    }

    private IQueryable<ConsumptionStatisticsEventRow> QueryEvents(
        PlaybackStatisticsQuery query,
        bool enforceLibraryVisibility) {
        var events = db.EntityConsumptionEvents.AsNoTracking()
            .Where(evt => evt.OccurredAt >= query.From && evt.OccurredAt < query.To);
        if (!query.AllUsers) {
            var userId = query.UserId ?? currentUser.UserId;
            events = events.Where(evt => evt.UserId == userId);
        }
        if (query.EventKind is { } eventKind) {
            events = events.Where(evt => evt.Kind == eventKind);
        }

        var entities = VisibleEntities(query.HideNsfw, enforceLibraryVisibility);
        var rows = from evt in events
                   join entity in entities on evt.EntityId equals entity.Id
                   select new ConsumptionStatisticsEventRow {
                       EventId = evt.Id,
                       EntityId = evt.EntityId,
                       EntityKindCode = entity.KindCode,
                       EntityTitle = entity.Title,
                       Kind = evt.Kind,
                       OccurredAt = evt.OccurredAt,
                       PositionSeconds = evt.PositionSeconds,
                       DurationSeconds = evt.DurationSeconds
                   };
        if (query.Kind is { } kind) {
            var code = kind.ToCode();
            rows = rows.Where(row => row.EntityKindCode == code);
        }
        return rows.OrderByDescending(row => row.OccurredAt).ThenByDescending(row => row.EventId);
    }

    private IQueryable<ConsumptionStatisticsDayRow> QueryDays(
        PlaybackStatisticsQuery query,
        bool enforceLibraryVisibility,
        TimeSpan offset) {
        var firstDate = DateOnly.FromDateTime(query.From.ToOffset(offset).Date);
        var lastInstant = query.To > DateTimeOffset.MinValue ? query.To.AddTicks(-1) : query.To;
        var lastDate = DateOnly.FromDateTime(lastInstant.ToOffset(offset).Date);
        var days = db.EntityConsumptionDays.AsNoTracking()
            .Where(day => day.ActivityDate >= firstDate && day.ActivityDate <= lastDate);
        if (!query.AllUsers) {
            var userId = query.UserId ?? currentUser.UserId;
            days = days.Where(day => day.UserId == userId);
        }

        var entities = VisibleEntities(query.HideNsfw, enforceLibraryVisibility);
        var rows = from day in days
                   join entity in entities on day.EntityId equals entity.Id
                   select new ConsumptionStatisticsDayRow {
                       EntityId = day.EntityId,
                       EntityKindCode = entity.KindCode,
                       EntityTitle = entity.Title,
                       Kind = day.Kind,
                       ActivityDate = day.ActivityDate,
                       DurationSeconds = day.DurationSeconds
                   };
        if (query.Kind is { } kind) {
            var code = kind.ToCode();
            rows = rows.Where(row => row.EntityKindCode == code);
        }
        return rows;
    }

    private IQueryable<Persistence.Entities.EntityRow> VisibleEntities(bool hideNsfw, bool enforceLibraryVisibility) {
        var all = db.Entities.AsNoTracking();
        var entities = EntityCatalogQueryPolicy.Apply(all, all, EntityCatalogSurface.Statistics);
        if (enforceLibraryVisibility) {
            entities = _libraryVisibility.ApplyCurrentUserVisibility(entities);
        }
        return hideNsfw ? entities.Where(entity => !entity.IsNsfw) : entities;
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
        return files.GroupBy(file => file.EntityId)
            .Select(group => new { EntityId = group.Key, File = EntityCoverSelection.Select(group) })
            .Where(item => item.File is not null)
            .ToDictionary(item => item.EntityId, item => item.File!.Path);
    }

    private static DateTimeOffset EventBoundary(
        IReadOnlyCollection<ConsumptionStatisticsEventRow> events,
        IReadOnlyCollection<ConsumptionStatisticsDayRow> days,
        bool latest) {
        var timestamps = events.Count > 0
            ? events.Select(row => row.OccurredAt).ToArray()
            : days.Select(row => new DateTimeOffset(row.ActivityDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)).ToArray();
        return latest ? timestamps.Max() : timestamps.Min();
    }

    private sealed class ConsumptionStatisticsEventRow {
        public Guid EventId { get; init; }
        public Guid EntityId { get; init; }
        public string EntityKindCode { get; init; } = string.Empty;
        public string EntityTitle { get; init; } = string.Empty;
        public ConsumptionEventKind Kind { get; init; }
        public DateTimeOffset OccurredAt { get; init; }
        public double? PositionSeconds { get; init; }
        public double? DurationSeconds { get; init; }
    }

    private sealed class ConsumptionStatisticsDayRow {
        public Guid EntityId { get; init; }
        public string EntityKindCode { get; init; } = string.Empty;
        public string EntityTitle { get; init; } = string.Empty;
        public ConsumptionActivityKind Kind { get; init; }
        public DateOnly ActivityDate { get; init; }
        public double DurationSeconds { get; init; }
    }

    private sealed record EntityKey(Guid EntityId, string EntityKindCode, string EntityTitle);

    private sealed record PlaybackStatisticsEntityFold(
        Guid EntityId,
        string EntityKindCode,
        string EntityTitle,
        int AccessedCount,
        int CompletedCount,
        int SkippedCount,
        double ActiveSeconds,
        DateTimeOffset FirstEventAt,
        DateTimeOffset LastEventAt);
}
