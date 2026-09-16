using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Integrations;

public sealed partial class IntegrationPluginGateway : IIntegrationManagerControlGateway {
    /// <inheritdoc />
    public async Task<ManagedControlState> ReconcileAsync(string pluginId, IntegrationConnectionContext connection, ReconcileManagedInput input, CancellationToken token) =>
        await InvokeAsync<ReconcileManagedInput, ManagedControlState>(await RequireControlAsync(pluginId, IntegrationOperation.ReconcileManaged, input.Scope, token),
            IntegrationOperation.ReconcileManaged, connection, input, token);
    /// <inheritdoc />
    public async Task<ManagedMutationResult> ConfigureAsync(string pluginId, IntegrationConnectionContext connection, ConfigureManagedInput input, CancellationToken token) =>
        await InvokeAsync<ConfigureManagedInput, ManagedMutationResult>(await RequireControlAsync(pluginId, IntegrationOperation.ConfigureManaged, input.Scope, token),
            IntegrationOperation.ConfigureManaged, connection, input, token);
    /// <inheritdoc />
    public async Task<ManagedMutationResult> RequestAsync(string pluginId, IntegrationConnectionContext connection, RequestManagedInput input, CancellationToken token) =>
        await InvokeAsync<RequestManagedInput, ManagedMutationResult>(await RequireControlAsync(pluginId, IntegrationOperation.RequestManaged, input.Scope, token),
            IntegrationOperation.RequestManaged, connection, input, token);

    private async Task<PluginDescriptor> RequireControlAsync(string pluginId, IntegrationOperation operation, ManagedControlScope scope, CancellationToken token) {
        var descriptor = await RequireManagerAsync(pluginId, token);
        if (scope?.Item is null || descriptor.Manifest.Integration?.Capabilities.Any(capability => capability.Kind == PluginCapability.ExternalManager
            && capability.Operations.Contains(operation) && capability.EntityKinds.Contains(scope.Item.EntityKind)) != true)
            throw new IntegrationInvocationException("The installed plugin does not declare this manager control for the selected work.");
        return descriptor;
    }
}
