using System.Text.Json;
using Prismedia.Contracts.Plugins;

namespace Prismedia.Application.Jobs.Ports;

/// <summary>
/// Port that runs a full-tree identify cascade for one queued entity in the background, streaming the
/// growing proposal onto the queue item as each child resolves and clearing the item's cascade marker
/// when finished.
/// </summary>
public interface IIdentifyCascadeRunner {
    /// <summary>Runs the cascade described by <paramref name="payload"/> to completion.</summary>
    /// <param name="payload">The entity, provider, and query that seeded the root proposal.</param>
    /// <param name="cascadeJobId">
    /// Id of this cascade's own job run. The cascade only streams onto the queue item while the item is
    /// still queued and still marked with this id, so a removed or superseded item is left untouched.
    /// </param>
    /// <param name="isFinalAttempt">
    /// True when this is the cascade job's last attempt, so a failure now is terminal. The cascade keeps
    /// the queue item's cascade marker set across retryable failures (so the review screen stays gated)
    /// and clears it only on success or on this final failed attempt.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the running cascade.</param>
    Task RunAsync(IdentifyCascadePayload payload, Guid cascadeJobId, bool isFinalAttempt, CancellationToken cancellationToken);
}

/// <summary>
/// Payload for an <see cref="Prismedia.Domain.Entities.JobType.IdentifyCascade"/> job: identifies the
/// entity's full child tree with the same provider and query that seeded its root proposal.
/// </summary>
/// <param name="EntityId">Queued entity whose child tree is being resolved.</param>
/// <param name="Provider">Provider code used to seed the root proposal.</param>
/// <param name="Query">Query (typically the picked candidate's external id) used to seed the root.</param>
/// <param name="HideNsfw">NSFW visibility carried from the originating request.</param>
public sealed record IdentifyCascadePayload(
    Guid EntityId,
    string Provider,
    IdentifyQuery? Query,
    bool HideNsfw) {
    public string ToJson() => JsonSerializer.Serialize(this);

    public static IdentifyCascadePayload Parse(string payloadJson) =>
        JsonSerializer.Deserialize<IdentifyCascadePayload>(payloadJson)
            ?? throw new InvalidOperationException("IdentifyCascade payload is missing or invalid.");
}
