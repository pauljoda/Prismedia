using Prismedia.Domain.Entities;

namespace Prismedia.Application.Playback;

/// <summary>Application port for staging durable discrete consumption events.</summary>
public interface IConsumptionEventStore {
    /// <summary>Returns whether this user's session already emitted the event kind.</summary>
    Task<bool> ContainsSessionEventAsync(
        string sessionId,
        ConsumptionEventKind kind,
        CancellationToken cancellationToken) => Task.FromResult(false);

    /// <summary>Stages one event in the current unit of work without committing it.</summary>
    Task StageAsync(ConsumptionEventAppend entry, CancellationToken cancellationToken);
}

/// <summary>One durable access, completion, or skip event.</summary>
/// <param name="EntityId">Entity the event belongs to.</param>
/// <param name="Kind">Event kind.</param>
/// <param name="OccurredAt">Timestamp when the event occurred.</param>
/// <param name="PositionSeconds">Optional playback position.</param>
/// <param name="DurationSeconds">Optional media runtime.</param>
/// <param name="SessionId">Optional playback session used to deduplicate access starts.</param>
public sealed record ConsumptionEventAppend(
    Guid EntityId,
    ConsumptionEventKind Kind,
    DateTimeOffset OccurredAt,
    double? PositionSeconds,
    double? DurationSeconds,
    string? SessionId = null);

internal sealed class NullConsumptionEventStore : IConsumptionEventStore {
    public static NullConsumptionEventStore Instance { get; } = new();

    private NullConsumptionEventStore() {
    }

    public Task StageAsync(ConsumptionEventAppend entry, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
