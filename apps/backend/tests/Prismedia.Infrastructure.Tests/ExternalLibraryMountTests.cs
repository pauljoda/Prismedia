using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
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
