using Prismedia.Contracts.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Read-only manager evidence used after Prismedia has frozen further actions for the scope.</summary>
public interface IIntegrationManagerReleaseGateway {
    /// <summary>Inspects exact monitoring and complete activity without cancelling or creating remote work.</summary>
    Task<ManagedReleaseObservation> InspectReleaseAsync(string pluginId, IntegrationConnectionContext connection,
        InspectManagedReleaseInput input, CancellationToken token);
}
