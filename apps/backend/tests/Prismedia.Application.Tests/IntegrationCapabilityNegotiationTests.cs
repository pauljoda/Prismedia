using Prismedia.Application.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests;

public sealed class IntegrationCapabilityNegotiationTests {
    [Fact]
    public void RemoteClaimsCannotExpandPackageOperationsKindsOrUserPermissions() {
        PluginIntegrationCapability[] declared = [
            new(PluginCapability.CatalogDiscovery, [IntegrationOperation.Search, IntegrationOperation.Browse], [EntityKind.Book]),
            new(PluginCapability.TransferExecutor, [IntegrationOperation.Submit], [EntityKind.Book])];
        PluginIntegrationCapability[] remote = [
            new(PluginCapability.CatalogDiscovery, [IntegrationOperation.Search, IntegrationOperation.Submit], [EntityKind.Book, EntityKind.Movie]),
            new(PluginCapability.TransferExecutor, [IntegrationOperation.Submit], [EntityKind.Book])];

        var result = Assert.Single(IntegrationCapabilityNegotiation.Intersect(declared, remote, [PluginCapability.CatalogDiscovery]));

        Assert.Equal([IntegrationOperation.Search], result.Operations);
        Assert.Equal([EntityKind.Book], result.EntityKinds);
    }

    [Fact]
    public void DuplicateOrEmptyRemoteDeclarationsDoNotGrantAuthority() {
        var support = new PluginIntegrationCapability(PluginCapability.CatalogDiscovery, [IntegrationOperation.Search], [EntityKind.Book]);
        Assert.Empty(IntegrationCapabilityNegotiation.Intersect([support], [support, support], [support.Kind]));
        Assert.Empty(IntegrationCapabilityNegotiation.Intersect([support], [support with { EntityKinds = [] }], [support.Kind]));
        Assert.Empty(IntegrationCapabilityNegotiation.Intersect([support], [], [support.Kind]));
    }
}
