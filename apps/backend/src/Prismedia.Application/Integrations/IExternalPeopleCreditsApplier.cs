using Prismedia.Contracts.Plugins;

namespace Prismedia.Application.Integrations;

/// <summary>
/// Applies provider credits only while the target still has no people relationships. The final
/// absence check and metadata mutation share the entity lifecycle lease so a concurrent user edit
/// cannot be replaced after an external lookup completes.
/// </summary>
public interface IExternalPeopleCreditsApplier {
    #region Abstract Methods

    /// <summary>Attempts one credits-only apply under the entity lifecycle lease.</summary>
    Task<ExternalPeopleCreditsApplyResult> ApplyIfMissingAsync(
        Guid entityId,
        EntityMetadataProposal proposal,
        CancellationToken cancellationToken);

    #endregion
}
