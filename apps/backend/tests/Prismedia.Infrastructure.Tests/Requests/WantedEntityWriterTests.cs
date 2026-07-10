using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;
using Prismedia.Infrastructure.Requests;

namespace Prismedia.Infrastructure.Tests.Requests;

/// <summary>
/// Covers the wanted-entity lifecycle behind request commits: skeleton creation (Wanted flag, provider
/// external id, rootless book detail), external-id-first and author-title dedupe, and the cancel-path
/// delete (including pruning an author placeholder that lost its last wanted book).
/// </summary>
public sealed class WantedEntityWriterTests {
    [Fact]
    public async Task EnsureCreatesAWantedBookSkeletonWithProviderIdAndRootlessDetail() {
        await using var db = CreateContext();
        var writer = Writer(db);

        var result = await writer.EnsureAsync(EntityKind.Book, new ExternalIdentity("openlibrary", "W1"), "Elantris", null, matchTitleKindWide: false, CancellationToken.None);

        Assert.True(result.Created);
        Assert.False(result.HasFile);
        var entity = await db.Entities.AsNoTracking().FirstAsync(row => row.Id == result.EntityId);
        Assert.True(entity.IsWanted);
        Assert.Equal(EntityKindRegistry.Book.Code, entity.KindCode);
        var externalId = Assert.Single(await db.EntityExternalIds.AsNoTracking().Where(row => row.EntityId == result.EntityId).ToArrayAsync());
        Assert.Equal(("openlibrary", "W1"), (externalId.Provider, externalId.Value));
        // No library root: root-scoped stale cleanup must never remove a wanted placeholder.
        Assert.Null((await db.BookDetails.AsNoTracking().FirstAsync(row => row.EntityId == result.EntityId)).LibraryRootId);
    }

    [Fact]
    public async Task EnsureReusesAnEntityMatchedByExternalIdAndReportsItsFile() {
        await using var db = CreateContext();
        var entityId = AddEntity(db, EntityKindRegistry.Book.Code, "Elantris", isWanted: false);
        AddExternalId(db, entityId, "openlibrary", "W1");
        AddSourceFile(db, entityId, "/media/books/Elantris.epub");
        await db.SaveChangesAsync();
        var writer = Writer(db);

        var result = await writer.EnsureAsync(EntityKind.Book, new ExternalIdentity("openlibrary", "W1"), "Elantris", null, matchTitleKindWide: false, CancellationToken.None);

        Assert.False(result.Created);
        Assert.True(result.HasFile);
        Assert.Equal(entityId, result.EntityId);
        Assert.Equal(1, await db.Entities.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task MovieRootReportsOnDiskWhenItsSourceFileBelongsToADescendant() {
        await using var db = CreateContext();
        var movieId = AddEntity(db, EntityKindRegistry.Movie.Code, "Dune", isWanted: false);
        var videoId = AddEntity(db, EntityKindRegistry.Video.Code, "Dune", isWanted: false, parentEntityId: movieId);
        AddExternalId(db, movieId, "tmdb", "M1");
        AddSourceFile(db, videoId, "/media/movies/Dune/Dune.mkv");
        await db.SaveChangesAsync();
        var writer = Writer(db);

        var result = await writer.EnsureAsync(
            EntityKind.Movie,
            new ExternalIdentity("tmdb", "M1"),
            "Dune",
            parentEntityId: null,
            matchTitleKindWide: false,
            CancellationToken.None);
        var monitorable = await writer.GetEntityAsync(movieId, CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal(movieId, result.EntityId);
        Assert.True(result.HasFile);
        Assert.True(monitorable?.HasSourceFile);
        Assert.Equal(2, await db.Entities.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task EnsureNormalizesTheExternalIdentityBeforeResolvingAndStamping() {
        await using var db = CreateContext();
        var entityId = AddEntity(db, EntityKindRegistry.Book.Code, "Stored title", isWanted: false);
        AddExternalId(db, entityId, "openlibrary", "W1");
        await db.SaveChangesAsync();

        var result = await Writer(db).EnsureAsync(
            EntityKind.Book,
            new ExternalIdentity(" OpenLibrary ", " W1 "),
            "Different title",
            parentEntityId: null,
            matchTitleKindWide: false,
            CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal(entityId, result.EntityId);
        var identity = Assert.Single(await db.EntityExternalIds.AsNoTracking().ToArrayAsync());
        Assert.Equal(("openlibrary", "W1"), (identity.Provider, identity.Value));
    }

    [Fact]
    public async Task EnsureScopesAnExternalIdentityMatchToTheRequestedParent() {
        await using var db = CreateContext();
        var firstAuthorId = AddEntity(db, EntityKindRegistry.BookAuthor.Code, "First author", isWanted: false);
        var secondAuthorId = AddEntity(db, EntityKindRegistry.BookAuthor.Code, "Second author", isWanted: false);
        var firstBookId = AddEntity(db, EntityKindRegistry.Book.Code, "Same work", isWanted: false, parentEntityId: firstAuthorId);
        var secondBookId = AddEntity(db, EntityKindRegistry.Book.Code, "Same work", isWanted: false, parentEntityId: secondAuthorId);
        AddExternalId(db, firstBookId, "openlibrary", "W1");
        AddExternalId(db, secondBookId, "openlibrary", "W1");
        await db.SaveChangesAsync();

        var result = await Writer(db).EnsureAsync(
            EntityKind.Book,
            new ExternalIdentity("openlibrary", "W1"),
            "Same work",
            secondAuthorId,
            matchTitleKindWide: false,
            CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal(secondBookId, result.EntityId);
    }

    [Fact]
    public async Task EnsureReportsAmbiguousIdentityWithoutFallingBackToTitleOrCreating() {
        await using var db = CreateContext();
        var firstId = AddEntity(db, EntityKindRegistry.BookAuthor.Code, "Matching title", isWanted: false);
        var secondId = AddEntity(db, EntityKindRegistry.BookAuthor.Code, "Other title", isWanted: false);
        AddExternalId(db, firstId, "openlibraryauthor", "A1");
        AddExternalId(db, secondId, "openlibraryauthor", "A1");
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ExternalIdentityAmbiguityException>(() => Writer(db).EnsureAsync(
            EntityKind.BookAuthor,
            new ExternalIdentity("openlibraryauthor", "A1"),
            "Matching title",
            parentEntityId: null,
            matchTitleKindWide: true,
            CancellationToken.None));

        Assert.Equal(EntityKind.BookAuthor, exception.Kind);
        Assert.Equal(new HashSet<Guid> { firstId, secondId }, exception.Matches.Select(match => match.EntityId).ToHashSet());
        Assert.Equal(2, await db.Entities.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task EnsureBindsAnAuthorByTitleAndStampsTheProviderId() {
        // A scanned author folder has no provider ids yet; requesting that author must reuse the
        // existing entity (title match, authors only) and stamp the id for future id-first lookups.
        await using var db = CreateContext();
        var authorId = AddEntity(db, EntityKindRegistry.BookAuthor.Code, "Brandon Sanderson", isWanted: false);
        await db.SaveChangesAsync();
        var writer = Writer(db);

        var result = await writer.EnsureAsync(EntityKind.BookAuthor, new ExternalIdentity(" OpenLibrary ", " A1 "), "brandon sanderson", null, matchTitleKindWide: true, CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal(authorId, result.EntityId);
        var externalId = Assert.Single(await db.EntityExternalIds.AsNoTracking().Where(row => row.EntityId == authorId).ToArrayAsync());
        Assert.Equal(("openlibrary", "A1"), (externalId.Provider, externalId.Value));
    }

    [Fact]
    public async Task EnsureNeverClaimsABookByBareTitleAcrossTheLibrary() {
        await using var db = CreateContext();
        AddEntity(db, EntityKindRegistry.Book.Code, "Common Title", isWanted: false);
        await db.SaveChangesAsync();
        var writer = Writer(db);

        var result = await writer.EnsureAsync(EntityKind.Book, new ExternalIdentity("openlibrary", "W9"), "Common Title", null, matchTitleKindWide: false, CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal(2, await db.Entities.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task EnsureCreatesWantedMusicSkeletonsWithTheirRootlessDetailRows() {
        await using var db = CreateContext();
        var writer = Writer(db);

        var artist = await writer.EnsureAsync(EntityKind.MusicArtist, new ExternalIdentity("musicbrainz", "MB1"), "Daft Punk", null, matchTitleKindWide: true, CancellationToken.None);
        var album = await writer.EnsureAsync(EntityKind.AudioLibrary, new ExternalIdentity("musicbrainz", "R1"), "Discovery", artist.EntityId, matchTitleKindWide: false, CancellationToken.None);

        Assert.Null((await db.MusicArtistDetails.AsNoTracking().FirstAsync(row => row.EntityId == artist.EntityId)).LibraryRootId);
        Assert.Null((await db.AudioLibraryDetails.AsNoTracking().FirstAsync(row => row.EntityId == album.EntityId)).LibraryRootId);
        Assert.Equal(artist.EntityId, (await db.Entities.AsNoTracking().FirstAsync(row => row.Id == album.EntityId)).ParentEntityId);
    }

    [Fact]
    public async Task EnsureCreatesAWantedMovieWithoutADetailRow() {
        await using var db = CreateContext();

        var movie = await Writer(db).EnsureAsync(EntityKind.Movie, new ExternalIdentity("tmdb", "M1"), "Dune", null, matchTitleKindWide: false, CancellationToken.None);

        var entity = await db.Entities.AsNoTracking().FirstAsync(row => row.Id == movie.EntityId);
        Assert.True(entity.IsWanted);
        Assert.Equal(EntityKindRegistry.Movie.Code, entity.KindCode);
    }

    [Fact]
    public async Task GetContainerReturnsCanonicalMonitorableIdentities() {
        await using var db = CreateContext();
        var authorId = AddEntity(db, EntityKindRegistry.BookAuthor.Code, "Author", isWanted: false);
        AddExternalId(db, authorId, "OpenLibraryWork", " W1 ");
        await db.SaveChangesAsync();

        var container = await Writer(db).GetEntityAsync(authorId, CancellationToken.None);

        var identity = Assert.Single(container!.ExternalIdentities);
        Assert.Equal(("openlibrarywork", "W1"), (identity.Namespace, identity.Value));
    }

    [Fact]
    public async Task DeleteIfWantedPrunesAnOrphanedWantedArtistLikeAnAuthor() {
        await using var db = CreateContext();
        var artistId = AddEntity(db, EntityKindRegistry.MusicArtist.Code, "Artist", isWanted: true);
        var albumId = AddEntity(db, EntityKindRegistry.AudioLibrary.Code, "Album", isWanted: true, parentEntityId: artistId);
        await db.SaveChangesAsync();

        Assert.True(await Writer(db).DeleteIfWantedAsync(albumId, CancellationToken.None));

        Assert.Empty(await db.Entities.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task DeleteIfWantedRemovesTheWantedBookAndPrunesItsOrphanedWantedAuthor() {
        await using var db = CreateContext();
        var authorId = AddEntity(db, EntityKindRegistry.BookAuthor.Code, "Author", isWanted: true);
        var bookId = AddEntity(db, EntityKindRegistry.Book.Code, "Book", isWanted: true, parentEntityId: authorId);
        await db.SaveChangesAsync();

        Assert.True(await Writer(db).DeleteIfWantedAsync(bookId, CancellationToken.None));

        Assert.Empty(await db.Entities.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task DeleteIfWantedKeepsAnAuthorThatStillHasOtherChildren() {
        await using var db = CreateContext();
        var authorId = AddEntity(db, EntityKindRegistry.BookAuthor.Code, "Author", isWanted: true);
        var bookId = AddEntity(db, EntityKindRegistry.Book.Code, "Book 1", isWanted: true, parentEntityId: authorId);
        AddEntity(db, EntityKindRegistry.Book.Code, "Book 2", isWanted: true, parentEntityId: authorId);
        await db.SaveChangesAsync();

        Assert.True(await Writer(db).DeleteIfWantedAsync(bookId, CancellationToken.None));

        Assert.Equal(2, await db.Entities.AsNoTracking().CountAsync()); // author + remaining book
    }

    [Fact]
    public async Task DeleteIfWantedRefusesAnImportedOrNonWantedEntity() {
        await using var db = CreateContext();
        var importedId = AddEntity(db, EntityKindRegistry.Book.Code, "Imported", isWanted: true);
        AddSourceFile(db, importedId, "/media/books/Imported.epub"); // the import won the race
        var ordinaryId = AddEntity(db, EntityKindRegistry.Book.Code, "Ordinary", isWanted: false);
        await db.SaveChangesAsync();
        var writer = Writer(db);

        Assert.False(await writer.DeleteIfWantedAsync(importedId, CancellationToken.None));
        Assert.False(await writer.DeleteIfWantedAsync(ordinaryId, CancellationToken.None));
        Assert.Equal(2, await db.Entities.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task BindProviderIdentitySelectsTheExactPluginWhenTwoPluginsShareANamespace() {
        await using var db = CreateContext();
        var entityId = AddEntity(db, EntityKindRegistry.Book.Code, "Owned title", isWanted: false);
        var identity = new ExternalIdentity("shared-books", "W1");
        AddExternalId(db, entityId, identity.Namespace, identity.Value);
        await db.SaveChangesAsync();
        var router = new ConfiguredIdentityRouter(
            new PluginIdentityRoute("alpha-books", identity),
            new PluginIdentityRoute("zeta-books", identity));

        var bound = await Writer(db, router).BindProviderIdentityAsync(
            entityId,
            new PluginIdentityRoute("zeta-books", identity),
            CancellationToken.None);

        Assert.True(bound);
        var persisted = Assert.Single(await db.EntityProviderIdentities.AsNoTracking().ToArrayAsync());
        Assert.Equal(("zeta-books", identity.Namespace, identity.Value),
            (persisted.PluginId, persisted.IdentityNamespace, persisted.IdentityValue));
        var entity = await db.Entities.AsNoTracking().SingleAsync(row => row.Id == entityId);
        Assert.Equal("Owned title", entity.Title);
        Assert.False(entity.IsWanted);
    }

    [Fact]
    public async Task BindProviderIdentityRejectsUnroutableOrUnownedIdentityWithoutPersisting() {
        await using var db = CreateContext();
        var entityId = AddEntity(db, EntityKindRegistry.Book.Code, "Owned title", isWanted: false);
        var ownedIdentity = new ExternalIdentity("shared-books", "W1");
        var foreignIdentity = new ExternalIdentity("shared-books", "W2");
        AddExternalId(db, entityId, ownedIdentity.Namespace, ownedIdentity.Value);
        await db.SaveChangesAsync();
        var writer = Writer(
            db,
            new ConfiguredIdentityRouter(new PluginIdentityRoute("alpha-books", ownedIdentity)));

        Assert.False(await writer.BindProviderIdentityAsync(
            entityId,
            new PluginIdentityRoute("untrusted-books", ownedIdentity),
            CancellationToken.None));
        Assert.False(await writer.BindProviderIdentityAsync(
            entityId,
            new PluginIdentityRoute("alpha-books", foreignIdentity),
            CancellationToken.None));
        Assert.Empty(await db.EntityProviderIdentities.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task GetContainerCarriesAStalePersistedProviderBindingWithoutRetargeting() {
        await using var db = CreateContext();
        var entityId = AddEntity(db, EntityKindRegistry.VideoSeries.Code, "Series", isWanted: false);
        AddExternalId(db, entityId, "tmdb", "82728");
        await db.SaveChangesAsync();
        var bindings = new EfEntityProviderIdentityStore(db, TimeProvider.System);
        await bindings.SetAsync(
            entityId,
            "tmdb",
            new ExternalIdentity("tmdb", "82728"),
            CancellationToken.None);
        await db.SaveChangesAsync();

        var rawIdentity = await db.EntityExternalIds.SingleAsync(row => row.EntityId == entityId);
        rawIdentity.Value = "99999";
        await db.SaveChangesAsync();
        var router = new ConfiguredIdentityRouter(
            new PluginIdentityRoute("replacement-plugin", new ExternalIdentity("tmdb", "99999")));

        var container = await Writer(db, router).GetEntityAsync(entityId, CancellationToken.None);

        Assert.NotNull(container);
        Assert.Equal(
            new PluginIdentityRoute("tmdb", new ExternalIdentity("tmdb", "82728")),
            container.ProviderIdentity);
        Assert.Equal([new ExternalIdentity("tmdb", "99999")], container.ExternalIdentities);
        Assert.Equal(0, router.CallCount);
    }

    private static WantedEntityWriter Writer(
        PrismediaDbContext db,
        IPluginIdentityRouter? identityRouter = null) =>
        new(
            db,
            new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(Path.GetTempPath())),
            new EfEntityExternalIdentityStore(db, TimeProvider.System),
            new EfEntityProviderIdentityStore(db, TimeProvider.System),
            identityRouter ?? new EmptyIdentityRouter(),
            new EfEntityHierarchyReader(db));

    private sealed class EmptyIdentityRouter : IPluginIdentityRouter {
        public Task<IReadOnlyList<PluginIdentityRoute>> ResolveAsync(
            string entityKindCode,
            IdentifyAction action,
            IReadOnlyList<ExternalIdentity> identities,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PluginIdentityRoute>>([]);
    }

    private sealed class ConfiguredIdentityRouter(params PluginIdentityRoute[] routes) : IPluginIdentityRouter {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<PluginIdentityRoute>> ResolveAsync(
            string entityKindCode,
            IdentifyAction action,
            IReadOnlyList<ExternalIdentity> identities,
            CancellationToken cancellationToken) {
            CallCount++;
            var requested = identities.ToHashSet();
            return Task.FromResult<IReadOnlyList<PluginIdentityRoute>>(
                routes.Where(route => requested.Contains(route.Identity)).ToArray());
        }
    }

    private static Guid AddEntity(PrismediaDbContext db, string kindCode, string title, bool isWanted, Guid? parentEntityId = null) {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = id, KindCode = kindCode, Title = title, ParentEntityId = parentEntityId,
            IsWanted = isWanted, CreatedAt = now, UpdatedAt = now
        });
        return id;
    }

    private static void AddExternalId(PrismediaDbContext db, Guid entityId, string provider, string value) =>
        db.EntityExternalIds.Add(new EntityExternalIdRow {
            Id = Guid.NewGuid(), EntityId = entityId, Provider = provider, Value = value, Url = null, CreatedAt = DateTimeOffset.UtcNow
        });

    private static void AddSourceFile(PrismediaDbContext db, Guid entityId, string path) {
        var now = DateTimeOffset.UtcNow;
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(), EntityId = entityId, Role = EntityFileRole.Source, Path = path, CreatedAt = now, UpdatedAt = now
        });
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
