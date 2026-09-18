using Microsoft.EntityFrameworkCore;
using Npgsql;
using Prismedia.Application.Files;
using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Files;
using Prismedia.Infrastructure.Integrations;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Settings;

namespace Prismedia.Infrastructure.Tests;

public sealed class ExternalLibraryMountTests : IDisposable {
    private readonly string workspace = Directory.CreateTempSubdirectory("prismedia-mount-").FullName;

    [Fact]
    public async Task CreationProtectsDedicatedRootAndIdempotentReplayDoesNotCreateAnotherLibrary() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var id = Guid.NewGuid();
        db.IntegrationConnections.Add(new IntegrationConnectionRow { Id = id, PluginId = "fixture", Name = "Fixture", BaseUrl = "http://manager.test/", Revision = 1 });
        await db.SaveChangesAsync();
        var store = new EfExternalLibraryMountStore(db, new(Path.Combine(workspace, "data"), Path.Combine(workspace, "cache")), new SettingsSnapshotCache());
        var path = Directory.CreateDirectory(Path.Combine(workspace, "external")).FullName;
        var request = new CreateExternalLibraryMountRequest(EntityKind.Movie, "1", "/movies", path, "External films");
        var mount = await store.CreateAsync(id, 1, request, default);
        Assert.Equal(mount.Id, (await store.CreateAsync(id, 1, request, default)).Id);
        var root = Assert.Single(await db.LibraryRoots.ToArrayAsync());
        Assert.False(root.Enabled);
        Assert.False(root.AutoIdentify);
        Assert.True(root.ScanVideos);
        Assert.Single(await db.ExternalLibraryMounts.ToArrayAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAsync(id, 1, request with { RemoteRootId = "2", LocalPath = workspace }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAsync(id, 1, request with { RemoteRootId = "3", LocalPath = Directory.CreateDirectory(Path.Combine(workspace, "data", "films")).FullName }, default));
    }

    [Fact]
    public async Task ExistingLibraryAttachmentPreservesConfigurationAndMediaIdentityWhileProtectingExactBytes() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await using var source = NpgsqlDataSource.Create(db.Database.GetConnectionString()!);
        var connectionId = Guid.NewGuid(); var rootId = Guid.NewGuid(); var entityId = Guid.NewGuid(); var fileId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var path = Directory.CreateDirectory(Path.Combine(workspace, "existing")).FullName;
        var file = Path.Combine(path, "film.mkv");
        await File.WriteAllBytesAsync(file, [1, 2, 3]);
        db.IntegrationConnections.Add(new IntegrationConnectionRow { Id = connectionId, PluginId = "fixture", Name = "Fixture", BaseUrl = "http://manager.test/", Revision = 3 });
        db.LibraryRoots.Add(new LibraryRootRow { Id = rootId, Path = path, Label = "Existing films", Enabled = true, Recursive = false,
            ScanVideos = true, ScanImages = false, ScanAudio = false, ScanBooks = false, AutoIdentify = true, IsNsfw = true,
            LastScannedAt = now.AddHours(-1), CreatedAt = now.AddDays(-1), UpdatedAt = now });
        db.Entities.Add(new EntityRow { Id = entityId, KindCode = EntityKind.Movie.ToCode(), Title = "Film", CreatedAt = now, UpdatedAt = now });
        db.EntityFiles.Add(new EntityFileRow { Id = fileId, EntityId = entityId, Role = EntityFileRole.Source, Path = file,
            SizeBytes = 3, CreatedAt = now, UpdatedAt = now });
        db.EntityLibraryRoots.Add(new EntityLibraryRootRow { EntityId = entityId, LibraryRootId = rootId });
        await db.SaveChangesAsync();
        var store = new EfExternalLibraryMountStore(db, new(Path.Combine(workspace, "data"), Path.Combine(workspace, "cache")), new SettingsSnapshotCache());
        var request = new AttachExistingExternalLibraryMountRequest(EntityKind.Movie, "1", "/movies", rootId, path);

        var firstAttachment = await store.AttachWithResultAsync(connectionId, 3, request, default);
        var replay = await store.AttachWithResultAsync(connectionId, 3, request, default);
        var mount = firstAttachment.Mount;
        Assert.True(firstAttachment.Created);
        Assert.False(replay.Created);
        Assert.Equal(mount.Id, replay.Mount.Id);
        await Assert.ThrowsAsync<ConnectionConflictException>(() =>
            store.AttachAsync(connectionId, 3, request with { ExistingLibraryRootId = Guid.NewGuid() }, default));
        db.ChangeTracker.Clear();

        var root = Assert.Single(await db.LibraryRoots.ToArrayAsync());
        Assert.Equal(rootId, root.Id); Assert.Equal("Existing films", root.Label); Assert.True(root.Enabled);
        Assert.False(root.Recursive); Assert.True(root.ScanVideos); Assert.True(root.AutoIdentify); Assert.True(root.IsNsfw);
        Assert.Equal(entityId, Assert.Single(await db.Entities.ToArrayAsync()).Id);
        Assert.Equal(fileId, Assert.Single(await db.EntityFiles.ToArrayAsync()).Id);
        Assert.Equal(rootId, Assert.Single(await db.EntityLibraryRoots.ToArrayAsync()).LibraryRootId);
        Assert.True(Assert.Single(await new EfSettingsPersistence(db).ListLibraryRootsAsync(default)).IsReadOnly);
        var evidence = Assert.Single(await store.InspectAsync(connectionId,
            [new("file-1", "/movies/film.mkv", 3, null, [new("movie-1", EntityKind.Movie, "Film")])], default));
        Assert.Equal(rootId, evidence.LibraryRootId); Assert.Equal(Path.Combine(mount.LocalPath, "film.mkv"), evidence.LocalPath);
        Assert.True(evidence.IsReadable); Assert.True(evidence.SizeMatches);
        var guard = new PostgresLibraryFileMutationGuard(source);
        var failure = await Assert.ThrowsAsync<FileOperationException>(async () => { await using var lease = await guard.EnterAsync([file], default); });
        Assert.Equal(ApiProblemCodes.ReadOnlyLibrary, failure.Code);
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(file));
    }

    [Fact]
    public async Task ExistingLibraryAttachmentRejectsStaleWrongNestedOrAlreadyManagedRoots() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var connectionId = Guid.NewGuid(); var rootId = Guid.NewGuid();
        var path = Directory.CreateDirectory(Path.Combine(workspace, "library")).FullName;
        var nested = Directory.CreateDirectory(Path.Combine(path, "nested")).FullName;
        var other = Directory.CreateDirectory(Path.Combine(workspace, "other")).FullName;
        db.IntegrationConnections.Add(new IntegrationConnectionRow { Id = connectionId, PluginId = "fixture", Name = "Fixture", BaseUrl = "http://manager.test/", Revision = 1 });
        db.LibraryRoots.AddRange(
            new LibraryRootRow { Id = rootId, Path = path, Label = "Films", ScanVideos = true },
            new LibraryRootRow { Id = Guid.NewGuid(), Path = nested, Label = "Nested", ScanVideos = true });
        await db.SaveChangesAsync();
        var store = new EfExternalLibraryMountStore(db, new(Path.Combine(workspace, "data"), Path.Combine(workspace, "cache")), new SettingsSnapshotCache());
        var request = new AttachExistingExternalLibraryMountRequest(EntityKind.Movie, "1", "/movies", rootId, path);

        await Assert.ThrowsAsync<ConnectionConflictException>(() => store.AttachAsync(connectionId, 0, request, default));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AttachAsync(connectionId, 1, request with { ExpectedLocalPath = other }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AttachAsync(connectionId, 1, request with { EntityKind = EntityKind.Book }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AttachAsync(connectionId, 1, request, default));
        var nestedRoot = await db.LibraryRoots.SingleAsync(root => root.Id != rootId);
        db.LibraryRoots.Remove(nestedRoot); await db.SaveChangesAsync();
        var otherConnection = Guid.NewGuid();
        db.IntegrationConnections.Add(new IntegrationConnectionRow { Id = otherConnection, PluginId = "fixture", Name = "Other", BaseUrl = "http://other.test/", Revision = 1 });
        db.ExternalLibraryMounts.Add(new ExternalLibraryMountRow { Id = Guid.NewGuid(), ConnectionId = otherConnection, LibraryRootId = rootId,
            RemoteRootId = "other", RemotePath = "/films", LocalPath = path, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => store.AttachAsync(connectionId, 1, request, default));
        Assert.Single(await db.LibraryRoots.ToArrayAsync());
    }

    [Fact]
    public async Task ExistingLibraryAttachmentRejectsUnfinishedNativeOwnershipButAllowsCompletedHistory() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var connectionId = Guid.NewGuid(); var rootId = Guid.NewGuid();
        var path = Directory.CreateDirectory(Path.Combine(workspace, "native-work")).FullName;
        var now = DateTimeOffset.UtcNow;
        db.IntegrationConnections.Add(new IntegrationConnectionRow { Id = connectionId, PluginId = "fixture", Name = "Fixture", BaseUrl = "http://manager.test/", Revision = 1 });
        db.LibraryRoots.Add(new LibraryRootRow { Id = rootId, Path = path, Label = "Films", ScanVideos = true });
        var acquisition = new AcquisitionRow { Id = Guid.NewGuid(), Kind = EntityKind.Movie, Title = "Pending film",
            TargetLibraryRootId = rootId, Status = AcquisitionStatus.Pending, CreatedAt = now, UpdatedAt = now };
        db.Acquisitions.Add(acquisition);
        await db.SaveChangesAsync();
        var store = new EfExternalLibraryMountStore(db, new(Path.Combine(workspace, "data"), Path.Combine(workspace, "cache")), new SettingsSnapshotCache());
        var request = new AttachExistingExternalLibraryMountRequest(EntityKind.Movie, "1", "/movies", rootId, path);

        await Assert.ThrowsAsync<ArgumentException>(() => store.AttachAsync(connectionId, 1, request, default));
        acquisition.Status = AcquisitionStatus.Imported;
        acquisition.FinalSourcePath = Path.Combine(path, "historical.mkv");
        db.Monitors.Add(new MonitorRow { Id = Guid.NewGuid(), Kind = EntityKind.Movie, Title = "Paused film", TargetLibraryRootId = rootId,
            Status = MonitorStatus.Paused, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => store.AttachAsync(connectionId, 1, request, default));
        var monitor = await db.Monitors.SingleAsync(); monitor.Status = MonitorStatus.Fulfilled; await db.SaveChangesAsync();
        var mount = await store.AttachAsync(connectionId, 1, request, default);
        Assert.Equal(rootId, mount.LibraryRootId);
        Assert.Equal(AcquisitionStatus.Imported, (await db.Acquisitions.SingleAsync()).Status);
    }

    [Fact]
    public async Task FileEvidenceRequiresExactContainmentReadableBytesAndMatchingLength() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var id = Guid.NewGuid();
        db.IntegrationConnections.Add(new IntegrationConnectionRow { Id = id, PluginId = "fixture", Name = "Fixture", BaseUrl = "http://manager.test/", Revision = 1 });
        await db.SaveChangesAsync();
        var store = new EfExternalLibraryMountStore(db, new(Path.Combine(workspace, "data"), Path.Combine(workspace, "cache")), new SettingsSnapshotCache());
        var path = Directory.CreateDirectory(Path.Combine(workspace, "external")).FullName;
        await store.CreateAsync(id, 1, new(EntityKind.Movie, "1", "/movies", path, "Films"), default);
        await File.WriteAllBytesAsync(Path.Combine(path, "film.mkv"), [1, 2, 3]);
        var outside = Path.Combine(workspace, "outside.mkv");
        await File.WriteAllBytesAsync(outside, [1, 2, 3]);
        File.CreateSymbolicLink(Path.Combine(path, "escape.mkv"), outside);
        ManagedLibraryFile FileAt(string remote, long size = 3) => new(remote, remote, size, null, [new("1", EntityKind.Movie, "Film")]);
        var evidence = await store.InspectAsync(id, [FileAt("/movies/film.mkv"), FileAt("/movies/film.mkv", 4), FileAt("/movies/missing.mkv"),
            FileAt("/movies/escape.mkv"), FileAt("/movies/../outside.mkv"), FileAt("/movies-other/film.mkv")], default);
        Assert.True(evidence[0].IsReadable && evidence[0].SizeMatches);
        Assert.True(evidence[1].IsReadable); Assert.False(evidence[1].SizeMatches);
        Assert.All(evidence.Skip(2), file => Assert.False(file.IsReadable));
        Assert.Null(evidence[5].LibraryRootId);
    }

    [Theory]
    [InlineData("C:\\Films", "c:\\films\\Title\\film.mkv", "Title/film.mkv")]
    [InlineData("\\\\server\\share", "\\\\SERVER\\share\\film.mkv", "film.mkv")]
    [InlineData("/Films", "/films/film.mkv", null)]
    [InlineData("/films", "/films-extra/film.mkv", null)]
    public void TranslationHonorsRemotePlatformAndSegmentBoundaries(string remote, string file, string? expected) {
        var local = ExternalLibraryPaths.Resolve(remote, workspace, file);
        Assert.Equal(expected is null ? null : Path.Combine(workspace, expected.Replace('/', Path.DirectorySeparatorChar)), local);
    }

    [Theory]
    [InlineData("/films/../outside.mkv")]
    [InlineData("/films/title\\file.mkv")]
    [InlineData("/films/title/./file.mkv")]
    public void AmbiguousOrTraversingRemotePathsAreRejected(string file) =>
        Assert.Throws<ArgumentException>(() => ExternalLibraryPaths.Resolve("/films", workspace, file));

    public void Dispose() => Directory.Delete(workspace, true);
}
