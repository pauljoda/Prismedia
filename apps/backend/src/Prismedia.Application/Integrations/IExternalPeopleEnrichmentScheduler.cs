namespace Prismedia.Application.Integrations;

/// <summary>
/// Schedules one exact, credits-only metadata enrichment for an externally managed holding.
/// Implementations must remain network-free so ordinary reconciliation is never blocked by metadata providers.
/// </summary>
public interface IExternalPeopleEnrichmentScheduler {
    #region Abstract Methods

    /// <summary>
    /// Attaches conflict-free pinned identities and queues enrichment when the holding's current
    /// provider fingerprint has not already been attempted.
    /// </summary>
    Task ScheduleAsync(
        Guid holdingId,
        CancellationToken cancellationToken);

    #endregion
}
