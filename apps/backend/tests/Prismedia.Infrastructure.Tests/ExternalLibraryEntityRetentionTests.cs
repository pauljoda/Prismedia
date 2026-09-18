using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Persistence;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class ExternalLibraryEntityRetentionTests {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CandidateProtectionFollowsMountedAncestorsAndDescendantsWithoutCrossingIntoSiblings(
        bool postgres) {
        await using var database = postgres ? await PostgresTestDatabase.CreateAsync() : null;
        await using var db = database?.CreateContext() ?? CreateContext();
        var rootId = Guid.NewGuid();
        var mountedId = Guid.NewGuid();
        var nativeChildId = Guid.NewGuid();
        var siblingId = Guid.NewGuid();
        var unrelatedId = Guid.NewGuid();
        var omittedMountedId = Guid.NewGuid();
        var libraryRootId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        db.Entities.AddRange(
            Entity(rootId, "Root", now),
            Entity(mountedId, "Mounted branch", now, rootId),
            Entity(nativeChildId, "Native child", now, mountedId),
            Entity(siblingId, "Native sibling", now, rootId),
            Entity(unrelatedId, "Unrelated", now),
            Entity(omittedMountedId, "Omitted mounted entity", now));
        db.LibraryRoots.Add(new LibraryRootRow {
            Id = libraryRootId,
            Path = "/media/external",
            Label = "External",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.IntegrationConnections.Add(new IntegrationConnectionRow {
            Id = connectionId,
            PluginId = "fixture-manager",
            Name = "Fixture manager",
            BaseUrl = "http://manager.test/"
        });
        db.ExternalLibraryMounts.Add(new ExternalLibraryMountRow {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            LibraryRootId = libraryRootId,
            RemoteRootId = "external",
            RemotePath = "/remote",
            LocalPath = "/media/external",
            CreatedAt = now
        });
        db.EntityLibraryRoots.AddRange(
            new EntityLibraryRootRow { EntityId = mountedId, LibraryRootId = libraryRootId },
            new EntityLibraryRootRow { EntityId = omittedMountedId, LibraryRootId = libraryRootId });
        await db.SaveChangesAsync();

        var candidates = new[] { rootId, mountedId, nativeChildId, siblingId, unrelatedId };
        HashSet<Guid> protectedIds;
        if (postgres) {
            var counter = new QueryCounter();
            await using var queryDb = new PrismediaDbContext(
                new DbContextOptionsBuilder<PrismediaDbContext>()
                    .UseNpgsql(db.Database.GetConnectionString())
                    .AddInterceptors(counter)
                    .Options);
            protectedIds = await ExternalLibraryEntityRetention.ListProtectedCandidateIdsAsync(
                queryDb,
                candidates,
                CancellationToken.None);
            Assert.Equal(1, counter.Reads);
            Assert.Contains("WITH RECURSIVE candidates", counter.LastCommandText, StringComparison.Ordinal);
        } else {
            protectedIds = await ExternalLibraryEntityRetention.ListProtectedCandidateIdsAsync(
                db,
                candidates,
                CancellationToken.None);
        }

        Assert.Equal(
            new[] { rootId, mountedId, nativeChildId }.OrderBy(id => id),
            protectedIds.OrderBy(id => id));
        Assert.DoesNotContain(omittedMountedId, protectedIds);
    }

    private static EntityRow Entity(Guid id, string title, DateTimeOffset now, Guid? parentId = null) =>
        new() {
            Id = id,
            KindCode = EntityKind.VideoSeries.ToCode(),
            Title = title,
            ParentEntityId = parentId,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"external-library-retention-{Guid.NewGuid():N}")
            .Options);

    private sealed class QueryCounter : DbCommandInterceptor {
        internal int Reads { get; private set; }
        internal string LastCommandText { get; private set; } = string.Empty;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default) {
            Reads++;
            LastCommandText = command.CommandText;
            return ValueTask.FromResult(result);
        }
    }
}
