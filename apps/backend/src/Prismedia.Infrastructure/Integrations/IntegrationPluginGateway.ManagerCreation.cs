using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Managed creation lookups and ensures, limited to kinds the plugin declares it can create.</summary>
public sealed partial class IntegrationPluginGateway : IIntegrationManagerCreationGateway {
    #region Actions - Managed Creation

    /// <inheritdoc />
    public async Task<ManagedLookupResult> LookupAsync(string pluginId, IntegrationConnectionContext connection,
        ManagedLookupInput input, CancellationToken token) =>
        await InvokeAsync<ManagedLookupInput, ManagedLookupResult>(
            await RequireCreationAsync(pluginId, IntegrationOperation.LookupManaged, input.EntityKind, token),
            IntegrationOperation.LookupManaged, connection, input, token);

    /// <inheritdoc />
    public async Task<EnsureManagedResult> EnsureAsync(string pluginId, IntegrationConnectionContext connection,
        EnsureManagedInput input, CancellationToken token) =>
        await InvokeAsync<EnsureManagedInput, EnsureManagedResult>(
            await RequireCreationAsync(pluginId, IntegrationOperation.EnsureManaged, input.Work.EntityKind, token),
            IntegrationOperation.EnsureManaged, connection, input, token);

    #endregion

    #region Actions - Capability Checks

    private async Task<PluginDescriptor> RequireCreationAsync(string pluginId, IntegrationOperation operation, EntityKind kind,
        CancellationToken token) {
        var descriptor = await RequireManagerAsync(pluginId, token);
        if (descriptor.Manifest.Integration?.Capabilities.Any(capability => capability.Kind == PluginCapability.ExternalManager
            && capability.Operations.Contains(operation) && capability.EntityKinds.Contains(kind)) != true) {
            throw new IntegrationInvocationException("The installed plugin does not declare managed creation for this kind.");
        }

        return descriptor;
    }

    #endregion
}
