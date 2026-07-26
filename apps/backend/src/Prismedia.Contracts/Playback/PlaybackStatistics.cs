using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Playback;

/// <summary>
/// Time-bounded playback statistics built from durable playback-history events.
/// </summary>
/// <param name="From">Inclusive lower bound used for the statistics window.</param>
/// <param name="To">Exclusive upper bound used for the statistics window.</param>
/// <param name="TotalEvents">Total event count in the window.</param>
/// <param name="CompletedCount">Completed playback count in the window.</param>
/// <param name="SkippedCount">Skip count in the window.</param>
/// <param name="DistinctEntityCount">Number of unique entities with events in the window.</param>
/// <param name="WatchSeconds">
/// Total observed playback seconds in the window, summed from each event's reported position and
/// capped by the reported duration. Events without a position contribute nothing.
/// </param>
/// <param name="TopEntities">Most active entities in the window.</param>
/// <param name="RecentEvents">Most recent playback events in the window.</param>
/// <param name="DailyEvents">Daily event buckets for timeline charts.</param>
/// <param name="KindBreakdown">
/// Per-entity-family shares of the window, ordered from most to least active. Drives the
/// spectrum dispersion view that maps one library to its media families.
/// </param>
/// <param name="Rhythm">
/// Sparse weekday/hour cells describing when playback happens. Only cells with at least one
/// event are present.
/// </param>
public sealed record PlaybackStatisticsResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    int TotalEvents,
    int CompletedCount,
    int SkippedCount,
    int DistinctEntityCount,
    double WatchSeconds,
    IReadOnlyList<PlaybackStatisticsEntity> TopEntities,
    IReadOnlyList<PlaybackStatisticsEvent> RecentEvents,
    IReadOnlyList<PlaybackStatisticsBucket> DailyEvents,
    IReadOnlyList<PlaybackStatisticsKindSlice> KindBreakdown,
    IReadOnlyList<PlaybackStatisticsRhythmCell> Rhythm);

/// <summary>Playback statistics for one entity.</summary>
/// <param name="Id">Entity identifier.</param>
/// <param name="Kind">Entity family the row belongs to.</param>
/// <param name="Title">Entity title at projection time.</param>
/// <param name="CoverUrl">Selected cover asset path, when the entity has one.</param>
/// <param name="CompletedCount">Completed playback count for the entity.</param>
/// <param name="SkippedCount">Skip count for the entity.</param>
/// <param name="WatchSeconds">Observed playback seconds accumulated by the entity.</param>
/// <param name="FirstEventAt">Oldest event timestamp for the entity inside the window.</param>
/// <param name="LastEventAt">Newest event timestamp for the entity inside the window.</param>
public sealed record PlaybackStatisticsEntity(
    Guid Id,
    EntityKind Kind,
    string Title,
    string? CoverUrl,
    int CompletedCount,
    int SkippedCount,
    double WatchSeconds,
    DateTimeOffset FirstEventAt,
    DateTimeOffset LastEventAt);

/// <summary>Recent playback-history event summary.</summary>
public sealed record PlaybackStatisticsEvent(
    Guid Id,
    Guid EntityId,
    EntityKind EntityKind,
    string EntityTitle,
    string? CoverUrl,
    PlaybackEventKind Kind,
    DateTimeOffset OccurredAt,
    double? PositionSeconds,
    double? DurationSeconds);

/// <summary>Daily playback event bucket.</summary>
/// <param name="Date">Local calendar day the events were bucketed into.</param>
/// <param name="CompletedCount">Completed playback count for the day.</param>
/// <param name="SkippedCount">Skip count for the day.</param>
/// <param name="WatchSeconds">Observed playback seconds for the day.</param>
public sealed record PlaybackStatisticsBucket(
    DateOnly Date,
    int CompletedCount,
    int SkippedCount,
    double WatchSeconds);

/// <summary>
/// One entity family's share of a playback window.
/// </summary>
/// <param name="Kind">Entity family the slice describes.</param>
/// <param name="TotalEvents">Combined completed and skipped count for the family.</param>
/// <param name="CompletedCount">Completed playback count for the family.</param>
/// <param name="SkippedCount">Skip count for the family.</param>
/// <param name="DistinctEntityCount">Unique entities of this family with events in the window.</param>
/// <param name="WatchSeconds">Observed playback seconds accumulated by the family.</param>
public sealed record PlaybackStatisticsKindSlice(
    EntityKind Kind,
    int TotalEvents,
    int CompletedCount,
    int SkippedCount,
    int DistinctEntityCount,
    double WatchSeconds);

/// <summary>
/// Playback density for one local weekday and hour, used to render viewing rhythm.
/// </summary>
/// <param name="DayOfWeek">Local day of week, 0 for Sunday through 6 for Saturday.</param>
/// <param name="Hour">Local hour of day, 0 through 23.</param>
/// <param name="CompletedCount">Completed playback count in the cell.</param>
/// <param name="SkippedCount">Skip count in the cell.</param>
/// <param name="WatchSeconds">Observed playback seconds in the cell.</param>
public sealed record PlaybackStatisticsRhythmCell(
    int DayOfWeek,
    int Hour,
    int CompletedCount,
    int SkippedCount,
    double WatchSeconds);
