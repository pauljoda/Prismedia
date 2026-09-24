namespace Prismedia.Application.Integrations;

/// <summary>Runs exact people enrichment inside the durable metadata job.</summary>
public interface IExternalPeopleEnrichmentRunner {
    #region Abstract Methods

    /// <summary>Enriches missing credits without searching by title or changing acquisition state.</summary>
    Task<ExternalPeopleEnrichmentResult> RunAsync(
        Guid holdingId,
        Guid entityId,
        string fingerprint,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records a terminal attempt after the queue exhausts transient retries, allowing a changed
    /// connection or provider fingerprint to become eligible later.
    /// </summary>
    Task RecordTerminalAttemptAsync(
        Guid holdingId,
        string fingerprint,
        CancellationToken cancellationToken);

    #endregion
}
