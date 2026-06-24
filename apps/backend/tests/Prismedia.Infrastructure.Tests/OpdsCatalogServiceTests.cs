using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Opds;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
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
            Entity(DirectoryComicId, EntityKindRegistry.Book.Code, "Comic", false),
            Entity(WrappedComicId, EntityKindRegistry.Book.Code, "Wrapped Comic", false));
        db.BookDetails.AddRange(
            new BookDetailRow {
                EntityId = DirectoryComicId,
                BookType = BookType.Comic,
                Format = BookFormat.ImageArchive,
                LibraryRootId = VisibleRootId
            },
            new BookDetailRow {
                EntityId = WrappedComicId,
                BookType = BookType.Comic,
                Format = BookFormat.ImageArchive,
                LibraryRootId = VisibleRootId
            });
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

    public void Dispose() {
        if (Directory.Exists(_tempDir)) {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private EfOpdsCatalogService CreateService(PrismediaDbContext db) =>
        new(db, new AssetPathService(_tempDir, Path.Combine(_tempDir, "cache")));

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
            Entity(VisibleBookId, EntityKindRegistry.Book.Code, "Visible Book", false),
            Entity(HiddenBookId, EntityKindRegistry.Book.Code, "Hidden Book", true),
            Entity(DisabledBookId, EntityKindRegistry.Book.Code, "Disabled Book", false),
            Entity(SeriesId, EntityKindRegistry.Book.Code, "Hidden Series", true),
            Entity(SeriesChildId, EntityKindRegistry.Book.Code, "Series Child", false, SeriesId),
            Entity(VisibleAuthorId, EntityKindRegistry.Person.Code, "Visible Author", false),
            Entity(HiddenAuthorId, EntityKindRegistry.Person.Code, "Hidden Author", true),
            Entity(VisibleTagId, EntityKindRegistry.Tag.Code, "Visible Tag", false),
            Entity(HiddenTagId, EntityKindRegistry.Tag.Code, "Hidden Tag", true),
            Entity(VisibleCollectionId, EntityKindRegistry.Collection.Code, "Visible Collection", false),
            Entity(HiddenCollectionId, EntityKindRegistry.Collection.Code, "Hidden Collection", true));
        db.BookDetails.AddRange(
            BookDetail(VisibleBookId, VisibleRootId),
            BookDetail(HiddenBookId, VisibleRootId),
            BookDetail(DisabledBookId, DisabledRootId),
            BookDetail(SeriesChildId, VisibleRootId));
        db.EntityFiles.AddRange(
            Source(VisibleBookId, Path.Combine(_tempDir, "visible.epub"), MediaContentTypes.Epub),
            Source(HiddenBookId, Path.Combine(_tempDir, "hidden.epub"), MediaContentTypes.Epub),
            Source(DisabledBookId, Path.Combine(_tempDir, "disabled.epub"), MediaContentTypes.Epub),
            Source(SeriesChildId, Path.Combine(_tempDir, "series-child.epub"), MediaContentTypes.Epub));
        db.EntityRelationshipLinks.AddRange(
            Relationship(VisibleBookId, VisibleAuthorId, EntityKindRegistry.Person.Code, RelationshipKind.Credits),
            Relationship(HiddenBookId, HiddenAuthorId, EntityKindRegistry.Person.Code, RelationshipKind.Credits),
            Relationship(VisibleBookId, VisibleTagId, EntityKindRegistry.Tag.Code, RelationshipKind.Tags),
            Relationship(HiddenBookId, HiddenTagId, EntityKindRegistry.Tag.Code, RelationshipKind.Tags));
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

    private static BookDetailRow BookDetail(Guid entityId, Guid rootId) =>
        new() {
            EntityId = entityId,
            BookType = BookType.Novel,
            Format = BookFormat.Epub,
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
