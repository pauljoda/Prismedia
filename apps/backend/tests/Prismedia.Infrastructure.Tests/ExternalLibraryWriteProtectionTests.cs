using Microsoft.EntityFrameworkCore;
using Npgsql;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Files;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Settings;
using Prismedia.Application.Settings;

namespace Prismedia.Infrastructure.Tests;

public sealed class ExternalLibraryWriteProtectionTests : IDisposable {
    private readonly string workspace = Directory.CreateTempSubdirectory("prismedia-external-protection-").FullName;

    [Fact]
    public async Task ProtectedPathsSurviveDisabledConnectionsAndRejectParentAndSymlinkAliases() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await using var source = NpgsqlDataSource.Create(db.Database.GetConnectionString()!);
        var guard = new PostgresLibraryFileMutationGuard(source);
        var external = Directory.CreateDirectory(Path.Combine(workspace, "external")).FullName;
        await AddBoundaryAsync(database, external);
        var alias = Path.Combine(workspace, "alias");
        Directory.CreateSymbolicLink(alias, external);
        foreach (var path in new[] { external, Path.Combine(external, "film.mkv"), workspace, Path.Combine(alias, "film.mkv") }) {
            var failure = await Assert.ThrowsAsync<FileOperationException>(async () => { await using var lease = await guard.EnterAsync([path], default); });
            Assert.Equal(ApiProblemCodes.ReadOnlyLibrary, failure.Code);
        }
        await using var permitted = await guard.EnterAsync([Path.Combine(workspace, "external-sibling", "film.mkv")], default);
    }

    [Fact]
    public async Task FileBrowserAndImportAdaptersPreserveExternalBytesButPermitIndependentCopies() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await using var source = NpgsqlDataSource.Create(db.Database.GetConnectionString()!);
        var guard = new PostgresLibraryFileMutationGuard(source);
        var external = Directory.CreateDirectory(Path.Combine(workspace, "external")).FullName;
        var file = Path.Combine(external, "film.mkv");
        await File.WriteAllTextAsync(file, "externally owned bytes");
        await AddBoundaryAsync(database, external);
        var storage = new LocalManagedFileStorage(guard);
        var root = new FileLibraryRoot(Guid.NewGuid(), workspace, "Fixture", true, true, false, false, false, false);
        var protectedFile = new ResolvedFilePath(root, "external/film.mkv", file);
        var destination = new ResolvedFilePath(root, "copy.mkv", Path.Combine(workspace, "copy.mkv"));
        await Assert.ThrowsAsync<FileOperationException>(() => storage.DeleteAsync(protectedFile, default));
        await Assert.ThrowsAsync<FileOperationException>(() => storage.MoveAsync(protectedFile, destination, default));
        await Assert.ThrowsAsync<FileOperationException>(() => storage.WriteFileAsync(new(root, "external/new.mkv", Path.Combine(external, "new.mkv")), new MemoryStream([1, 2, 3]), default));
        var mover = new ImportFileMover(guard);
        await Assert.ThrowsAsync<FileOperationException>(() => mover.PlaceAsync(new(file, destination.AbsolutePath), ImportMode.Hardlink, default));
        await mover.PlaceAsync(new(file, destination.AbsolutePath), ImportMode.Copy, default);
        Assert.Equal("externally owned bytes", await File.ReadAllTextAsync(file));
        Assert.Equal("externally owned bytes", await File.ReadAllTextAsync(destination.AbsolutePath));
        await Assert.ThrowsAsync<FileOperationException>(() => mover.PlaceAsync(new(destination.AbsolutePath, Path.Combine(external, "incoming.mkv")), ImportMode.Copy, default));
        Assert.Single(Directory.GetFiles(external));
    }

    [Fact]
    public async Task SettingsCannotRetargetOrDeleteProtectedRootButCanDisableScanning() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var external = Directory.CreateDirectory(Path.Combine(workspace, "external")).FullName;
        await AddBoundaryAsync(database, external);
        await using var db = database.CreateContext();
        var settings = new EfSettingsPersistence(db);
        var root = Assert.Single(await settings.ListLibraryRootsAsync(default));
        Assert.True(root.IsReadOnly);
        await Assert.ThrowsAsync<ReadOnlyLibraryException>(() => settings.SaveLibraryRootAsync(root with { Path = workspace }, default));
        await Assert.ThrowsAsync<ReadOnlyLibraryException>(() => settings.DeleteLibraryRootAsync(root.Id, default));
        var changed = await settings.SaveLibraryRootAsync(root with { Enabled = false, Label = "Paused" }, default);
        Assert.True(changed.IsReadOnly);
        Assert.False(changed.Enabled);
        Assert.Equal(external, changed.Path);
        Assert.Single(await db.ExternalLibraryMounts.ToArrayAsync());
    }

    [Fact]
    public async Task DownloadConfigurationCannotClaimAProtectedFolderAfterMapping() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var external = Directory.CreateDirectory(Path.Combine(workspace, "external")).FullName;
        await AddBoundaryAsync(database, external);
        await using var db = database.CreateContext();
        var clients = new EfDownloadClientConfigStore(db);
        await Assert.ThrowsAsync<AcquisitionConfigurationException>(() => clients.SaveAsync(new(null, DownloadClientKind.QBittorrent,
            "Fixture", "http://client.test", null, null, "fixture", false, DownloadDirectory: external), default));
        await Assert.ThrowsAsync<AcquisitionConfigurationException>(() => new EfRemotePathMappingStore(db).SaveAsync(
            new(null, Guid.NewGuid(), "/downloads", external), default));
        Assert.Empty(await db.DownloadClientConfigs.ToArrayAsync());
        Assert.Empty(await db.RemotePathMappings.ToArrayAsync());
    }

    [Fact]
    public async Task MountConfigurationWaitsForActiveMutationAndNestedAdaptersReuseTheLease() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await using var source = NpgsqlDataSource.Create(db.Database.GetConnectionString()!);
        var writerGuard = new PostgresLibraryFileMutationGuard(source);
        var configurationGuard = new PostgresLibraryFileMutationGuard(source);
        var external = Directory.CreateDirectory(Path.Combine(workspace, "external")).FullName;
        var file = Path.Combine(external, "film.mkv");
        await using var outer = await writerGuard.EnterAsync([file], default);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var configure = Task.Run(async () => {
            started.SetResult();
            await using var exclusive = await configurationGuard.EnterConfigurationAsync(default);
            await AddBoundaryAsync(database, external);
        });
        await started.Task;
        await Task.Delay(100);
        Assert.False(configure.IsCompleted);
        // This must not request another database lock behind the exclusive waiter.
        await using (var nested = await writerGuard.EnterAsync([file], default).AsTask().WaitAsync(TimeSpan.FromSeconds(2))) {
            await File.WriteAllTextAsync(file, "finished before external ownership");
        }
        await outer.DisposeAsync();
        await configure.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<FileOperationException>(async () => { await using var lease = await writerGuard.EnterAsync([file], default); });
        Assert.Equal("finished before external ownership", await File.ReadAllTextAsync(file));
    }

    private static async Task AddBoundaryAsync(PostgresTestDatabase database, string path) {
        await using var db = database.CreateContext();
        var connectionId = Guid.NewGuid(); var rootId = Guid.NewGuid();
        db.IntegrationConnections.Add(new IntegrationConnectionRow { Id = connectionId, PluginId = "fixture-manager", Name = "Offline manager",
            BaseUrl = "http://manager.test/", Enabled = false, Status = ConnectionStatus.Disabled });
        db.LibraryRoots.Add(new LibraryRootRow { Id = rootId, Path = path, Label = "External library", Enabled = false, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        db.ExternalLibraryMounts.Add(new ExternalLibraryMountRow { Id = Guid.NewGuid(), ConnectionId = connectionId, LibraryRootId = rootId,
            RemoteRootId = "1", RemotePath = "/remote/library", LocalPath = path, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }
    public void Dispose() => Directory.Delete(workspace, recursive: true);
}
