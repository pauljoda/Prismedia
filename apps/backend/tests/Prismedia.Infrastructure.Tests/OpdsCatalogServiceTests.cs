using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Opds;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Entities.Mappers;
using Prismedia.Infrastructure.Entities.Thumbnails;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Opds;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class OpdsCatalogServiceTests : IDisposable {
    private static readonly Guid VisibleRootId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid DisabledRootId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid VisibleBookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid HiddenBookId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DisabledBookId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SeriesId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid SeriesChildId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid VisibleAuthorId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid HiddenAuthorId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid VisibleTagId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid HiddenTagId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid VisibleCollectionId = Guid.Parse("12121212-1212-1212-1212-121212121212");
    private static readonly Guid HiddenCollectionId = Guid.Parse("13131313-1313-1313-1313-131313131313");
    private static readonly Guid DirectoryComicId = Guid.Parse("14141414-1414-1414-1414-141414141414");
    private static readonly Guid WrappedComicId = Guid.Parse("15151515-1515-1515-1515-151515151515");
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"prismedia-opds-catalog-{Guid.NewGuid():N}");

    public OpdsCatalogServiceTests() {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task CatalogHidesNsfwAndDisabledLibraryMetadataBeforeGrouping() {
        await using var db = CreateContext();
        SeedCatalog(db);
        var service = CreateService(db);

        var visibleCount = await service.CountVisibleBooksAsync(hideNsfw: true, CancellationToken.None);
        var allAllowedCount = await service.CountVisibleBooksAsync(hideNsfw: false, CancellationToken.None);
        var hiddenSearch = await service.SearchBooksAsync("Hidden", hideNsfw: true, new OpdsPageRequest(1, 50), CancellationToken.None);
        var hiddenAllowedSearch = await service.SearchBooksAsync("Hidden", hideNsfw: false, new OpdsPageRequest(1, 50), CancellationToken.None);
        var authors = await service.ListAuthorsAsync(hideNsfw: true, new OpdsPageRequest(1, 50), CancellationToken.None);
        var tags = await service.ListTagsAsync(hideNsfw: true, new OpdsPageRequest(1, 50), CancellationToken.None);
        var collections = await service.ListCollectionsAsync(hideNsfw: true, new OpdsPageRequest(1, 50), CancellationToken.None);
        var series = await service.ListSeriesAsync(hideNsfw: true, new OpdsPageRequest(1, 50), CancellationToken.None);
        var hiddenDownload = await service.GetBookDownloadAsync(HiddenBookId, hideNsfw: true, CancellationToken.None);
        var hiddenAllowedDownload = await service.GetBookDownloadAsync(HiddenBookId, hideNsfw: false, CancellationToken.None);
        var disabledLibrary = await service.ListLibraryBooksAsync(DisabledRootId, hideNsfw: false, new OpdsPageRequest(1, 50), CancellationToken.None);

        Assert.Equal(1, visibleCount);
        Assert.Equal(3, allAllowedCount);
        Assert.Empty(hiddenSearch.Items);
        Assert.Contains(hiddenAllowedSearch.Items, book => book.Id == HiddenBookId);
        Assert.Contains(authors.Items, entry => entry.Id == VisibleAuthorId);
        Assert.DoesNotContain(authors.Items, entry => entry.Id == HiddenAuthorId);
        Assert.Contains(tags.Items, entry => entry.Id == VisibleTagId);
        Assert.DoesNotContain(tags.Items, entry => entry.Id == HiddenTagId);
        Assert.Contains(collections.Items, entry => entry.Id == VisibleCollectionId);
        Assert.DoesNotContain(collections.Items, entry => entry.Id == HiddenCollectionId);
        Assert.DoesNotContain(series.Items, entry => entry.Id == SeriesId);
        Assert.Null(hiddenDownload);
        Assert.NotNull(hiddenAllowedDownload);
        Assert.Null(disabledLibrary);
    }

    [Fact]
    public async Task CatalogMapsBookMimeTypesAndAuthorizedAssetCovers() {
        await using var db = CreateContext();
        SeedCatalog(db);
        var coverPath = Path.Combine(_tempDir, "cache", "book-covers", VisibleBookId.ToString(), "thumb.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(coverPath)!);
        await File.WriteAllTextAsync(coverPath, "cover");
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = VisibleBookId,
            Role = EntityFileRole.Thumbnail,
            Path = AssetPathService.BookCoverThumbnailUrl(VisibleBookId),
            MimeType = MediaContentTypes.ImageJpeg,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.Entities.AddRange(
            Entity(DirectoryComicId, EntityKind.Book.ToCode(), "Comic", false),
            Entity(WrappedComicId, EntityKind.Book.ToCode(), "Wrapped Comic", false));
        db.BookDetails.AddRange(
            new BookDetailRow {
                EntityId = DirectoryComicId,
                BookType = BookType.Comic,
                Format = BookFormat.ImageArchive
            },
            new BookDetailRow {
                EntityId = WrappedComicId,
                BookType = BookType.Comic,
                Format = BookFormat.ImageArchive
            });
        db.EntityLibraryRoots.AddRange(
            RootMembership(DirectoryComicId, VisibleRootId),
            RootMembership(WrappedComicId, VisibleRootId));
        var comicDirectory = Path.Combine(_tempDir, "comic-folder");
        Directory.CreateDirectory(comicDirectory);
        await File.WriteAllTextAsync(Path.Combine(comicDirectory, "001.jpg"), "page");
        var wrappedComicDirectory = Path.Combine(_tempDir, "wrapped-comic");
        Directory.CreateDirectory(wrappedComicDirectory);
        var wrappedComicPath = Path.Combine(wrappedComicDirectory, "wrapped.cbz");
        await File.WriteAllTextAsync(wrappedComicPath, "archive");
        db.EntityFiles.AddRange(
            Source(DirectoryComicId, comicDirectory, null),
            Source(WrappedComicId, wrappedComicDirectory, null));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var recent = await service.ListRecentAsync(hideNsfw: true, new OpdsPageRequest(1, 50), CancellationToken.None);
        var directoryDownload = await service.GetBookDownloadAsync(DirectoryComicId, hideNsfw: true, CancellationToken.None);
        var wrappedDownload = await service.GetBookDownloadAsync(WrappedComicId, hideNsfw: true, CancellationToken.None);
        var cover = await service.GetBookCoverAsync(VisibleBookId, hideNsfw: true, CancellationToken.None);

        Assert.Contains(recent.Items, book => book.Id == VisibleBookId && book.AcquisitionContentType == MediaContentTypes.Epub);
        Assert.Contains(recent.Items, book => book.Id == DirectoryComicId && book.AcquisitionContentType == MediaContentTypes.ComicBookZip);
        Assert.Contains(recent.Items, book => book.Id == WrappedComicId && book.AcquisitionContentType == MediaContentTypes.ComicBookZip);
        Assert.NotNull(directoryDownload);
        Assert.Equal("comic-folder.cbz", directoryDownload.FileName);
        Assert.NotNull(wrappedDownload);
        Assert.Equal(wrappedComicPath, wrappedDownload.Path);
        Assert.Equal("wrapped.cbz", wrappedDownload.FileName);
        Assert.NotNull(cover);
        Assert.Equal(coverPath, cover.Path);
        Assert.Equal(MediaContentTypes.ImageJpeg, cover.ContentType);
    }

    [Fact]
    public async Task AcquisitionFeedsExcludeBookSeriesContainersButSeriesNavigationUsesThem() {
        await using var db = CreateContext();
        SeedCatalog(db);
        var seriesId = Guid.Parse("19191919-1919-1919-1919-191919191919");
        var firstBookId = Guid.Parse("20202020-2020-2020-2020-202020202020");
        var secondBookId = Guid.Parse("21212121-2121-2121-2121-212121212121");
        var seriesDirectory = Path.Combine(_tempDir, "song-series");
        Directory.CreateDirectory(seriesDirectory);
        var firstPath = Path.Combine(seriesDirectory, "first.epub");
        var secondPath = Path.Combine(seriesDirectory, "second.epub");
        await File.WriteAllTextAsync(firstPath, "first");
        await File.WriteAllTextAsync(secondPath, "second");
        db.Entities.AddRange(
            Entity(seriesId, EntityKind.Book.ToCode(), "A Song of Ice and Fire", false),
            Entity(firstBookId, EntityKind.Book.ToCode(), "A Game of Thrones", false, seriesId),
            Entity(secondBookId, EntityKind.Book.ToCode(), "A Clash of Kings", false, seriesId));
        db.BookDetails.AddRange(
            BookDetail(seriesId),
            BookDetail(firstBookId),
            BookDetail(secondBookId));
        db.EntityLibraryRoots.AddRange(
            RootMembership(seriesId, VisibleRootId),
            RootMembership(firstBookId, VisibleRootId),
            RootMembership(secondBookId, VisibleRootId));
        db.EntityFiles.AddRange(
            Source(seriesId, seriesDirectory, null),
            Source(firstBookId, firstPath, MediaContentTypes.Epub),
            Source(secondBookId, secondPath, MediaContentTypes.Epub));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var library = await service.ListLibraryBooksAsync(VisibleRootId, hideNsfw: true, new OpdsPageRequest(1, 50), CancellationToken.None);
        var series = await service.ListSeriesAsync(hideNsfw: true, new OpdsPageRequest(1, 50), CancellationToken.None);
        var seriesBooks = await service.ListSeriesBooksAsync(seriesId, hideNsfw: true, new OpdsPageRequest(1, 50), CancellationToken.None);
        var parentDetail = await service.GetBookAsync(seriesId, hideNsfw: true, CancellationToken.None);
        var parentDownload = await service.GetBookDownloadAsync(seriesId, hideNsfw: true, CancellationToken.None);

        Assert.NotNull(library);
        Assert.DoesNotContain(library.Items, book => book.Id == seriesId);
        Assert.Contains(library.Items, book => book.Id == firstBookId);
        Assert.Contains(library.Items, book => book.Id == secondBookId);
        var seriesEntry = Assert.Single(series.Items, entry => entry.Id == seriesId);
        Assert.Equal(2, seriesEntry.VisibleBookCount);
        Assert.NotNull(seriesBooks);
        Assert.DoesNotContain(seriesBooks.Items, book => book.Id == seriesId);
        Assert.Contains(seriesBooks.Items, book => book.Id == firstBookId);
        Assert.Contains(seriesBooks.Items, book => book.Id == secondBookId);
        Assert.Null(parentDetail);
        Assert.Null(parentDownload);
    }

    [Fact]
    public async Task CatalogUsesSharedThumbnailRepresentativeAsOpdsCover() {
        await using var db = CreateContext();
        SeedCatalog(db);
        var bookId = Guid.Parse("16161616-1616-1616-1616-161616161616");
        var chapterId = Guid.Parse("17171717-1717-1717-1717-171717171717");
        var pageId = Guid.Parse("18181818-1818-1818-1818-181818181818");
        var comicDirectory = Path.Combine(_tempDir, "representative-comic");
        Directory.CreateDirectory(comicDirectory);
        await File.WriteAllTextAsync(Path.Combine(comicDirectory, "001.jpg"), "page");
        var pageThumbPath = Path.Combine(_tempDir, "cache", "book-pages", pageId.ToString(), "thumb.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(pageThumbPath)!);
        await File.WriteAllTextAsync(pageThumbPath, "thumbnail");
        var now = DateTimeOffset.UtcNow;
        var chapter = Entity(chapterId, EntityKind.BookChapter.ToCode(), "Chapter 1", false, bookId);
        chapter.SortOrder = 0;
        var page = Entity(pageId, EntityKind.BookPage.ToCode(), "Page 1", false, chapterId);
        page.SortOrder = 0;
        db.Entities.AddRange(
            Entity(bookId, EntityKind.Book.ToCode(), "Representative Comic", false),
            chapter,
            page);
        db.BookDetails.Add(new BookDetailRow {
            EntityId = bookId,
            BookType = BookType.Comic,
            Format = BookFormat.ImageArchive
        });
        db.EntityLibraryRoots.Add(RootMembership(bookId, VisibleRootId));
        db.EntityFiles.AddRange(
            Source(bookId, comicDirectory, null),
            new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = pageId,
                Role = EntityFileRole.Thumbnail,
                Path = AssetPathService.BookPageThumbnailUrl(pageId),
                MimeType = MediaContentTypes.ImageJpeg,
                CreatedAt = now,
                UpdatedAt = now
            });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var recent = await service.ListRecentAsync(hideNsfw: true, new OpdsPageRequest(1, 50), CancellationToken.None);
        var entry = Assert.Single(recent.Items, book => book.Id == bookId);
        var cover = await service.GetBookCoverAsync(bookId, hideNsfw: true, CancellationToken.None);

        Assert.Equal(MediaContentTypes.ImageJpeg, entry.CoverContentType);
        Assert.Equal(MediaContentTypes.ImageJpeg, entry.ThumbnailContentType);
        Assert.NotNull(cover);
        Assert.Equal(pageThumbPath, cover.Path);
        Assert.Equal(MediaContentTypes.ImageJpeg, cover.ContentType);
    }

    public void Dispose() {
        if (Directory.Exists(_tempDir)) {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private EfOpdsCatalogService CreateService(PrismediaDbContext db) {
        var assets = new AssetPathService(_tempDir, Path.Combine(_tempDir, "cache"));
        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var entityReadService = new EfEntityReadService(
            db,
            TestUserContext.Admin(),
            repository,
            ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db),
            assets);
        return new EfOpdsCatalogService(db, assets, entityReadService, TestUserContext.Admin());
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private void SeedCatalog(PrismediaDbContext db) {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "visible.epub"), "visible");
        File.WriteAllText(Path.Combine(_tempDir, "hidden.epub"), "hidden");
        File.WriteAllText(Path.Combine(_tempDir, "disabled.epub"), "disabled");
        File.WriteAllText(Path.Combine(_tempDir, "series-child.epub"), "series");
        var now = DateTimeOffset.UtcNow;
        db.LibraryRoots.AddRange(
            new LibraryRootRow { Id = VisibleRootId, Label = "Visible", Path = _tempDir, Enabled = true, ScanBooks = true, CreatedAt = now, UpdatedAt = now },
            new LibraryRootRow { Id = DisabledRootId, Label = "Disabled", Path = _tempDir, Enabled = false, ScanBooks = true, CreatedAt = now, UpdatedAt = now });
        db.Entities.AddRange(
            Entity(VisibleBookId, EntityKind.Book.ToCode(), "Visible Book", false),
            Entity(HiddenBookId, EntityKind.Book.ToCode(), "Hidden Book", true),
            Entity(DisabledBookId, EntityKind.Book.ToCode(), "Disabled Book", false),
            Entity(SeriesId, EntityKind.Book.ToCode(), "Hidden Series", true),
            Entity(SeriesChildId, EntityKind.Book.ToCode(), "Series Child", false, SeriesId),
            Entity(VisibleAuthorId, EntityKind.Person.ToCode(), "Visible Author", false),
            Entity(HiddenAuthorId, EntityKind.Person.ToCode(), "Hidden Author", true),
            Entity(VisibleTagId, EntityKind.Tag.ToCode(), "Visible Tag", false),
            Entity(HiddenTagId, EntityKind.Tag.ToCode(), "Hidden Tag", true),
            Entity(VisibleCollectionId, EntityKind.Collection.ToCode(), "Visible Collection", false),
            Entity(HiddenCollectionId, EntityKind.Collection.ToCode(), "Hidden Collection", true));
        db.BookDetails.AddRange(
            BookDetail(VisibleBookId),
            BookDetail(HiddenBookId),
            BookDetail(DisabledBookId),
            BookDetail(SeriesChildId));
        db.EntityLibraryRoots.AddRange(
            RootMembership(VisibleBookId, VisibleRootId),
            RootMembership(HiddenBookId, VisibleRootId),
            RootMembership(DisabledBookId, DisabledRootId),
            RootMembership(SeriesChildId, VisibleRootId));
        db.CollectionDetails.AddRange(
            new CollectionDetailRow { EntityId = VisibleCollectionId, OwnerUserId = TestUserContext.UserId },
            new CollectionDetailRow { EntityId = HiddenCollectionId, OwnerUserId = TestUserContext.UserId });
        db.EntityFiles.AddRange(
            Source(VisibleBookId, Path.Combine(_tempDir, "visible.epub"), MediaContentTypes.Epub),
            Source(HiddenBookId, Path.Combine(_tempDir, "hidden.epub"), MediaContentTypes.Epub),
            Source(DisabledBookId, Path.Combine(_tempDir, "disabled.epub"), MediaContentTypes.Epub),
            Source(SeriesChildId, Path.Combine(_tempDir, "series-child.epub"), MediaContentTypes.Epub));
        db.EntityRelationshipLinks.AddRange(
            Relationship(VisibleBookId, VisibleAuthorId, EntityKind.Person.ToCode(), RelationshipKind.Credits),
            Relationship(HiddenBookId, HiddenAuthorId, EntityKind.Person.ToCode(), RelationshipKind.Credits),
            Relationship(VisibleBookId, VisibleTagId, EntityKind.Tag.ToCode(), RelationshipKind.Tags),
            Relationship(HiddenBookId, HiddenTagId, EntityKind.Tag.ToCode(), RelationshipKind.Tags));
        db.CollectionItemDetails.AddRange(
            new CollectionItemDetailRow { Id = Guid.NewGuid(), CollectionEntityId = VisibleCollectionId, ItemEntityId = VisibleBookId, SortOrder = 0, AddedAt = now },
            new CollectionItemDetailRow { Id = Guid.NewGuid(), CollectionEntityId = HiddenCollectionId, ItemEntityId = HiddenBookId, SortOrder = 0, AddedAt = now });
        db.SaveChanges();
    }

    private static EntityRow Entity(Guid id, string kind, string title, bool isNsfw, Guid? parentId = null) {
        var now = DateTimeOffset.UtcNow;
        return new EntityRow {
            Id = id,
            KindCode = kind,
            Title = title,
            ParentEntityId = parentId,
            IsNsfw = isNsfw,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static BookDetailRow BookDetail(Guid entityId) =>
        new() {
            EntityId = entityId,
            BookType = BookType.Novel,
            Format = BookFormat.Epub
        };

    private static EntityLibraryRootRow RootMembership(Guid entityId, Guid rootId) =>
        new() {
            EntityId = entityId,
            LibraryRootId = rootId
        };

    private static EntityFileRow Source(Guid entityId, string path, string? mimeType) {
        var now = DateTimeOffset.UtcNow;
        return new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Role = EntityFileRole.Source,
            Path = path,
            MimeType = mimeType,
            SizeBytes = 10,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static EntityRelationshipLinkRow Relationship(
        Guid entityId,
        Guid targetId,
        string targetKindCode,
        RelationshipKind relationshipKind) =>
        new() {
            EntityId = entityId,
            TargetEntityId = targetId,
            TargetKindCode = targetKindCode,
            RelationshipCode = relationshipKind.ToCode(),
            Label = relationshipKind.ToCode(),
            CreatedAt = DateTimeOffset.UtcNow
        };
}
