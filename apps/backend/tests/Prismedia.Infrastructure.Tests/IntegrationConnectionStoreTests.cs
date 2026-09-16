using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Integrations;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class IntegrationConnectionStoreTests : IDisposable {
    private const string CredentialKey = "api-key";
    private const string Credential = "fixture-secret-never-in-public-documents";
    private const string PluginId = "fixture";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"prismedia-connection-store-{Guid.NewGuid():N}");
    private readonly DbContextOptions<PrismediaDbContext> _options = new DbContextOptionsBuilder<PrismediaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    private static IntegrationConnection NewConnection() => IntegrationConnection.Create(PluginId, "Books", "http://catalog.test", true,
        [PluginCapability.CatalogDiscovery], new Dictionary<string, string>());

    [Fact]
    public async Task CredentialsPersistEncryptedAcrossRestartsAndCannotMoveBetweenConnections() {
        var first = NewConnection();
        var second = NewConnection();
        await using (var db = new PrismediaDbContext(_options)) {
            var store = new EfIntegrationConnectionStore(db, new ConnectionSecretProtector(_root));
            await store.SaveAsync(first, null, new Dictionary<string, string?> { [CredentialKey] = Credential }, CancellationToken.None);
            await store.SaveAsync(second, null, new Dictionary<string, string?>(), CancellationToken.None);
            var row = await db.IntegrationConnections.SingleAsync(item => item.Id == first.State.Id);
            Assert.DoesNotContain(Credential, row.ProtectedSecretsJson);
            Assert.DoesNotContain(Credential, JsonSerializer.Serialize(await store.ListAsync(CancellationToken.None)));
        }
        await using (var db = new PrismediaDbContext(_options)) {
            var protector = new ConnectionSecretProtector(_root);
            var store = new EfIntegrationConnectionStore(db, protector);
            Assert.Equal(Credential, (await store.ReadSecretsAsync(first.State.Id, CancellationToken.None))[CredentialKey]);
            Assert.Empty(await store.ReadSecretsAsync(second.State.Id, CancellationToken.None));
            var encrypted = JsonSerializer.Deserialize<Dictionary<string, string>>((await db.IntegrationConnections.SingleAsync(item => item.Id == first.State.Id)).ProtectedSecretsJson)![CredentialKey];
            Assert.Throws<ConnectionSecretUnavailableException>(() => protector.Unprotect(second.State.Id, CredentialKey, encrypted));
        }
    }

    [Fact]
    public async Task StaleProbeCannotOverwriteEditedConfigurationAndSecretsCanBeExplicitlyRemoved() {
        var connection = NewConnection();
        await using var db = new PrismediaDbContext(_options);
        var store = new EfIntegrationConnectionStore(db, new ConnectionSecretProtector(_root));
        await store.SaveAsync(connection, null, new Dictionary<string, string?> { [CredentialKey] = Credential }, CancellationToken.None);
        var stale = (await store.FindAsync(connection.State.Id, CancellationToken.None))!.Connection;
        connection.Configure("Updated", connection.State.BaseUrl, false, connection.State.EnabledCapabilities, connection.State.Settings);
        await store.SaveAsync(connection, 1, new Dictionary<string, string?> { [CredentialKey] = null }, CancellationToken.None);
        stale.RecordProbe("remote", [], null, DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<ConnectionConflictException>(() => store.SaveAsync(stale, 1, new Dictionary<string, string?>(), CancellationToken.None));
        var saved = (await store.FindAsync(connection.State.Id, CancellationToken.None))!;
        Assert.Equal("Updated", saved.Connection.State.Name);
        Assert.Equal(ConnectionStatus.Disabled, saved.Connection.State.Status);
        Assert.Empty(saved.ConfiguredSecretKeys);
        Assert.Empty(await store.ReadSecretsAsync(connection.State.Id, CancellationToken.None));
    }

    [Fact]
    public async Task MappedConnectionCannotChangeSourceOrBeDeletedButCanBeDisabled() {
        await using var db = new PrismediaDbContext(_options);
        var store = new EfIntegrationConnectionStore(db, new ConnectionSecretProtector(_root));
        var connection = NewConnection();
        await store.SaveAsync(connection, null, new Dictionary<string, string?>(), default);
        db.ExternalLibraryMounts.Add(new ExternalLibraryMountRow { Id = Guid.NewGuid(), ConnectionId = connection.State.Id,
            LibraryRootId = Guid.NewGuid(), RemoteRootId = "1", RemotePath = "/remote", LocalPath = _root });
        await db.SaveChangesAsync();
        var changed = (await store.FindAsync(connection.State.Id, default))!.Connection;
        changed.Configure(changed.State.Name, "http://different.test", true, changed.State.EnabledCapabilities, changed.State.Settings);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(changed, 1, new Dictionary<string, string?>(), default));
        await Assert.ThrowsAsync<ConnectionInUseException>(() => store.DeleteAsync(connection.State.Id, 1, default));
        connection.Configure(connection.State.Name, connection.State.BaseUrl, false, connection.State.EnabledCapabilities, connection.State.Settings);
        await store.SaveAsync(connection, 1, new Dictionary<string, string?>(), default);
        Assert.False((await store.FindAsync(connection.State.Id, default))!.Connection.State.Enabled);
        Assert.Single(await db.ExternalLibraryMounts.ToArrayAsync());
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
