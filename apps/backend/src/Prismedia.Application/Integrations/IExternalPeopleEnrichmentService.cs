using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;

namespace Prismedia.Application.Integrations;

/// <summary>
/// Schedules one exact, credits-only metadata enrichment for an externally managed holding.
/// Implementations must remain network-free so ordinary reconciliation is never blocked by metadata providers.
/// </summary>
public interface IExternalPeopleEnrichmentScheduler {
    /// <summary>
    /// Attaches conflict-free pinned identities and queues enrichment when the holding's current
    /// provider fingerprint has not already been attempted.
    /// </summary>
    Task ScheduleAsync(
        Guid holdingId,
        CancellationToken cancellationToken);
}

/// <summary>Runs exact people enrichment inside the durable metadata job.</summary>
public interface IExternalPeopleEnrichmentRunner {
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
}

/// <summary>
/// Applies provider credits only while the target still has no people relationships. The final
/// absence check and metadata mutation share the entity lifecycle lease so a concurrent user edit
/// cannot be replaced after an external lookup completes.
/// </summary>
public interface IExternalPeopleCreditsApplier {
    /// <summary>Attempts one credits-only apply under the entity lifecycle lease.</summary>
    Task<ExternalPeopleCreditsApplyResult> ApplyIfMissingAsync(
        Guid entityId,
        EntityMetadataProposal proposal,
        CancellationToken cancellationToken);
}

/// <summary>Outcome of the atomic missing-credits apply.</summary>
public enum ExternalPeopleCreditsApplyResult {
    /// <summary>The provider credits were applied.</summary>
    Applied,

    /// <summary>Credits already existed when the lifecycle lease was acquired.</summary>
    ExistingCredits,

    /// <summary>A user-authored credits edit or clear protects the section from enrichment.</summary>
    ProtectedByUser,

    /// <summary>The target no longer exists.</summary>
    NotFound,

    /// <summary>Destructive lifecycle ownership prevented the mutation.</summary>
    LifecycleConflict
}

/// <summary>Outcome of one exact external-library people enrichment.</summary>
/// <param name="Applied">Whether credits were applied.</param>
/// <param name="Provider">Provider that supplied credits, when applied.</param>
/// <param name="Message">Concise job progress message.</param>
public sealed record ExternalPeopleEnrichmentResult(
    bool Applied,
    string? Provider,
    string Message);
