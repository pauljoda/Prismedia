using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Covers the post-scan hint pass for video/audio imports: external ids stamp onto the entity owning
/// the imported path, the hint is consumed, and the owner's TOP-LEVEL ancestor is reported for the
/// identify kick. Book hints and not-yet-owned paths are left alone.
/// </summary>
public sealed class AcquisitionHintFolderOwnerTests {
    [Fact]
    public async Task StampsIdsOnTheSeasonOwnerAndReportsTheSeries() {
        await using var db = CreateContext();
        var seriesId = AddEntity(db, EntityKindRegistry.VideoSeries.Code, null, "/media/tv/Show (2008)", title: "Show");
        var seasonId = AddEntity(db, EntityKindRegistry.VideoSeason.Code, seriesId, "/media/tv/Show (2008)/S01");
        AddHint(db, "/media/tv/Show (2008)/S01", """{"tmdb":"4242"}""");
        await db.SaveChangesAsync();

        var owners = await new AcquisitionHintApplier(db).ApplyToFolderOwnersAsync(CancellationToken.None);

        var owner = Assert.Single(owners);
        Assert.Equal(seriesId, owner.TopLevelEntityId);
        Assert.Equal(EntityKindRegistry.VideoSeries.Code, owner.TopLevelKindCode);
        Assert.Equal("Show", owner.TopLevelTitle);
        var stamped = Assert.Single(await db.EntityExternalIds.AsNoTracking().Where(row => row.EntityId == seasonId).ToArrayAsync());
        Assert.Equal("tmdb", stamped.Provider);
        Assert.Equal("4242", stamped.Value);
        Assert.True(Assert.Single(await db.AcquisitionImportHints.AsNoTracking().ToArrayAsync()).Consumed);
    }

    [Fact]
    public async Task BroadCheckpointHintStampsTheLinkedEpisodeInsteadOfTheSeriesFolderOwner() {
        await using var db = CreateContext();
        var seriesId = AddEntity(db, EntityKindRegistry.VideoSeries.Code, null, "/media/tv/Show", title: "Show");
        var seasonId = AddEntity(db, EntityKindRegistry.VideoSeason.Code, seriesId, "/media/tv/Show/S01");
        var episodeId = AddEntity(db, EntityKindRegistry.Video.Code, seasonId, "/media/tv/Show/S01/E01.mkv");
        AddHint(db, "/media/tv/Show", """{"tmdb":"episode-4242"}""", episodeId);
        await db.SaveChangesAsync();

        var owners = await new AcquisitionHintApplier(db).ApplyToFolderOwnersAsync(CancellationToken.None);

        Assert.Equal(seriesId, Assert.Single(owners).TopLevelEntityId);
        var stamped = Assert.Single(await db.EntityExternalIds.AsNoTracking().ToArrayAsync());
        Assert.Equal(episodeId, stamped.EntityId);
        Assert.Equal("tmdb", stamped.Provider);
        Assert.Equal("episode-4242", stamped.Value);
        Assert.Empty(await db.EntityExternalIds.AsNoTracking()
            .Where(row => row.EntityId == seriesId || row.EntityId == seasonId)
            .ToArrayAsync());
    }

    [Fact]
    public async Task ScopedApplyIgnoresAStaleHintForAnotherEpisodeInTheSameSeries() {
        await using var db = CreateContext();
        var seriesId = AddEntity(db, EntityKindRegistry.VideoSeries.Code, null, "/media/tv/Show", title: "Show");
        var seasonId = AddEntity(db, EntityKindRegistry.VideoSeason.Code, seriesId, "/media/tv/Show/S01");
        var episodeOneId = AddEntity(db, EntityKindRegistry.Video.Code, seasonId, "/media/tv/Show/S01/E01.mkv");
        var episodeTwoId = AddEntity(db, EntityKindRegistry.Video.Code, seasonId, "/media/tv/Show/S01/E02.mkv");
        var staleAcquisitionId = AddHint(db, "/media/tv/Show", """{"tmdb":"episode-1"}""", episodeOneId);
        var activeAcquisitionId = AddHint(db, "/media/tv/Show", """{"tmdb":"episode-2"}""", episodeTwoId);
        await db.SaveChangesAsync();

        await new AcquisitionHintApplier(db).ApplyToFolderOwnersAsync(
            CancellationToken.None,
            activeAcquisitionId);

        var stamped = Assert.Single(await db.EntityExternalIds.AsNoTracking().ToArrayAsync());
        Assert.Equal(episodeTwoId, stamped.EntityId);
        Assert.Equal("episode-2", stamped.Value);
        var hints = await db.AcquisitionImportHints.AsNoTracking().ToArrayAsync();
        Assert.False(Assert.Single(hints, hint => hint.AcquisitionId == staleAcquisitionId).Consumed);
        Assert.True(Assert.Single(hints, hint => hint.AcquisitionId == activeAcquisitionId).Consumed);
    }

    [Fact]
    public async Task BroadCompleteSeriesHintBindsTheRequestedSeasonOnlyByItsPosition() {
        await using var db = CreateContext();
        var seriesId = AddEntity(db, EntityKindRegistry.VideoSeries.Code, null, "/media/tv/Show", title: "Show");
        var wantedSeasonThreeId = AddWantedEntity(
            db,
            EntityKindRegistry.VideoSeason.Code,
            seriesId,
            sortOrder: 3);
        var acquisitionId = AddHint(
            db,
            "/media/tv/Show",
            """{"tmdb":"season-3"}""",
            wantedSeasonThreeId);
        await db.SaveChangesAsync();
        var hints = new AcquisitionHintApplier(db);

        var directWrongSeason = await hints.BindWantedEntityAsync(
            EntityKind.VideoSeason,
            "/media/tv/Show/S01",
            CancellationToken.None,
            acquisitionId,
            requireExactPath: true);
        var wrongPosition = await hints.BindWantedChildBySortOrderAsync(
            EntityKind.VideoSeason,
            "/media/tv/Show",
            1,
            "/media/tv/Show/S01",
            CancellationToken.None);
        var requestedPosition = await hints.BindWantedChildBySortOrderAsync(
            EntityKind.VideoSeason,
            "/media/tv/Show",
            3,
            "/media/tv/Show/S03",
            CancellationToken.None);

        Assert.False(directWrongSeason);
        Assert.Null(wrongPosition);
        Assert.Equal(wantedSeasonThreeId, requestedPosition);
        var source = await db.EntityFiles.AsNoTracking()
            .SingleAsync(row => row.EntityId == wantedSeasonThreeId && row.Role == EntityFileRole.Source);
        Assert.Equal("/media/tv/Show/S03", source.Path);
    }

    [Fact]
    public async Task StampingDelegatesOneCanonicalIdentitySetAndSkipsTransientLocators() {
        await using var db = CreateContext();
        var seasonId = AddEntity(db, EntityKindRegistry.VideoSeason.Code, null, "/media/tv/Show/S01");
        AddHint(db, "/media/tv/Show/S01", """{" TMDB ":" 4242 ","openlibrary":"https://openlibrary.org/works/OL1W","isbn":"   "}""");
        var hint = Assert.Single(db.AcquisitionImportHints.Local);
        hint.IdentityNamespace = "TMDB";
        hint.IdentityValue = " 4242 ";
        await db.SaveChangesAsync();
        var identities = new RecordingExternalIdentityStore();

        await new AcquisitionHintApplier(db, identities).ApplyToFolderOwnersAsync(CancellationToken.None);

        var write = Assert.Single(identities.Writes);
        Assert.Equal(seasonId, write.EntityId);
        Assert.Equal(ExternalIdentityWriteMode.AddMissing, write.Mode);
        var association = Assert.Single(write.Identities);
        Assert.Equal("tmdb", association.Identity.Namespace);
        Assert.Equal("4242", association.Identity.Value);
    }

    [Fact]
    public async Task AddMissingPreservesAnExistingNamespaceAndAddsNewCanonicalOnes() {
        await using var db = CreateContext();
        var movieId = AddEntity(db, EntityKindRegistry.Movie.Code, null, "/media/movies/Film");
        db.EntityExternalIds.Add(new EntityExternalIdRow {
            Id = Guid.NewGuid(), EntityId = movieId, Provider = "tmdb", Value = "old", CreatedAt = DateTimeOffset.UtcNow
        });
        AddHint(db, "/media/movies/Film", """{" TMDB ":"new"," TVDB ":" 77 "}""");
        await db.SaveChangesAsync();

        await new AcquisitionHintApplier(db).ApplyToFolderOwnersAsync(CancellationToken.None);

        var stamped = await db.EntityExternalIds.AsNoTracking()
            .Where(row => row.EntityId == movieId)
            .OrderBy(row => row.Provider)
            .ToArrayAsync();
        Assert.Collection(stamped,
            row => {
                Assert.Equal("tmdb", row.Provider);
                Assert.Equal("old", row.Value);
            },
            row => {
                Assert.Equal("tvdb", row.Provider);
                Assert.Equal("77", row.Value);
            });
    }

    [Fact]
    public async Task MultipleHintsForOneOwnerDoNotCreateDuplicateIdentityRows() {
        await using var db = CreateContext();
        var seasonId = AddEntity(db, EntityKindRegistry.VideoSeason.Code, null, "/media/tv/Show/S01");
        AddHint(db, "/media/tv/Show/S01/episode-1.mkv", """{"tmdb":"4242"}""");
        AddHint(db, "/media/tv/Show/S01/episode-2.mkv", """{"TMDB":" 4242 "}""");
        await db.SaveChangesAsync();

        await new AcquisitionHintApplier(db).ApplyToFolderOwnersAsync(CancellationToken.None);

        var stamped = Assert.Single(await db.EntityExternalIds.AsNoTracking()
            .Where(row => row.EntityId == seasonId)
            .ToArrayAsync());
        Assert.Equal("tmdb", stamped.Provider);
        Assert.Equal("4242", stamped.Value);
    }

    [Fact]
    public async Task FileHintsResolveThroughTheParentFolderWalk() {
        await using var db = CreateContext();
        var movieId = AddEntity(db, EntityKindRegistry.Movie.Code, null, "/media/movies/Film (2020)", title: "Film");
        AddHint(db, "/media/movies/Film (2020)/Film.mkv", """{"tmdb":"77"}""");
        await db.SaveChangesAsync();

        var owners = await new AcquisitionHintApplier(db).ApplyToFolderOwnersAsync(CancellationToken.None);

        Assert.Equal(movieId, Assert.Single(owners).TopLevelEntityId);
        Assert.Single(await db.EntityExternalIds.AsNoTracking().Where(row => row.EntityId == movieId).ToArrayAsync());
    }

    [Fact]
    public async Task DeepFileAndEntityHierarchiesResolveWithoutArbitraryDepthCaps() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var ids = Enumerable.Range(0, 7).Select(_ => Guid.NewGuid()).ToArray();
        for (var index = 0; index < ids.Length; index++) {
            db.Entities.Add(new EntityRow {
                Id = ids[index],
                ParentEntityId = index == 0 ? null : ids[index - 1],
                KindCode = index == 0
                    ? EntityKindRegistry.VideoSeries.Code
                    : EntityKindRegistry.Video.Code,
                Title = index == 0 ? "Deep series" : $"Level {index}",
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = ids[^1],
            Role = EntityFileRole.Source,
            Path = "/media/deep",
            CreatedAt = now,
            UpdatedAt = now
        });
        AddHint(db, "/media/deep/a/b/c/d/e/episode.mkv", """{"tmdb":"deep-1"}""");
        await db.SaveChangesAsync();

        var owners = await new AcquisitionHintApplier(db)
            .ApplyToFolderOwnersAsync(CancellationToken.None);

        var owner = Assert.Single(owners);
        Assert.Equal(ids[0], owner.TopLevelEntityId);
        Assert.Equal("Deep series", owner.TopLevelTitle);
        Assert.Equal(
            ids[^1],
            (await db.EntityExternalIds.AsNoTracking().SingleAsync()).EntityId);
    }

    [Fact]
    public async Task BookOwnersAndUnownedPathsAreLeftForTheirOwnPasses() {
        await using var db = CreateContext();
        AddEntity(db, EntityKindRegistry.Book.Code, null, "/media/books/Novel.epub");
        AddHint(db, "/media/books/Novel.epub", """{"openlibrary":"OL1"}""");
        AddHint(db, "/media/tv/Not Scanned Yet/S01", """{"tmdb":"1"}""");
        await db.SaveChangesAsync();

        var owners = await new AcquisitionHintApplier(db).ApplyToFolderOwnersAsync(CancellationToken.None);

        Assert.Empty(owners);
        Assert.All(await db.AcquisitionImportHints.AsNoTracking().ToArrayAsync(), hint => Assert.False(hint.Consumed));
        Assert.Empty(await db.EntityExternalIds.AsNoTracking().ToArrayAsync());
    }

    private static Guid AddEntity(PrismediaDbContext db, string kindCode, Guid? parent, string sourcePath, string? title = null) {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = id, KindCode = kindCode, Title = title ?? kindCode, ParentEntityId = parent, CreatedAt = now, UpdatedAt = now
        });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(), EntityId = id, Role = EntityFileRole.Source, Path = sourcePath, CreatedAt = now, UpdatedAt = now
        });
        return id;
    }

    private static Guid AddWantedEntity(
        PrismediaDbContext db,
        string kindCode,
        Guid? parent,
        int sortOrder) {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = kindCode,
            Title = kindCode,
            ParentEntityId = parent,
            SortOrder = sortOrder,
            IsWanted = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
        return id;
    }

    private static Guid AddHint(
        PrismediaDbContext db,
        string sourcePath,
        string externalIdsJson,
        Guid? linkedEntityId = null) {
        var now = DateTimeOffset.UtcNow;
        var acquisitionId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId, Status = AcquisitionStatus.Imported, Title = "T", EntityId = linkedEntityId,
            ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = now, UpdatedAt = now
        });
        db.AcquisitionImportHints.Add(new AcquisitionImportHintRow {
            Id = Guid.NewGuid(), AcquisitionId = acquisitionId, EntityId = linkedEntityId, SourcePath = sourcePath,
            ExternalIdsJson = externalIdsJson, SourceUrlsJson = "[]", Consumed = false, CreatedAt = now, UpdatedAt = now
        });
        return acquisitionId;
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class RecordingExternalIdentityStore : IEntityExternalIdentityStore {
        public List<WriteCall> Writes { get; } = [];

        public Task<IReadOnlyList<EntityExternalId>> ListAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EntityExternalId>>([]);

        public Task<ExternalIdentityResolution> ResolveAsync(
            EntityKind kind,
            IReadOnlyCollection<ExternalIdentity> identities,
            Guid? parentEntityId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ExternalIdentityResolution([]));

        public Task WriteAsync(
            Guid entityId,
            IReadOnlyCollection<EntityExternalId> identities,
            ExternalIdentityWriteMode mode,
            CancellationToken cancellationToken) {
            Writes.Add(new WriteCall(entityId, identities.ToArray(), mode));
            return Task.CompletedTask;
        }
    }

    private sealed record WriteCall(
        Guid EntityId,
        IReadOnlyList<EntityExternalId> Identities,
        ExternalIdentityWriteMode Mode);
}
