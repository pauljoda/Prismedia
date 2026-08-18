using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Locks the rollup-backed visibility predicate on real PostgreSQL: hidden roots hide their
/// directly rooted entities and their inherited structural descendants, rootless taxonomy stays
/// visible, and a descendant-rooted container disappears only when every rooted descendant is
/// hidden. The in-memory <see cref="UserLibraryVisibilityTests"/> suite continues to cover the
/// live fallback predicate.
/// </summary>
public sealed class UserLibraryVisibilityPostgresTests {
    [Fact]
    public async Task RollupVisibilityHidesRestrictedRootsInheritedDescendantsAndFullyHiddenSeries() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var now = DateTimeOffset.UtcNow;

        var grantedRootId = Guid.NewGuid();
        var restrictedRootId = Guid.NewGuid();
        db.LibraryRoots.AddRange(
            new LibraryRootRow { Id = grantedRootId, Path = "/media/granted", Label = "Granted" },
            new LibraryRootRow { Id = restrictedRootId, Path = "/media/restricted", Label = "Restricted" });

        var grantedVideoId = Guid.NewGuid();
        var restrictedVideoId = Guid.NewGuid();
        var tagId = Guid.NewGuid();
        var hiddenSeriesId = Guid.NewGuid();
        var hiddenSeasonId = Guid.NewGuid();
        var hiddenEpisodeId = Guid.NewGuid();
        var mixedSeriesId = Guid.NewGuid();
        var mixedSeasonId = Guid.NewGuid();
        var mixedGrantedEpisodeId = Guid.NewGuid();
        var mixedRestrictedEpisodeId = Guid.NewGuid();
        db.Entities.AddRange(
            Entity(grantedVideoId, EntityKind.Video, "Granted video", null, now),
            Entity(restrictedVideoId, EntityKind.Video, "Restricted video", null, now),
            Entity(tagId, EntityKind.Tag, "Rootless tag", null, now),
            Entity(hiddenSeriesId, EntityKind.VideoSeries, "Hidden series", null, now),
            Entity(hiddenSeasonId, EntityKind.VideoSeason, "Hidden season", hiddenSeriesId, now),
            Entity(hiddenEpisodeId, EntityKind.VideoEpisode, "Hidden episode", hiddenSeasonId, now),
            Entity(mixedSeriesId, EntityKind.VideoSeries, "Mixed series", null, now),
            Entity(mixedSeasonId, EntityKind.VideoSeason, "Mixed season", mixedSeriesId, now),
            Entity(mixedGrantedEpisodeId, EntityKind.VideoEpisode, "Mixed granted episode", mixedSeasonId, now),
            Entity(mixedRestrictedEpisodeId, EntityKind.VideoEpisode, "Mixed restricted episode", mixedSeasonId, now));
        db.EntityLibraryRoots.AddRange(
            new EntityLibraryRootRow { EntityId = grantedVideoId, LibraryRootId = grantedRootId },
            new EntityLibraryRootRow { EntityId = restrictedVideoId, LibraryRootId = restrictedRootId },
            new EntityLibraryRootRow { EntityId = hiddenEpisodeId, LibraryRootId = restrictedRootId },
            new EntityLibraryRootRow { EntityId = mixedGrantedEpisodeId, LibraryRootId = grantedRootId },
            new EntityLibraryRootRow { EntityId = mixedRestrictedEpisodeId, LibraryRootId = restrictedRootId });
        await db.SaveChangesAsync();

        var filter = new EfEntityLibraryVisibilityFilter(db, TestUserContext.Member(grantedRootId));
        Assert.True(await filter.RequiresCurrentUserVisibilityAsync(CancellationToken.None));
        var visibleIds = (await filter.ApplyCurrentUserVisibility(db.Entities.AsNoTracking())
            .Select(entity => entity.Id)
            .ToArrayAsync())
            .ToHashSet();

        // Directly rooted media follows its own root.
        Assert.Contains(grantedVideoId, visibleIds);
        Assert.DoesNotContain(restrictedVideoId, visibleIds);

        // Rootless taxonomy stays visible for everyone.
        Assert.Contains(tagId, visibleIds);

        // Structural descendants inherit the nearest rooted ancestor/descendant context: the
        // hidden episode disappears, and its season and series (whose only rooted descendant is
        // hidden) disappear with it.
        Assert.DoesNotContain(hiddenEpisodeId, visibleIds);
        Assert.DoesNotContain(hiddenSeriesId, visibleIds);
        Assert.DoesNotContain(hiddenSeasonId, visibleIds);

        // A container with any visible rooted descendant remains browsable; only the restricted
        // episode within it is hidden.
        Assert.Contains(mixedSeriesId, visibleIds);
        Assert.Contains(mixedSeasonId, visibleIds);
        Assert.Contains(mixedGrantedEpisodeId, visibleIds);
        Assert.DoesNotContain(mixedRestrictedEpisodeId, visibleIds);
    }

    private static EntityRow Entity(
        Guid id,
        EntityKind kind,
        string title,
        Guid? parentId,
        DateTimeOffset now) =>
        new() {
            Id = id,
            KindCode = kind.ToCode(),
            Title = title,
            ParentEntityId = parentId,
            CreatedAt = now,
            UpdatedAt = now,
        };
}
