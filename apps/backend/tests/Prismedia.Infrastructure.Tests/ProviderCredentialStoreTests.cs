using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Security;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Tests;

public sealed class ProviderCredentialStoreTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"provider-credentials-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("false")]
    [InlineData("invalid")]
    public async Task DisabledStartupMigrationsDoNotResolveOrWriteADatabase(string setting) {
        await using var services = new ServiceCollection().BuildServiceProvider();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Prismedia:ApplyMigrations"] = setting
        }).Build();
        await ProviderCredentialUpgradeRunner.UpgradeAsync(services, configuration);
    }

    [Fact]
    public async Task ProtectedValuesSurviveAProcessRestartAndAreBoundToTheProviderAndKey() {
        await using var db = Context();
        var row = await Seed(db);
        var protector = new ProviderCredentialProtector(_root);
        var store = new ProviderCredentialStore(db, protector);
        store.SetValue(row, "saved-fixture-secret", DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        Assert.NotEqual("saved-fixture-secret", row.EncryptedValue);
        Assert.DoesNotContain("saved-fixture-secret", row.EncryptedValue);
        var restarted = new ProviderCredentialProtector(_root);
        Assert.Equal("saved-fixture-secret", (await new ProviderCredentialStore(db, restarted).ReadAsync(row.ProviderConfigId, [row.CredentialKey], default))[row.CredentialKey]);
        Assert.Throws<InvalidOperationException>(() => restarted.Unprotect(Guid.NewGuid(), row.CredentialKey, row.EncryptedValue, row.ProtectionVersion));
        Assert.Throws<InvalidOperationException>(() => restarted.Unprotect(row.ProviderConfigId, "another-field", row.EncryptedValue, row.ProtectionVersion));
        Assert.Throws<InvalidOperationException>(() => new ProviderCredentialProtector(Path.Combine(_root, "other-instance")).Unprotect(row.ProviderConfigId, row.CredentialKey, row.EncryptedValue, row.ProtectionVersion));
        Assert.Throws<InvalidOperationException>(() => restarted.Unprotect(row.ProviderConfigId, row.CredentialKey, "plaintext", ProviderCredentialProtector.CurrentVersion));
        Assert.Throws<InvalidOperationException>(() => restarted.Unprotect(row.ProviderConfigId, row.CredentialKey, row.EncryptedValue, 999));
    }

    [Fact]
    public async Task StartupUpgradeProtectsDisabledProvidersAndPreservesCredentialEditTimes() {
        await using var db = Context();
        var row = await Seed(db); var updatedAt = row.UpdatedAt;
        var store = new ProviderCredentialStore(db, new(_root));
        await store.UpgradeLegacyAsync(default);
        Assert.Equal(ProviderCredentialProtector.CurrentVersion, row.ProtectionVersion);
        Assert.NotEqual("legacy-fixture-secret", row.EncryptedValue);
        Assert.Equal(updatedAt, row.UpdatedAt);
        var protectedValue = row.EncryptedValue;
        await store.UpgradeLegacyAsync(default);
        Assert.Equal(protectedValue, row.EncryptedValue);
        Assert.Empty(await store.ReadAsync(row.ProviderConfigId, ["undeclared-field"], default));
        Assert.Equal("legacy-fixture-secret", (await store.ReadAsync(row.ProviderConfigId, [row.CredentialKey], default))[row.CredentialKey]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpgradeCannotOverwriteARotatedSecretOrResurrectADeletedCredential(bool delete) {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var initial = database.CreateContext();
        var row = await Seed(initial);
        var protector = new ProviderCredentialProtector(_root);
        var gate = new BeforeUpgrade(async () => {
            await using var writer = database.CreateContext();
            var current = await writer.ProviderCredentials.SingleAsync();
            if (delete) writer.ProviderCredentials.Remove(current);
            else new ProviderCredentialStore(writer, protector).SetValue(current, "rotated-fixture-secret", DateTimeOffset.UtcNow);
            await writer.SaveChangesAsync();
        });
        await using var upgrade = new PrismediaDbContext(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseNpgsql(initial.Database.GetConnectionString()).AddInterceptors(gate).Options);
        var result = await new ProviderCredentialStore(upgrade, protector).ReadAsync(row.ProviderConfigId, [row.CredentialKey], default);
        Assert.True(gate.Fired);
        if (delete) { Assert.Empty(result); Assert.Empty(await initial.ProviderCredentials.AsNoTracking().ToArrayAsync()); }
        else {
            Assert.Equal("rotated-fixture-secret", result[row.CredentialKey]);
            var saved = await initial.ProviderCredentials.AsNoTracking().SingleAsync();
            Assert.Equal(ProviderCredentialProtector.CurrentVersion, saved.ProtectionVersion);
            Assert.Equal("rotated-fixture-secret", protector.Unprotect(saved.ProviderConfigId, saved.CredentialKey, saved.EncryptedValue, saved.ProtectionVersion));
        }
    }

    [Fact]
    public async Task CatalogPassesOnlyDeclaredKeysAndEnvironmentOverridesDoNotRequireOldKeys() {
        await using var db = Context();
        var row = await Seed(db);
        var provider = await db.ProviderConfigs.SingleAsync();
        provider.Enabled = true;
        provider.ProviderCode = "credential-boundary-" + Guid.NewGuid().ToString("N");
        var store = new ProviderCredentialStore(db, new(_root));
        store.SetValue(row, "saved-secret", DateTimeOffset.UtcNow);
        db.ProviderCredentials.Add(new ProviderCredentialRow {
            Id = Guid.NewGuid(), ProviderConfigId = provider.Id, CredentialKey = "removed-field",
            EncryptedValue = "unreadable-old-value", ProtectionVersion = ProviderCredentialProtector.CurrentVersion,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var manifest = new PluginManifest(2, ["prismedia"], provider.ProviderCode, "Fixture", "1.0.0", "dotnet-process", "unused.dll",
            new("1.0.0", null, "1.0.0", null), [new(row.CredentialKey, "API key", true, null)], false, []);
        var catalog = new PluginCatalogService(store, db, new([], _root, "3.8.0"));
        Assert.Equal("saved-secret", Assert.Single(await catalog.GetAuthAsync(manifest, default)).Value);
        row.EncryptedValue = "unreadable-current-value";
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.GetAuthAsync(manifest, default));
        var environmentKey = "PRISMEDIA_PLUGIN_" + provider.ProviderCode.Replace('-', '_').ToUpperInvariant() + "_API_KEY";
        try {
            Environment.SetEnvironmentVariable(environmentKey, "environment-secret");
            Assert.Equal("environment-secret", Assert.Single(await catalog.GetAuthAsync(manifest, default)).Value);
        } finally { Environment.SetEnvironmentVariable(environmentKey, null); }
        Assert.Equal("unreadable-current-value", (await db.ProviderCredentials.AsNoTracking().SingleAsync(candidate => candidate.Id == row.Id)).EncryptedValue);
    }

    private static PrismediaDbContext Context() => new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static async Task<ProviderCredentialRow> Seed(PrismediaDbContext db) {
        var provider = new ProviderConfigRow { Id = Guid.NewGuid(), ProviderCode = "fixture-provider", DisplayName = "Fixture provider", ProviderType = ProviderType.ExternalProcess, Enabled = false, CreatedAt = DateTimeOffset.UtcNow };
        var row = new ProviderCredentialRow { Id = Guid.NewGuid(), ProviderConfigId = provider.Id, CredentialKey = "apiKey", EncryptedValue = "legacy-fixture-secret", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.ProviderConfigs.Add(provider); db.ProviderCredentials.Add(row); await db.SaveChangesAsync(); return row;
    }

    private sealed class BeforeUpgrade(Func<Task> before) : DbCommandInterceptor {
        public bool Fired { get; private set; }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) {
            if (!Fired && command.CommandText.Contains("UPDATE provider_credentials", StringComparison.Ordinal)) { Fired = true; await before(); }
            return result;
        }
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
