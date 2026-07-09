using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Prismedia.Application.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfEntityExternalIdentityStoreTests {
    private static readonly DateTimeOffset InitialTime = DateTimeOffset.Parse("2026-01-02T03:04:05Z");
    private static readonly DateTimeOffset WriteTime = DateTimeOffset.Parse("2026-07-09T18:30:00Z");

    [Fact]
    public async Task ListAsyncReturnsCanonicalIdentitiesInStableOrder() {
        await using var db = CreateContext();
        var entityId = AddEntity(db, EntityKind.Movie, "The Matrix");
        AddIdentity(db, entityId, " TmDb ", " 603 ", InitialTime.AddMinutes(1));
        AddIdentity(db, entityId, "IMDB", " tt0133093 ", InitialTime);
        await db.SaveChangesAsync();

        var identities = await Store(db).ListAsync(entityId, CancellationToken.None);

        Assert.Collection(
            identities,
            identity => Assert.Equal(new ExternalIdentity("imdb", "tt0133093"), identity.Identity),
            identity => Assert.Equal(new ExternalIdentity("tmdb", "603"), identity.Identity));
    }

    [Fact]
    public async Task ResolveAsyncScopesTheSameIdentityByEntityKind() {
        await using var db = CreateContext();
        var movieId = AddEntity(db, EntityKind.Movie, "Movie");
        var seriesId = AddEntity(db, EntityKind.VideoSeries, "Series");
        AddIdentity(db, movieId, "tmdb", "42", InitialTime);
        AddIdentity(db, seriesId, "tmdb", "42", InitialTime);
        await db.SaveChangesAsync();

        var resolution = await Store(db).ResolveAsync(
            EntityKind.Movie,
            [new ExternalIdentity("TMDB", " 42 ")],
            parentEntityId: null,
            CancellationToken.None);

        Assert.Equal(ExternalIdentityResolutionStatus.Matched, resolution.Status);
        Assert.Equal(movieId, resolution.EntityId);
        Assert.Equal(movieId, Assert.Single(resolution.Matches).EntityId);
    }

    [Fact]
    public async Task ResolveAsyncPreservesMultipleLocalMatchesAsAmbiguous() {
        await using var db = CreateContext();
        var firstId = AddEntity(db, EntityKind.BookVolume, "Volume 1");
        var secondId = AddEntity(db, EntityKind.BookVolume, "Volume 2");
        AddIdentity(db, firstId, "mangadex", "series-1", InitialTime);
        AddIdentity(db, secondId, "mangadex", "series-1", InitialTime);
        await db.SaveChangesAsync();

        var resolution = await Store(db).ResolveAsync(
            EntityKind.BookVolume,
            [new ExternalIdentity("mangadex", "series-1")],
            parentEntityId: null,
            CancellationToken.None);

        Assert.Equal(ExternalIdentityResolutionStatus.Ambiguous, resolution.Status);
        Assert.Null(resolution.EntityId);
        Assert.Equal(
            new HashSet<Guid> { firstId, secondId },
            resolution.Matches.Select(match => match.EntityId).ToHashSet());
    }

    [Fact]
    public async Task ResolveAsyncConvergesMultipleIdentitiesOnOneEntity() {
        await using var db = CreateContext();
        var entityId = AddEntity(db, EntityKind.Movie, "The Matrix");
        AddIdentity(db, entityId, "tmdb", "603", InitialTime);
        AddIdentity(db, entityId, "imdb", "tt0133093", InitialTime.AddMinutes(1));
        await db.SaveChangesAsync();

        var resolution = await Store(db).ResolveAsync(
            EntityKind.Movie,
            [new ExternalIdentity("tmdb", "603"), new ExternalIdentity("imdb", "tt0133093")],
            parentEntityId: null,
            CancellationToken.None);

        Assert.Equal(ExternalIdentityResolutionStatus.Matched, resolution.Status);
        Assert.Equal(entityId, resolution.EntityId);
        Assert.Equal(2, Assert.Single(resolution.Matches).MatchedIdentities.Count);
    }

    [Fact]
    public async Task ResolveAsyncReportsDifferentIdentityEvidenceAsAmbiguous() {
        await using var db = CreateContext();
        var firstId = AddEntity(db, EntityKind.Book, "First edition");
        var secondId = AddEntity(db, EntityKind.Book, "Second edition");
        AddIdentity(db, firstId, "openlibrary", "OL1W", InitialTime);
        AddIdentity(db, secondId, "isbn-13", "9780000000001", InitialTime);
        await db.SaveChangesAsync();

        var resolution = await Store(db).ResolveAsync(
            EntityKind.Book,
            [new ExternalIdentity("openlibrary", "OL1W"), new ExternalIdentity("isbn-13", "9780000000001")],
            parentEntityId: null,
            CancellationToken.None);

        Assert.Equal(ExternalIdentityResolutionStatus.Ambiguous, resolution.Status);
        Assert.Equal(2, resolution.Matches.Count);
    }

    [Fact]
    public async Task ResolveAsyncHonorsStructuralParentScope() {
        await using var db = CreateContext();
        var firstParentId = AddEntity(db, EntityKind.VideoSeries, "First series");
        var secondParentId = AddEntity(db, EntityKind.VideoSeries, "Second series");
        var firstSeasonId = AddEntity(db, EntityKind.VideoSeason, "Season 1", firstParentId);
        var secondSeasonId = AddEntity(db, EntityKind.VideoSeason, "Season 1", secondParentId);
        AddIdentity(db, firstSeasonId, "tmdb", "season-1", InitialTime);
        AddIdentity(db, secondSeasonId, "tmdb", "season-1", InitialTime);
        await db.SaveChangesAsync();

        var resolution = await Store(db).ResolveAsync(
            EntityKind.VideoSeason,
            [new ExternalIdentity("tmdb", "season-1")],
            firstParentId,
            CancellationToken.None);

        Assert.Equal(ExternalIdentityResolutionStatus.Matched, resolution.Status);
        Assert.Equal(firstSeasonId, resolution.EntityId);
    }

    [Fact]
    public async Task ResolveAsyncIncludesAddedAndModifiedRowsFromTheCurrentUnitOfWork() {
        await using var db = CreateContext();
        var addedEntityId = AddEntity(db, EntityKind.Movie, "Added");
        var modifiedEntityId = AddEntity(db, EntityKind.Movie, "Modified");
        var modified = AddIdentity(db, modifiedEntityId, "tmdb", "before", InitialTime);
        await db.SaveChangesAsync();
        AddIdentity(db, addedEntityId, "tmdb", "added", InitialTime);
        modified.Value = "after";

        var added = await Store(db).ResolveAsync(
            EntityKind.Movie,
            [new ExternalIdentity("tmdb", "added")],
            null,
            CancellationToken.None);
        var changed = await Store(db).ResolveAsync(
            EntityKind.Movie,
            [new ExternalIdentity("tmdb", "after")],
            null,
            CancellationToken.None);
        var stale = await Store(db).ResolveAsync(
            EntityKind.Movie,
            [new ExternalIdentity("tmdb", "before")],
            null,
            CancellationToken.None);

        Assert.Equal(addedEntityId, added.EntityId);
        Assert.Equal(modifiedEntityId, changed.EntityId);
        Assert.Equal(ExternalIdentityResolutionStatus.NotFound, stale.Status);
    }

    [Fact]
    public async Task ResolveAsyncIgnoresDeletedRowsFromTheCurrentUnitOfWork() {
        await using var db = CreateContext();
        var entityId = AddEntity(db, EntityKind.Movie, "Deleted");
        var identity = AddIdentity(db, entityId, "tmdb", "deleted", InitialTime);
        await db.SaveChangesAsync();
        db.EntityExternalIds.Remove(identity);

        var resolution = await Store(db).ResolveAsync(
            EntityKind.Movie,
            [new ExternalIdentity("tmdb", "deleted")],
            null,
            CancellationToken.None);

        Assert.Equal(ExternalIdentityResolutionStatus.NotFound, resolution.Status);
        Assert.Empty(resolution.Matches);
    }

    [Fact]
    public async Task WriteAsyncAddMissingPreservesExistingNamespaceAndNormalizesNewIdentity() {
        await using var db = CreateContext();
        var entityId = AddEntity(db, EntityKind.Movie, "Movie");
        var existing = AddIdentity(db, entityId, "tmdb", "original", InitialTime);
        await db.SaveChangesAsync();

        await Store(db).WriteAsync(
            entityId,
            [
                new EntityExternalId(" TMDB ", " replacement ", null),
                new EntityExternalId(" MusicBrainzRecording ", " recording-1 ", "https://example.test/recording-1")
            ],
            ExternalIdentityWriteMode.AddMissing,
            CancellationToken.None);

        Assert.Equal("original", existing.Value);
        var added = Assert.Single(db.EntityExternalIds.Local, row => row.EntityId == entityId && row.Id != existing.Id);
        Assert.Equal("musicbrainzrecording", added.Provider);
        Assert.Equal("recording-1", added.Value);
        Assert.Equal(EntityState.Added, db.Entry(added).State);
    }

    [Fact]
    public async Task WriteAsyncUpsertPreservesRowIdentityAndCreatedTime() {
        await using var db = CreateContext();
        var entityId = AddEntity(db, EntityKind.Movie, "Movie");
        var existing = AddIdentity(db, entityId, "tmdb", "old", InitialTime);
        var existingRowId = existing.Id;
        await db.SaveChangesAsync();

        await Store(db).WriteAsync(
            entityId,
            [new EntityExternalId("TMDB", "new", "https://example.test/new")],
            ExternalIdentityWriteMode.Upsert,
            CancellationToken.None);

        Assert.Equal(existingRowId, existing.Id);
        Assert.Equal(InitialTime, existing.CreatedAt);
        Assert.Equal(WriteTime, existing.UpdatedAt);
        Assert.Equal("tmdb", existing.Provider);
        Assert.Equal("new", existing.Value);
        Assert.Equal("https://example.test/new", existing.Url);
    }

    [Fact]
    public async Task WriteAsyncReplaceAllRemovesMissingAndDifferentiallyUpdatesExistingRows() {
        await using var db = CreateContext();
        var entityId = AddEntity(db, EntityKind.Movie, "Movie");
        var kept = AddIdentity(db, entityId, "tmdb", "old", InitialTime);
        var removed = AddIdentity(db, entityId, "imdb", "tt-old", InitialTime.AddMinutes(1));
        await db.SaveChangesAsync();

        await Store(db).WriteAsync(
            entityId,
            [
                new EntityExternalId("tmdb", "new", null),
                new EntityExternalId("tvdb", "tv-1", null)
            ],
            ExternalIdentityWriteMode.ReplaceAll,
            CancellationToken.None);

        Assert.Equal(EntityState.Modified, db.Entry(kept).State);
        Assert.Equal("new", kept.Value);
        Assert.Equal(EntityState.Deleted, db.Entry(removed).State);
        var added = Assert.Single(db.EntityExternalIds.Local, row => row.Provider == "tvdb");
        Assert.Equal(EntityState.Added, db.Entry(added).State);
    }

    [Fact]
    public async Task WriteAsyncRejectsConflictingValuesForOneNormalizedNamespace() {
        await using var db = CreateContext();
        var entityId = AddEntity(db, EntityKind.Movie, "Movie");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => Store(db).WriteAsync(
            entityId,
            [
                new EntityExternalId("TMDB", "first", null),
                new EntityExternalId("tmdb", "second", null)
            ],
            ExternalIdentityWriteMode.Upsert,
            CancellationToken.None));

        Assert.Contains("tmdb", exception.Message, StringComparison.Ordinal);
        Assert.Empty(db.EntityExternalIds.Local);
    }

    [Fact]
    public async Task WriteAsyncRejectsAnUnknownReconciliationMode() {
        await using var db = CreateContext();
        var entityId = AddEntity(db, EntityKind.Movie, "Movie");

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Store(db).WriteAsync(
            entityId,
            [],
            (ExternalIdentityWriteMode)999,
            CancellationToken.None));

        Assert.Equal("mode", exception.ParamName);
    }

    [Fact]
    public async Task WriteAsyncLeavesCommitControlWithTheCaller() {
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateOptions(databaseName, databaseRoot);
        var entityId = Guid.NewGuid();
        await using (var setup = new PrismediaDbContext(options)) {
            setup.Entities.Add(NewEntity(entityId, EntityKind.Movie, "Movie", null));
            await setup.SaveChangesAsync();
        }

        await using (var writer = new PrismediaDbContext(options)) {
            await Store(writer).WriteAsync(
                entityId,
                [new EntityExternalId("tmdb", "603", null)],
                ExternalIdentityWriteMode.Upsert,
                CancellationToken.None);
        }

        await using var reader = new PrismediaDbContext(options);
        Assert.Empty(await reader.EntityExternalIds.AsNoTracking().ToArrayAsync());
    }

    private static EfEntityExternalIdentityStore Store(PrismediaDbContext db) =>
        new(db, new FixedTimeProvider(WriteTime));

    private static PrismediaDbContext CreateContext() =>
        new(CreateOptions(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot()));

    private static DbContextOptions<PrismediaDbContext> CreateOptions(
        string databaseName,
        InMemoryDatabaseRoot databaseRoot) =>
        new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;

    private static Guid AddEntity(
        PrismediaDbContext db,
        EntityKind kind,
        string title,
        Guid? parentEntityId = null) {
        var id = Guid.NewGuid();
        db.Entities.Add(NewEntity(id, kind, title, parentEntityId));
        return id;
    }

    private static EntityRow NewEntity(Guid id, EntityKind kind, string title, Guid? parentEntityId) =>
        new() {
            Id = id,
            KindCode = kind.ToCode(),
            Title = title,
            ParentEntityId = parentEntityId,
            CreatedAt = InitialTime,
            UpdatedAt = InitialTime
        };

    private static EntityExternalIdRow AddIdentity(
        PrismediaDbContext db,
        Guid entityId,
        string provider,
        string value,
        DateTimeOffset createdAt) {
        var row = new EntityExternalIdRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Provider = provider,
            Value = value,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
        db.EntityExternalIds.Add(row);
        return row;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
