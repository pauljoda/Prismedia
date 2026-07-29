using Prismedia.Domain.Entities;

namespace Prismedia.Application.Playback;

/// <summary>
/// Application port for staging time-bounded entity activity in the current unit of work.
/// </summary>
public interface IEntityActivityStore {
    /// <summary>Stages one activity interval without committing the surrounding unit of work.</summary>
    /// <param name="entry">Activity interval to persist.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task StageAsync(EntityActivityAppend entry, CancellationToken cancellationToken);
}

/// <summary>
/// One bounded interval of active reading or listening reported by a book client heartbeat.
/// </summary>
/// <param name="EntityId">Book receiving the activity.</param>
/// <param name="Kind">Whether the interval was spent reading or listening.</param>
/// <param name="OccurredAt">Server timestamp for the end of the interval.</param>
/// <param name="DurationSeconds">Observed active duration.</param>
public sealed record EntityActivityAppend(
    Guid EntityId,
    BookActivityKind Kind,
    DateTimeOffset OccurredAt,
    double DurationSeconds);

internal sealed class NullEntityActivityStore : IEntityActivityStore {
    public static NullEntityActivityStore Instance { get; } = new();

    private NullEntityActivityStore() {
    }

    public Task StageAsync(EntityActivityAppend entry, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
