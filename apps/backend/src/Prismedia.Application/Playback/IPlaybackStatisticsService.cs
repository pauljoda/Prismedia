using Prismedia.Contracts.Playback;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Playback;

/// <summary>
/// Query service for timeframe-aware playback statistics.
/// </summary>
public interface IPlaybackStatisticsService {
    /// <summary>
    /// Returns playback statistics for the requested filter window.
    /// </summary>
    Task<PlaybackStatisticsResponse> GetAsync(PlaybackStatisticsQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Playback statistics filter.
/// </summary>
/// <param name="From">Inclusive lower time bound.</param>
/// <param name="To">Exclusive upper time bound.</param>
/// <param name="Kind">Optional entity kind filter.</param>
/// <param name="EventKind">Optional playback event kind filter.</param>
/// <param name="HideNsfw">Whether NSFW entities should be hidden.</param>
/// <param name="UserId">The user whose events should be projected when <paramref name="AllUsers"/> is false.</param>
/// <param name="AllUsers">
/// When true (admins only, enforced at the endpoint), includes every user's events
/// and null-stamped legacy household events instead of only the caller's own events.
/// </param>
public sealed record PlaybackStatisticsQuery(
    DateTimeOffset From,
    DateTimeOffset To,
    EntityKind? Kind,
    PlaybackEventKind? EventKind,
    bool HideNsfw,
    Guid? UserId = null,
    bool AllUsers = false);
