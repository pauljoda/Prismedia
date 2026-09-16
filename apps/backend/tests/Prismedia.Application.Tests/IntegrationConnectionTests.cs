using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Tests;

public sealed class IntegrationConnectionTests {
    private const string PluginId = "fixture";
    private static IntegrationConnection Create() => IntegrationConnection.Create(PluginId, "Books", "http://catalog.test", true,
        [PluginCapability.CatalogDiscovery], new Dictionary<string, string>());
    private static IntegrationSupport Support => new(PluginCapability.CatalogDiscovery, [IntegrationOperation.Search], [EntityKind.Book]);

    [Fact]
    public void SamePluginCanHaveIndependentConnectionsAndConfigurationInvalidatesAuthority() {
        var first = Create();
        var second = Create();
        Assert.NotEqual(first.State.Id, second.State.Id);
        Assert.False(first.Allows(Support.Kind, IntegrationOperation.Search, EntityKind.Book));
        first.RecordProbe("installation-a", [Support], null, DateTimeOffset.UtcNow);
        Assert.True(first.Allows(Support.Kind, IntegrationOperation.Search, EntityKind.Book));
        first.Configure("Renamed", first.State.BaseUrl, true, first.State.EnabledCapabilities, first.State.Settings);
        Assert.False(first.Allows(Support.Kind, IntegrationOperation.Search, EntityKind.Book));
        Assert.Equal(ConnectionStatus.Unverified, second.State.Status);
    }

    [Fact]
    public void OutagePreservesIdentityAndReplacementRevokesCapabilities() {
        var connection = Create();
        connection.RecordProbe("installation-a", [Support], null, DateTimeOffset.UtcNow);
        connection.RecordProbe(null, [], "Unavailable", DateTimeOffset.UtcNow);
        Assert.Equal("installation-a", connection.State.RemoteInstanceId);
        Assert.Equal(ConnectionStatus.Unavailable, connection.State.Status);
        connection.RecordProbe("installation-b", [Support], null, DateTimeOffset.UtcNow);
        Assert.Equal(ConnectionStatus.IdentityChanged, connection.State.Status);
        Assert.Empty(connection.State.EffectiveCapabilities);
        Assert.Equal("installation-a", connection.State.RemoteInstanceId);
        Assert.Throws<ArgumentException>(() => connection.Configure("Moved", "http://other.test", true,
            connection.State.EnabledCapabilities, connection.State.Settings));
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://user:secret@catalog.test")]
    [InlineData("http://catalog.test?token=secret")]
    public void CredentialsAndNonHttpAddressesCannotEnterPublicConnectionConfiguration(string address) =>
        Assert.Throws<ArgumentException>(() => IntegrationConnection.Create(PluginId, "Books", address, true,
            [PluginCapability.CatalogDiscovery], new Dictionary<string, string>()));
}
