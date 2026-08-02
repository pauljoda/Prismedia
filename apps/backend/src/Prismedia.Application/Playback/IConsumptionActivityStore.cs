using Prismedia.Domain.Entities;

namespace Prismedia.Application.Playback;

/// <summary>Application port for incrementing one entity/day active-duration bucket.</summary>
public interface IConsumptionActivityStore {
    /// <summary>Stages one bounded duration increment in the current unit of work.</summary>
    Task StageAsync(ConsumptionActivityAppend entry, CancellationToken cancellationToken);
}

/// <summary>One active-duration increment assigned to a local calendar day.</summary>
/// <param name="EntityId">Entity receiving the active time.</param>
/// <param name="Kind">Viewing, listening, or reading mode.</param>
/// <param name="ActivityDate">Client-local calendar day.</param>
/// <param name="DurationSeconds">Bounded active duration.</param>
public sealed record ConsumptionActivityAppend(
    Guid EntityId,
    ConsumptionActivityKind Kind,
    DateOnly ActivityDate,
    double DurationSeconds);

internal sealed class NullConsumptionActivityStore : IConsumptionActivityStore {
    public static NullConsumptionActivityStore Instance { get; } = new();

    private NullConsumptionActivityStore() {
    }

    public Task StageAsync(ConsumptionActivityAppend entry, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
