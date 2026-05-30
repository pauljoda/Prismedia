namespace Prismedia.Application.Jobs.Ports;

/// <summary>
/// Port that runs auto identify for a single entity: it walks the configured providers in order
/// and applies the first proposal that clears the confidence/exact-match bar, exactly as a manual
/// identify-and-apply would (full fields, structural children, relationships, and artwork).
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
