using Prismedia.Domain.Entities;

namespace Prismedia.Application.Playback;

/// <summary>
/// Application port for appending durable playback-history events.
/// </summary>
public interface IPlaybackEventStore {
    /// <summary>
    /// Stages one playback-history event in the current unit of work without committing it.
    /// </summary>
    /// <param name="entry">Playback event to persist.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task StageAsync(PlaybackEventAppend entry, CancellationToken cancellationToken);

    /// <summary>
    /// Appends and commits one playback-history event immediately.
    /// </summary>
    /// <param name="entry">Playback event to persist.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task AppendAsync(PlaybackEventAppend entry, CancellationToken cancellationToken);
}

/// <summary>
/// Durable playback-history event produced by playback use cases.
/// </summary>
/// <param name="EntityId">Entity the event belongs to.</param>
/// <param name="Kind">Event kind.</param>
/// <param name="OccurredAt">Domain timestamp for when playback happened.</param>
/// <param name="PositionSeconds">Optional playback position associated with the event.</param>
/// <param name="DurationSeconds">Optional entity duration associated with the event.</param>
public sealed record PlaybackEventAppend(
    Guid EntityId,
    PlaybackEventKind Kind,
    DateTimeOffset OccurredAt,
    double? PositionSeconds,
    double? DurationSeconds);

internal sealed class NullPlaybackEventStore : IPlaybackEventStore {
    public static NullPlaybackEventStore Instance { get; } = new();

    private NullPlaybackEventStore() {
    }

    public Task StageAsync(PlaybackEventAppend entry, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AppendAsync(PlaybackEventAppend entry, CancellationToken cancellationToken) => Task.CompletedTask;
}
