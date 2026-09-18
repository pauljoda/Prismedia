using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Plugins;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Application.Plugins;
using Prismedia.Application.Integrations;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Integrations;
using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Tests;

public sealed class PluginInstalledVersionTests : IDisposable {
    private const string ProviderId = "version-fixture";
    private readonly string root = Path.Combine(Path.GetTempPath(), "prismedia-plugin-version-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DiscoveringANewerPackageDoesNotActivateItOrChangeCredentialSaving() {
        await using var db = CreateContext();
        var catalog = Catalog(db);
        await WriteAsync("1.0.0");
        await catalog.InstallAsync(ProviderId, default);
        await WriteAsync("2.0.0");

        Assert.Equal("1.0.0", (await catalog.FindProviderAsync(ProviderId, null, default))!.Manifest.Version);
        var listed = Assert.Single(await catalog.ListInstalledProvidersAsync(default));
        Assert.Equal("1.0.0", listed.Version);
        Assert.True(listed.UpdateAvailable);
        Assert.Equal("2.0.0", listed.AvailableVersion);
        Assert.Equal("1.0.0", (await catalog.InstallAsync(ProviderId, default))!.Version);
        await catalog.SaveAuthAsync(ProviderId, new Dictionary<string, string?> { ["apiKey"] = "fixture-secret" }, default);
        Assert.Equal("1.0.0", (await catalog.FindProviderAsync(ProviderId, null, default))!.Manifest.Version);
    }

    [Fact]
    public async Task AnUnavailableInstalledArtifactDoesNotSilentlyRunADifferentVersion() {
        await using var db = CreateContext();
        var catalog = Catalog(db);
        await WriteAsync("1.0.0");
        await catalog.InstallAsync(ProviderId, default);
        await WriteAsync("2.0.0");
        Directory.Delete(Path.Combine(root, "1.0.0"), true);

        Assert.Null(await catalog.FindProviderAsync(ProviderId, null, default));
        var missing = Assert.Single(await catalog.ListInstalledProvidersAsync(default));
        Assert.True(missing.Installed);
        Assert.False(missing.Enabled);
        Assert.Equal("1.0.0", missing.Version);
        Assert.True(missing.UpdateAvailable);
        Assert.Equal("2.0.0", (await catalog.UpdateAsync(ProviderId, default))!.Version);
        Assert.Equal("2.0.0", (await catalog.FindProviderAsync(ProviderId, null, default))!.Manifest.Version);
    }

    [Theory]
    [InlineData(IntegrationTransferPhase.PendingSubmission)]
    [InlineData(IntegrationTransferPhase.SubmissionUncertain)]
    [InlineData(IntegrationTransferPhase.AwaitingRemote)]
    [InlineData(IntegrationTransferPhase.AwaitingAcknowledgement)]
    [InlineData(IntegrationTransferPhase.NeedsReview)]
    public async Task UpdatesWaitForUnfinishedTransfersEvenWhenTheConnectionIsDisabled(IntegrationTransferPhase phase) {
        await using var db = CreateContext();
        var catalog = Catalog(db);
        await WriteAsync("1.0.0");
        await catalog.InstallAsync(ProviderId, default);
        var connection = Connection(); connection.Enabled = false;
        db.IntegrationConnections.Add(connection);
        var transfer = new IntegrationTransferRow { Id = Guid.NewGuid(), ConnectionId = connection.Id, Phase = phase };
        db.IntegrationTransfers.Add(transfer);
        await db.SaveChangesAsync();
        await WriteAsync("2.0.0");

        await Assert.ThrowsAsync<PluginInUseException>(() => catalog.UpdateAsync(ProviderId, default));
        Assert.Equal("1.0.0", (await catalog.FindProviderAsync(ProviderId, null, default))!.Manifest.Version);
        transfer.Phase = IntegrationTransferPhase.Completed;
        await db.SaveChangesAsync();
        Assert.Equal("2.0.0", (await catalog.UpdateAsync(ProviderId, default))!.Version);
    }

    [Fact]
    public async Task UpdatesAllowAcceptedHoldingObservationButStillBlockActionsAndUnacceptedRequests() {
        await using var db = CreateContext();
        var catalog = Catalog(db);
        await WriteAsync("1.0.0"); await catalog.InstallAsync(ProviderId, default); await WriteAsync("2.0.0");
        var connection = Connection(); db.IntegrationConnections.Add(connection);
        var control = new ManagedControlRow { Id = Guid.NewGuid(), ConnectionId = connection.Id, ActiveHoldingId = Guid.NewGuid() };
        var requestId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var libraryRootId = Guid.NewGuid();
        var requestState = new ManagedRequestState(requestId, connection.Id, entityId, libraryRootId,
            1, ManagedRequestPhase.AwaitingFiles, "accepted-remote-id");
        var request = new ManagedRequestRow { Id = requestId, ConnectionId = connection.Id,
            EntityId = entityId, LibraryRootId = libraryRootId, Revision = 1,
            Phase = ManagedRequestPhase.AwaitingFiles,
            StateJson = JsonSerializer.Serialize(requestState, PluginProcessTransport.JsonOptions) };
        db.ManagedControls.Add(control); db.ManagedRequests.Add(request); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<PluginInUseException>(() => catalog.UpdateAsync(ProviderId, default));
        control.ActiveHoldingId = null; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<PluginInUseException>(() => catalog.UpdateAsync(ProviderId, default));
        db.ManagedHoldings.Add(new() {
            Id = request.Id,
            ConnectionId = connection.Id,
            LibraryRootId = libraryRootId,
            Kind = EntityKind.Movie,
            RemoteId = "accepted-remote-id",
            Title = "Accepted holding",
            Status = ManagedTrackingStatus.WaitingForFiles
        });
        await db.SaveChangesAsync();
        Assert.Equal("2.0.0", (await catalog.UpdateAsync(ProviderId, default))!.Version);
        Assert.Equal(ConnectionStatus.Unverified, connection.Status);
        Assert.Equal(2, connection.Revision);
        Assert.Equal("persistent-instance", connection.RemoteInstanceId);
        Assert.Equal("{}", connection.ProtectedSecretsJson);
    }

    [Fact]
    public async Task RemovalPreservesEnabledConnectionsAndMappedRootsButAllowsInactiveHistory() {
        await using var db = CreateContext();
        var catalog = Catalog(db);
        await WriteAsync("1.0.0"); await catalog.InstallAsync(ProviderId, default);
        var connection = Connection(); db.IntegrationConnections.Add(connection); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<PluginInUseException>(() => catalog.RemoveAsync(ProviderId, default));
        connection.Enabled = false;
        var mount = new ExternalLibraryMountRow { Id = Guid.NewGuid(), ConnectionId = connection.Id };
        db.ExternalLibraryMounts.Add(mount); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<PluginInUseException>(() => catalog.RemoveAsync(ProviderId, default));
        db.ExternalLibraryMounts.Remove(mount);
        db.IntegrationTransfers.Add(new() { Id = Guid.NewGuid(), ConnectionId = connection.Id, Phase = IntegrationTransferPhase.Completed });
        await db.SaveChangesAsync();
        Assert.True(await catalog.RemoveAsync(ProviderId, default));
        Assert.Single(await db.IntegrationConnections.ToArrayAsync());
        Assert.Single(await db.IntegrationTransfers.ToArrayAsync());
    }

    private static IntegrationConnectionRow Connection() => new() { Id = Guid.NewGuid(), PluginId = ProviderId, Name = "Fixture",
        BaseUrl = "https://fixture.test", Enabled = true, Status = ConnectionStatus.Ready, Revision = 1, RemoteInstanceId = "persistent-instance" };

    [Fact]
    public async Task ActivationWaitsForConcurrentAcceptanceAndThenSeesItsUnfinishedTransfer() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var accepted = database.CreateContext();
        await WriteAsync("1.0.0"); await Catalog(accepted).InstallAsync(ProviderId, default); await WriteAsync("2.0.0");
        var connection = Connection(); accepted.IntegrationConnections.Add(connection); await accepted.SaveChangesAsync();
        await using var transaction = await accepted.Database.BeginTransactionAsync();
        await PluginLifecycleLease.LockConnectionAsync(accepted, connection.Id, default);
        accepted.IntegrationTransfers.Add(new() { Id = Guid.NewGuid(), ConnectionId = connection.Id, Phase = IntegrationTransferPhase.AwaitingAcknowledgement });
        await accepted.SaveChangesAsync();

        await using var updating = database.CreateContext();
        var update = Catalog(updating).UpdateAsync(ProviderId, default);
        await Task.Delay(200);
        Assert.False(update.IsCompleted);
        await transaction.CommitAsync();
        await Assert.ThrowsAsync<PluginInUseException>(() => update);
        Assert.Equal("1.0.0", (await Catalog(updating).FindProviderAsync(ProviderId, null, default))!.Manifest.Version);
    }

    [Fact]
    public async Task MissingAllPackageFilesRetainsTheInstalledEntryForRecoveryAndRemoval() {
        await using var db = CreateContext();
        var catalog = Catalog(db);
        await WriteAsync("1.0.0"); await catalog.InstallAsync(ProviderId, default);
        Directory.Delete(Path.Combine(root, "1.0.0"), true);
        var provider = Assert.Single(await catalog.ListInstalledProvidersAsync(default));
        Assert.True(provider.Installed);
        Assert.False(provider.Enabled);
        Assert.Equal("1.0.0", provider.Version);
        Assert.Empty(provider.Supports);
    }

    [Fact]
    public async Task RepairDoesNotDowngradeAnUnavailableInstalledVersion() {
        await using var db = CreateContext();
        var catalog = Catalog(db);
        await WriteAsync("2.0.0"); await catalog.InstallAsync(ProviderId, default); await WriteAsync("1.0.0");
        Directory.Delete(Path.Combine(root, "2.0.0"), true);
        Assert.Null(await catalog.UpdateAsync(ProviderId, default));
        Assert.Equal("2.0.0", Assert.Single(await catalog.ListInstalledProvidersAsync(default)).Version);
    }

    [Fact]
    public async Task SavingCredentialsCommitsInsideTheSharedLifecycleTransaction() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await WriteAsync("1.0.0");
        Assert.True(await Catalog(db).SaveAuthAsync(ProviderId, new Dictionary<string, string?> { ["apiKey"] = "transaction-secret" }, default));
        await using var reread = database.CreateContext();
        Assert.Single(await reread.ProviderConfigs.ToArrayAsync());
        Assert.Single(await reread.ProviderCredentials.ToArrayAsync());
    }

    [Fact]
    public async Task TransferAcceptanceWaitsForActivationAndRejectsItsStaleConnectionProbe() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var activating = database.CreateContext();
        var connection = Connection(); activating.IntegrationConnections.Add(connection); await activating.SaveChangesAsync();
        await using var activation = await PluginLifecycleLease.AcquireAsync(activating, ProviderId, default);
        connection.Status = ConnectionStatus.Unverified; connection.Revision++; await activating.SaveChangesAsync();

        await using var accepting = database.CreateContext();
        var transfer = IntegrationTransfer.CreateSourceDownload(Guid.NewGuid(), connection.Id);
        var plan = new IntegrationTransferPlan("Book", EntityKind.Book, Guid.NewGuid(), Path.GetTempPath(), "fixture-owner", new string('a', 64),
            new(new("fixture", "https://catalog.test/book", EntityKind.Book), "epub"));
        var accept = new EfIntegrationTransferStore(accepting, new(root), new Scheduler()).CreateAsync(transfer, plan, default);
        await Task.Delay(200);
        Assert.False(accept.IsCompleted);
        await activation!.CommitAsync();
        await Assert.ThrowsAsync<ConnectionConflictException>(() => accept);
        Assert.Empty(await accepting.IntegrationTransfers.ToArrayAsync());
    }

    private sealed class Scheduler : IIntegrationTransferScheduler {
        public Task EnqueueAsync(Guid operationId, string title, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private PluginCatalogService Catalog(PrismediaDbContext db) =>
        new(ProviderCredentialTestStore.Create(db), db, new([root], root, "3.8.0"));
    private static PrismediaDbContext CreateContext() => new(new DbContextOptionsBuilder<PrismediaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private async Task WriteAsync(string version) {
        var directory = Path.Combine(root, version);
        Directory.CreateDirectory(directory);
        var manifest = new PluginManifest(2, ["prismedia"], ProviderId, "Version fixture", version,
            DotnetPluginProcessRunner.Code, "fixture.dll", new("2.0.0", null, "1.0.0", null),
            [new("apiKey", "API key", false, null)], false,
            [new(EntityKind.Book.ToCode(), [IdentifyAction.LookupId.ToCode()], [ProviderId])]);
        await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(manifest, PluginProcessTransport.JsonOptions));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
