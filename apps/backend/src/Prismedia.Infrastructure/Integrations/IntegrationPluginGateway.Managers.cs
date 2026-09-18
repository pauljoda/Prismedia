using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Application.Integrations;

namespace Prismedia.Infrastructure.Integrations;

public sealed partial class IntegrationPluginGateway : IIntegrationManagerGateway {
    /// <inheritdoc />
    public async Task<ManagedDiscoveryPage> DiscoverAsync(string pluginId, IntegrationConnectionContext connection, ManagedDiscoveryQuery input, CancellationToken cancellationToken) =>
        await InvokeAsync<ManagedDiscoveryQuery, ManagedDiscoveryPage>(await RequireManagerAsync(pluginId, cancellationToken), IntegrationOperation.DiscoverManaged, connection, input, cancellationToken);
    /// <inheritdoc />
    public async Task<ManagedLibraryPage> SearchLibraryAsync(string pluginId, IntegrationConnectionContext connection, ManagedLibraryQuery input, CancellationToken cancellationToken) =>
        await InvokeAsync<ManagedLibraryQuery, ManagedLibraryPage>(await RequireManagerAsync(pluginId, cancellationToken), IntegrationOperation.SearchLibrary, connection, input, cancellationToken);
    /// <inheritdoc />
    public async Task<ManagedItemSnapshot> GetLibraryItemAsync(string pluginId, IntegrationConnectionContext connection, ManagedItemInput input, CancellationToken cancellationToken) =>
        await InvokeAsync<ManagedItemInput, ManagedItemSnapshot>(await RequireManagerAsync(pluginId, cancellationToken), IntegrationOperation.GetLibraryItem, connection, input, cancellationToken);
    /// <inheritdoc />
    public async Task<ManagerOptions> GetOptionsAsync(string pluginId, IntegrationConnectionContext connection, ManagerOptionsInput input, CancellationToken cancellationToken) =>
        await InvokeAsync<ManagerOptionsInput, ManagerOptions>(await RequireManagerAsync(pluginId, cancellationToken), IntegrationOperation.ManagerOptions, connection, input, cancellationToken);
    private async Task<Plugins.PluginDescriptor> RequireManagerAsync(string pluginId, CancellationToken cancellationToken) =>
        await FindDescriptorAsync(pluginId, cancellationToken) ?? throw new IntegrationInvocationException("The integration plugin is unavailable or disabled.");
}
