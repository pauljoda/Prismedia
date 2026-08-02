using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Playback;

/// <summary>
/// Time-bounded consumption statistics built from discrete events and daily duration buckets.
/// </summary>
/// <param name="From">Inclusive lower bound used for the statistics window.</param>
/// <param name="To">Exclusive upper bound used for the statistics window.</param>
/// <param name="TotalEvents">Total event count in the window.</param>
/// <param name="AccessedCount">Open/start event count in the window.</param>
/// <param name="CompletedCount">Completed consumption count in the window.</param>
/// <param name="SkippedCount">Skip count in the window.</param>
/// <param name="DistinctEntityCount">Number of unique entities with events in the window.</param>
/// <param name="ActiveSeconds">Total active consumption time from daily buckets.</param>
/// <param name="ViewingSeconds">Active video viewing time.</param>
/// <param name="ReadingSeconds">Active reading time reported by book-reader heartbeats.</param>
/// <param name="ListeningSeconds">Active audiobook time reported by player heartbeats.</param>
/// <param name="TopEntities">Most active entities in the window.</param>
/// <param name="RecentEvents">Most recent consumption events in the window.</param>
/// <param name="DailyEvents">Daily event buckets for timeline charts.</param>
/// <param name="KindBreakdown">
/// Per-entity-family shares of the window, ordered from most to least active. Drives the
/// spectrum dispersion view that maps one library to its media families.
/// </param>
/// <param name="Rhythm">
/// Sparse weekday/hour cells describing when consumption events happen. Only cells with at least one
/// event are present.
/// </param>
public sealed record PlaybackStatisticsResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    int TotalEvents,
    int AccessedCount,
    int CompletedCount,
    int SkippedCount,
    int DistinctEntityCount,
    double ActiveSeconds,
    double ViewingSeconds,
    double ReadingSeconds,
    double ListeningSeconds,
    IReadOnlyList<PlaybackStatisticsEntity> TopEntities,
    IReadOnlyList<PlaybackStatisticsEvent> RecentEvents,
    IReadOnlyList<PlaybackStatisticsBucket> DailyEvents,
    IReadOnlyList<PlaybackStatisticsKindSlice> KindBreakdown,
    IReadOnlyList<PlaybackStatisticsRhythmCell> Rhythm);

/// <summary>Consumption statistics for one Entity on the stable playback statistics route.</summary>
/// <param name="Id">Entity identifier.</param>
/// <param name="Kind">Entity family the row belongs to.</param>
/// <param name="Title">Entity title at projection time.</param>
/// <param name="CoverUrl">Selected cover asset path, when the entity has one.</param>
/// <param name="CompletedCount">Completed consumption count for the entity.</param>
/// <param name="AccessedCount">Open/start count for the entity.</param>
/// <param name="SkippedCount">Skip count for the entity.</param>
/// <param name="ActiveSeconds">Active consumption time accumulated by the entity.</param>
/// <param name="FirstEventAt">Oldest event timestamp for the entity inside the window.</param>
/// <param name="LastEventAt">Newest event timestamp for the entity inside the window.</param>
public sealed record PlaybackStatisticsEntity(
    Guid Id,
    EntityKind Kind,
    string Title,
    string? CoverUrl,
    int AccessedCount,
    int CompletedCount,
    int SkippedCount,
    double ActiveSeconds,
    DateTimeOffset FirstEventAt,
    DateTimeOffset LastEventAt);

/// <summary>Recent consumption-history event summary.</summary>
public sealed record PlaybackStatisticsEvent(
    Guid Id,
    Guid EntityId,
    EntityKind EntityKind,
    string EntityTitle,
    string? CoverUrl,
    ConsumptionEventKind Kind,
    DateTimeOffset OccurredAt,
    double? PositionSeconds,
    double? DurationSeconds);

/// <summary>Daily consumption event and active-time bucket.</summary>
/// <param name="Date">Local calendar day the events were bucketed into.</param>
/// <param name="AccessedCount">Open/start count for the day.</param>
/// <param name="CompletedCount">Completed consumption count for the day.</param>
/// <param name="SkippedCount">Skip count for the day.</param>
/// <param name="ActiveSeconds">Active consumption time for the day.</param>
/// <param name="ViewingSeconds">Active viewing time for the day.</param>
/// <param name="ListeningSeconds">Active listening time for the day.</param>
/// <param name="ReadingSeconds">Active reading time for the day.</param>
public sealed record PlaybackStatisticsBucket(
    DateOnly Date,
    int AccessedCount,
    int CompletedCount,
    int SkippedCount,
    double ActiveSeconds,
    double ViewingSeconds,
    double ListeningSeconds,
    double ReadingSeconds);

/// <summary>
/// One Entity family's share of a consumption window.
/// </summary>
/// <param name="Kind">Entity family the slice describes.</param>
/// <param name="TotalEvents">Combined accessed, completed, and skipped count for the family.</param>
/// <param name="CompletedCount">Completed consumption count for the family.</param>
/// <param name="SkippedCount">Skip count for the family.</param>
/// <param name="DistinctEntityCount">Unique entities of this family with events in the window.</param>
/// <param name="AccessedCount">Open/start count for the family.</param>
/// <param name="ActiveSeconds">Active consumption time accumulated by the family.</param>
public sealed record PlaybackStatisticsKindSlice(
    EntityKind Kind,
    int TotalEvents,
    int AccessedCount,
    int CompletedCount,
    int SkippedCount,
    int DistinctEntityCount,
    double ActiveSeconds);

/// <summary>
/// Consumption-event density for one local weekday and hour.
/// </summary>
/// <param name="DayOfWeek">Local day of week, 0 for Sunday through 6 for Saturday.</param>
/// <param name="Hour">Local hour of day, 0 through 23.</param>
/// <param name="AccessedCount">Open/start count in the cell.</param>
/// <param name="CompletedCount">Completed consumption count in the cell.</param>
/// <param name="SkippedCount">Skip count in the cell.</param>
public sealed record PlaybackStatisticsRhythmCell(
    int DayOfWeek,
    int Hour,
    int AccessedCount,
    int CompletedCount,
    int SkippedCount);
