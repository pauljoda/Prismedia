using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Managed release inspection, limited to work the plugin declares it can control.</summary>
public sealed partial class IntegrationPluginGateway : IIntegrationManagerReleaseGateway {
    #region Actions - Release Inspection

    /// <inheritdoc />
    public async Task<ManagedReleaseObservation> InspectReleaseAsync(string pluginId, IntegrationConnectionContext connection,
        InspectManagedReleaseInput input, CancellationToken token) =>
        await InvokeAsync<InspectManagedReleaseInput, ManagedReleaseObservation>(
            await RequireControlAsync(pluginId, IntegrationOperation.InspectManagedRelease, input.Scope, token),
            IntegrationOperation.InspectManagedRelease, connection, input, token);

    #endregion
}
