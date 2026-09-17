using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Integrations;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Tests;

public sealed class EntityExternalLibraryProvenanceTests {
    [Fact]
    public async Task ProjectsSavedMappedLibraryThroughNearestEffectiveRootWhileNativeEntitiesRemainNative() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await AddExternalLibraryAsync(db, enabled: false, ConnectionStatus.Unavailable);
        var now = DateTimeOffset.UtcNow;
        var externalRoot = AddEntity(db, EntityKind.VideoSeries, "External series", null, now);
        var inheritedChild = AddEntity(db, EntityKind.VideoEpisode, "Inherited episode", externalRoot.Id, now);
        var nativeChild = AddEntity(db, EntityKind.VideoEpisode, "Native episode", externalRoot.Id, now);
        var nativeRoot = new LibraryRootRow {
            Id = Guid.NewGuid(), Path = $"/media/native-{Guid.NewGuid():N}", Label = "Native library",
            Enabled = true, CreatedAt = now, UpdatedAt = now
        };
        db.LibraryRoots.Add(nativeRoot);
        db.EntityLibraryRoots.AddRange(
            new EntityLibraryRootRow { EntityId = externalRoot.Id, LibraryRootId = fixture.LibraryRootId },
            new EntityLibraryRootRow { EntityId = nativeChild.Id, LibraryRootId = nativeRoot.Id });
        await db.SaveChangesAsync();

        var reader = new EfEntityExternalLibraryProvenanceReader(db);
        var direct = await reader.ReadAsync(externalRoot.Id, default);
        Assert.NotNull(direct);
        Assert.Equal(fixture.ConnectionId, direct.ConnectionId);
        Assert.Equal("Offline Radarr", direct.ConnectionName);
        Assert.Equal("radarr", direct.PluginId);
        Assert.Equal(fixture.LibraryRootId, direct.LibraryRootId);
        Assert.Equal("External movies", direct.LibraryLabel);
        Assert.Null(direct.Holding);

        var inherited = await reader.ReadAsync(inheritedChild.Id, default);
        Assert.NotNull(inherited);
        Assert.Equal(fixture.LibraryRootId, inherited.LibraryRootId);
        Assert.Null(await reader.ReadAsync(nativeChild.Id, default));

        var native = AddEntity(db, EntityKind.Movie, "Native movie", null, now);
        await db.SaveChangesAsync();
        Assert.Null(await reader.ReadAsync(native.Id, default));
    }

    [Fact]
    public async Task RetainsAnExactReleasedHoldingLinkButNeverInheritsAnotherEntitysLink() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await AddExternalLibraryAsync(db, enabled: false, ConnectionStatus.Unavailable);
        var now = DateTimeOffset.UtcNow;
        var series = AddEntity(db, EntityKind.VideoSeries, "Series", null, now);
        var episode = AddEntity(db, EntityKind.VideoEpisode, "Episode", series.Id, now);
        db.EntityLibraryRoots.Add(new EntityLibraryRootRow { EntityId = series.Id, LibraryRootId = fixture.LibraryRootId });
        var item = new ManagedItemInput(EntityKind.VideoSeries, "series-42", new Dictionary<string, string> { [ExternalIdProviders.Tvdb] = "42" });
        var holdingId = Guid.NewGuid();
        db.ManagedHoldings.Add(new ManagedHoldingRow {
            Id = holdingId,
            ConnectionId = fixture.ConnectionId,
            LibraryRootId = fixture.LibraryRootId,
            Kind = EntityKind.VideoSeries,
            RemoteId = item.RemoteId,
            Title = "Series",
            ItemJson = JsonSerializer.Serialize(item, PluginProcessTransport.JsonOptions),
            SelectionsJson = "[]",
            TargetsJson = JsonSerializer.Serialize(new[] {
                new ManagedTargetBinding(new("episode-1", EntityKind.VideoEpisode, 1, 1, 1), episode.Id)
            }, PluginProcessTransport.JsonOptions),
            Status = ManagedTrackingStatus.Released,
            Revision = 4,
            LastCheckedAt = now,
            NextCheckAt = now,
            ReleasedAt = now,
        });
        await db.SaveChangesAsync();

        var reader = new EfEntityExternalLibraryProvenanceReader(db);
        var child = await reader.ReadAsync(episode.Id, default);
        Assert.NotNull(child);
        var linked = child.Holding;
        Assert.NotNull(linked);
        Assert.Equal(holdingId, linked.HoldingId);
        Assert.Equal(item.EntityKind, linked.Item.EntityKind);
        Assert.Equal(item.RemoteId, linked.Item.RemoteId);
        Assert.Equal(item.ExpectedExternalIds, linked.Item.ExpectedExternalIds);
        Assert.Equal(ManagedTrackingStatus.Released, linked.Status);

        var container = await reader.ReadAsync(series.Id, default);
        Assert.NotNull(container);
        Assert.Null(container.Holding);
    }

    [Fact]
    public async Task InvalidSavedHoldingIdentityOmitsOnlyTheBacklink() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await AddExternalLibraryAsync(db, enabled: true, ConnectionStatus.Ready);
        var now = DateTimeOffset.UtcNow;
        var movie = AddEntity(db, EntityKind.Movie, "Movie", null, now);
        db.EntityLibraryRoots.Add(new EntityLibraryRootRow { EntityId = movie.Id, LibraryRootId = fixture.LibraryRootId });
        db.ManagedHoldings.Add(new ManagedHoldingRow {
            Id = Guid.NewGuid(), ConnectionId = fixture.ConnectionId, LibraryRootId = fixture.LibraryRootId,
            Kind = EntityKind.Movie, RemoteId = "movie-1", Title = "Movie", ItemJson = "{}", SelectionsJson = "[]",
            TargetsJson = JsonSerializer.Serialize(new[] {
                new ManagedTargetBinding(new("movie-1", EntityKind.Movie, null, null, null), movie.Id)
            }, PluginProcessTransport.JsonOptions),
            Status = ManagedTrackingStatus.Tracking, Revision = 1, LastCheckedAt = now, NextCheckAt = now,
        });
        await db.SaveChangesAsync();

        var result = await new EfEntityExternalLibraryProvenanceReader(db).ReadAsync(movie.Id, default);
        Assert.NotNull(result);
        Assert.Equal(fixture.LibraryRootId, result.LibraryRootId);
        Assert.Null(result.Holding);
    }

    private static EntityRow AddEntity(PrismediaDbContext db, EntityKind kind, string title, Guid? parentId, DateTimeOffset now) {
        var row = new EntityRow {
            Id = Guid.NewGuid(), KindCode = kind.ToCode(), Title = title, ParentEntityId = parentId,
            CreatedAt = now, UpdatedAt = now
        };
        db.Entities.Add(row);
        return row;
    }

    private static async Task<(Guid ConnectionId, Guid LibraryRootId)> AddExternalLibraryAsync(
        PrismediaDbContext db,
        bool enabled,
        ConnectionStatus status) {
        var now = DateTimeOffset.UtcNow;
        var connectionId = Guid.NewGuid();
        var libraryRootId = Guid.NewGuid();
        db.IntegrationConnections.Add(new IntegrationConnectionRow {
            Id = connectionId, PluginId = "radarr", Name = "Offline Radarr", BaseUrl = "https://manager.test/",
            Enabled = enabled, Revision = 1, Status = status
        });
        db.LibraryRoots.Add(new LibraryRootRow {
            Id = libraryRootId, Path = $"/media/external-{Guid.NewGuid():N}", Label = "External movies",
            Enabled = true, CreatedAt = now, UpdatedAt = now
        });
        db.ExternalLibraryMounts.Add(new ExternalLibraryMountRow {
            Id = Guid.NewGuid(), ConnectionId = connectionId, LibraryRootId = libraryRootId,
            RemoteRootId = "remote-root", RemotePath = "/remote/movies", LocalPath = "/media/external", CreatedAt = now
        });
        await db.SaveChangesAsync();
        return (connectionId, libraryRootId);
    }
}
