namespace Prismedia.Application.Jobs.Ports;

/// <summary>
/// Port that runs auto identify for a single entity: it walks the configured providers in order
/// and applies the first proposal that clears the confidence/exact-match bar, exactly as a manual
/// identify-and-apply would (metadata fields, structural children, relationships, and artwork).
/// </summary>
public interface IAutoIdentifyRunner {
    /// <summary>
    /// Attempts to auto-identify and fully apply metadata for one entity.
    /// Honors the auto-identify enabled flag, selected entity kinds, the un-organized-only gate,
    /// and the configured provider order. Returns a result describing what happened.
    /// </summary>
    /// <param name="entityId">Entity to identify.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AutoIdentifyResult> RunAsync(Guid entityId, CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to auto-identify and fully apply metadata for one entity with job-supplied execution
    /// options, such as progress-sensitive timeout behavior. Implementations that do not need the
    /// options can rely on the default forwarding behavior.
    /// </summary>
    /// <param name="entityId">Entity to identify.</param>
    /// <param name="options">Execution options for this run.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AutoIdentifyResult> RunAsync(
        Guid entityId,
        AutoIdentifyRunOptions options,
        CancellationToken cancellationToken) =>
        RunAsync(entityId, cancellationToken);
}

/// <summary>
/// Shared auto-identify policy constants used by the runner and the scan enqueue path.
/// </summary>
public static class AutoIdentifyPolicy {
    /// <summary>
    /// How many completed auto-identify runs may end without a confident match before the entity
    /// is excluded from auto identify and left for manual identification. Runs that never reach a
    /// provider (disabled, wrong kind, no capable provider) do not consume an attempt.
    /// </summary>
    public const int MaxAttemptsPerEntity = 3;
}

/// <summary>
/// Execution options supplied by the job handler to an auto-identify run.
/// </summary>
/// <param name="InactivityTimeout">
/// Optional maximum time the run may spend without observable provider/cascade progress. A long
/// series may run beyond this total duration as long as children keep resolving.
/// </param>
/// <param name="ReportProgressAsync">Optional callback for publishing live job progress.</param>
public sealed record AutoIdentifyRunOptions(
    TimeSpan? InactivityTimeout = null,
    Func<AutoIdentifyProgress, CancellationToken, Task>? ReportProgressAsync = null) {
    public static AutoIdentifyRunOptions Default { get; } = new();
}

/// <summary>
/// Stage of observable progress during an auto-identify run.
/// </summary>
public enum AutoIdentifyProgressPhase {
    /// <summary>The provider is identifying/cascading metadata proposals.</summary>
    Identifying,

    /// <summary>The accepted proposal is being applied to local entities.</summary>
    Applying
}

/// <summary>
/// Progress observed while a provider is walking an auto-identify proposal tree or applying an accepted proposal.
/// </summary>
/// <param name="Phase">Current auto-identify stage.</param>
/// <param name="ResolvedSteps">Number of progress callbacks seen for this run.</param>
/// <param name="RootChildCount">Current number of direct structural root children in the partial proposal.</param>
/// <param name="CurrentTitle">Display title of the entity currently being applied, when in the apply stage.</param>
/// <param name="CurrentPath">Structural path of the entity currently being applied, when in the apply stage.</param>
public sealed record AutoIdentifyProgress(
    AutoIdentifyProgressPhase Phase,
    int ResolvedSteps,
    int RootChildCount,
    string? CurrentTitle = null,
    IReadOnlyList<string>? CurrentPath = null) {
    public AutoIdentifyProgress(int resolvedSteps, int rootChildCount)
        : this(AutoIdentifyProgressPhase.Identifying, resolvedSteps, rootChildCount) {
    }
}

/// <summary>
/// Outcome of an auto-identify attempt for a single entity.
/// </summary>
/// <param name="Applied">True when a provider proposal was applied to the entity.</param>
/// <param name="Provider">Provider id that produced the applied proposal, when applied.</param>
/// <param name="Confidence">Confidence of the applied proposal, when reported by the provider.</param>
/// <param name="SkipReason">Human-readable reason when nothing was applied.</param>
public sealed record AutoIdentifyResult(
    bool Applied,
    string? Provider = null,
    decimal? Confidence = null,
    string? SkipReason = null);
