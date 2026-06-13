using System.Text.Json;
using Prismedia.Contracts.Plugins;

namespace Prismedia.Application.Jobs.Ports;

/// <summary>
/// Port that runs one requested identify provider search for a queued entity in the background.
/// The runner owns the queue item's <c>queued → searching → search/proposal/error</c> transitions
/// and only writes results while the item is still marked with this run's job id, so a search that
/// was superseded by a newer request never overwrites the newer run's state.
/// </summary>
public interface IIdentifySearchRunner {
    /// <summary>Runs the search described by <paramref name="payload"/> to completion.</summary>
    /// <param name="payload">The entity, optional provider, and optional query to search with.</param>
    /// <param name="searchJobId">
    /// Id of this search's own job run. Results are only written while the queue item's search marker
    /// still names this id.
    /// </param>
    /// <param name="isFinalAttempt">
    /// True when this is the job's last attempt, so a failure now is terminal rather than retried.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the running search.</param>
    Task RunAsync(IdentifySearchPayload payload, Guid searchJobId, bool isFinalAttempt, CancellationToken cancellationToken);
}

/// <summary>
/// Payload for an <see cref="Prismedia.Domain.Entities.JobType.IdentifySearch"/> job: one provider
/// search for one identify queue item.
/// </summary>
/// <param name="EntityId">Queued entity to search for.</param>
/// <param name="Provider">
/// Provider code selected by the user, or null to walk the enabled providers that can identify the
/// entity's kind, in catalog order, until one returns a result.
/// </param>
/// <param name="Query">Optional title, URL, or external ID override (e.g. a picked candidate).</param>
/// <param name="HideNsfw">NSFW visibility carried from the originating request.</param>
/// <param name="IsForeground">True when this search came from a direct single-entity manual identify request.</param>
public sealed record IdentifySearchPayload(
    Guid EntityId,
    string? Provider,
    IdentifyQuery? Query,
    bool HideNsfw,
    bool IsForeground = false) {
    public string ToJson() => JsonSerializer.Serialize(this);

    public static IdentifySearchPayload Parse(string payloadJson) =>
        JsonSerializer.Deserialize<IdentifySearchPayload>(payloadJson)
            ?? throw new InvalidOperationException("IdentifySearch payload is missing or invalid.");
}
