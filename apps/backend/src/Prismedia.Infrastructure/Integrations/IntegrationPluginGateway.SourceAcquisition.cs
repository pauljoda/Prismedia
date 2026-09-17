using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Integrations;

public sealed partial class IntegrationPluginGateway : IIntegrationSourceAcquisitionGateway {
    /// <inheritdoc />
    public async Task<SourceAcquisitionObservation> ObserveSourceAsync(string pluginId, IntegrationConnectionContext connection,
        ObserveSourceInput input, CancellationToken cancellationToken) {
        var descriptor = await FindDescriptorAsync(pluginId, cancellationToken)
            ?? throw new IntegrationInvocationException("The integration plugin is unavailable or disabled.");
        return await InvokeAsync<ObserveSourceInput, SourceAcquisitionObservation>(descriptor, IntegrationOperation.ObserveSource,
            connection, input, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SourceAcquisitionObservation> RequestSourceAsync(string pluginId, IntegrationConnectionContext connection,
        RequestSourceInput input, CancellationToken cancellationToken) {
        var descriptor = await FindDescriptorAsync(pluginId, cancellationToken)
            ?? throw new IntegrationInvocationException("The integration plugin is unavailable or disabled.");
        return await InvokeAsync<RequestSourceInput, SourceAcquisitionObservation>(descriptor, IntegrationOperation.RequestSource,
            connection, input, cancellationToken);
    }
}
