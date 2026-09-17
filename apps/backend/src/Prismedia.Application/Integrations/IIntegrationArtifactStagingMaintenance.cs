namespace Prismedia.Application.Integrations;

/// <summary>Removes private transfer staging only after durable terminal import evidence has aged past retention.</summary>
public interface IIntegrationArtifactStagingMaintenance {
    /// <summary>Deletes eligible operation directories last updated before <paramref name="completedBefore"/> and returns the count removed.</summary>
    Task<int> SweepAsync(DateTimeOffset completedBefore, CancellationToken cancellationToken);
}
