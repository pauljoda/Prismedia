using Prismedia.Contracts.Playback;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Playback;

/// <summary>
/// Query service for timeframe-aware consumption statistics.
/// </summary>
public interface IPlaybackStatisticsService {
    /// <summary>
    /// Returns consumption statistics for the requested filter window.
    /// </summary>
    Task<PlaybackStatisticsResponse> GetAsync(PlaybackStatisticsQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Consumption statistics filter carried by the stable playback statistics route.
/// </summary>
/// <param name="From">Inclusive lower time bound.</param>
/// <param name="To">Exclusive upper time bound.</param>
/// <param name="Kind">Optional entity kind filter.</param>
/// <param name="EventKind">Optional consumption event kind filter.</param>
/// <param name="HideNsfw">Whether NSFW entities should be hidden.</param>
/// <param name="UserId">The user whose events should be projected when <paramref name="AllUsers"/> is false.</param>
/// <param name="AllUsers">
/// When true (admins only, enforced at the endpoint), includes every user's events
/// and null-stamped legacy household events instead of only the caller's own events.
/// </param>
/// <param name="UtcOffsetMinutes">
/// Minutes to add to UTC before bucketing events into calendar days, weekdays, and hours, so
/// day and rhythm projections match the caller's wall clock instead of UTC.
/// </param>
public sealed record PlaybackStatisticsQuery(
    DateTimeOffset From,
    DateTimeOffset To,
    EntityKind? Kind,
    ConsumptionEventKind? EventKind,
    bool HideNsfw,
    Guid? UserId = null,
    bool AllUsers = false,
    int UtcOffsetMinutes = 0);
