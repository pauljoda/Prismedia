using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Books;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Tests;

public sealed class EpubBookContentsServiceTests : IDisposable {
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"prismedia-epub-contents-{Guid.NewGuid():N}");

    public EpubBookContentsServiceTests() {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task ProjectsNavigationAndReadingOrderWithoutReadingChapterBodiesIntoTheContract() {
        var bookId = Guid.NewGuid();
        var path = Path.Combine(_tempDirectory, "book.epub");
        await CreateEpubAsync(path);
        await using var db = CreateContext();
        var service = new EpubBookContentsService(
            new FakeEntityFileContentService(bookId, path),
            new EpubBookContentsCache(),
            db,
            new VisibleEntityScope());

        var response = await service.GetAsync(bookId, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Collection(
            response.Items,
            first => {
                Assert.Equal("Chapter One", first.Title);
                Assert.Equal("text/chapter-1.xhtml", first.Location);
                Assert.Equal(0, first.SectionIndex);
                Assert.Equal(0d, first.StartFraction);
                Assert.InRange(first.EndFraction!.Value, 0.01d, 0.99d);
            },
            second => {
                Assert.Equal("Chapter Two", second.Title);
                Assert.Equal("text/chapter-2.xhtml#part", second.Location);
                Assert.Equal(1, second.SectionIndex);
                Assert.Equal(1d, second.EndFraction);
                Assert.True(second.StartFraction > 0d);
            });
    }

    [Fact]
    public async Task RejectsNonEpubSources() {
        var bookId = Guid.NewGuid();
        var path = Path.Combine(_tempDirectory, "book.pdf");
        await File.WriteAllTextAsync(path, "not an epub");
        await using var db = CreateContext();
        var service = new EpubBookContentsService(
            new FakeEntityFileContentService(bookId, path),
            new EpubBookContentsCache(),
            db,
            new VisibleEntityScope());

        var response = await service.GetAsync(bookId, CancellationToken.None);

        Assert.Null(response);
    }

    public void Dispose() {
        if (Directory.Exists(_tempDirectory)) {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"epub-contents-{Guid.NewGuid():N}")
            .Options);

    private sealed class VisibleEntityScope : IEntityVisibilityChecker {
        public Task<bool> IsVisibleAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private static async Task CreateEpubAsync(string path) {
        await using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
        await WriteEntryAsync(archive, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        await WriteEntryAsync(archive, "META-INF/container.xml", """
            <?xml version="1.0"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles>
                <rootfile full-path="OPS/package.opf" media-type="application/oebps-package+xml" />
              </rootfiles>
            </container>
            """);
        await WriteEntryAsync(archive, "OPS/package.opf", """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="book-id">urn:uuid:test-book</dc:identifier>
                <dc:title>Test Book</dc:title>
                <dc:language>en</dc:language>
              </metadata>
              <manifest>
                <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav" />
                <item id="chapter-1" href="text/chapter-1.xhtml" media-type="application/xhtml+xml" />
                <item id="chapter-2" href="text/chapter-2.xhtml" media-type="application/xhtml+xml" />
              </manifest>
              <spine>
                <itemref idref="chapter-1" />
                <itemref idref="chapter-2" />
              </spine>
            </package>
            """);
        await WriteEntryAsync(archive, "OPS/nav.xhtml", """
            <?xml version="1.0" encoding="utf-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
              <head><title>Contents</title></head>
              <body><nav epub:type="toc"><ol>
                <li><a href="text/chapter-1.xhtml">Chapter One</a></li>
                <li><a href="text/chapter-2.xhtml#part">Chapter Two</a></li>
              </ol></nav></body>
            </html>
            """);
        await WriteEntryAsync(archive, "OPS/text/chapter-1.xhtml", """
            <html xmlns="http://www.w3.org/1999/xhtml"><body><p>Short first chapter.</p></body></html>
            """);
        await WriteEntryAsync(archive, "OPS/text/chapter-2.xhtml", """
            <html xmlns="http://www.w3.org/1999/xhtml"><body><h1 id="part">Part</h1><p>A substantially longer second chapter used to produce a distinct section weight.</p></body></html>
            """);
    }

    private static async Task WriteEntryAsync(
        ZipArchive archive,
        string name,
        string contents,
        CompressionLevel compression = CompressionLevel.Optimal) {
        var entry = archive.CreateEntry(name, compression);
        await using var entryStream = entry.Open();
        await using var writer = new StreamWriter(entryStream);
        await writer.WriteAsync(contents);
    }

    private sealed class FakeEntityFileContentService(Guid bookId, string path) : IEntityFileContentService {
        public Task<EntityFileContent?> GetContentAsync(
            Guid entityId,
            string role,
            CancellationToken cancellationToken) =>
            Task.FromResult<EntityFileContent?>(
                entityId == bookId && role == EntityFileRole.Source.ToCode()
                    ? new EntityFileContent(entityId, role, path, MediaContentTypes.Epub)
                    : null);
    }
}
