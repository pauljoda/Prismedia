namespace Prismedia.Infrastructure.Integrations;

/// <summary>Builds exact-identity people enrichment plans for connected holdings from saved configuration.</summary>
internal interface IExternalPeopleEnrichmentPlanResolver {
    #region Abstract Methods

    /// <summary>Resolves the current enrichment plan for an unreleased holding, or null when enrichment does not apply.</summary>
    Task<ExternalPeopleEnrichmentPlan?> ResolveAsync(Guid holdingId, CancellationToken cancellationToken);

    #endregion
}
