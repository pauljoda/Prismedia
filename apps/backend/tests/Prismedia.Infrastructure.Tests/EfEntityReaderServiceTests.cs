using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Files;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfEntityReaderServiceTests {
    [Fact]
    public async Task ReadsAnExactOrderedManifestAndResolvesPagesThroughTheEntitySource() {
        await using var db = CreateContext();
        var entityId = AddReadableEntity(db, withSource: true);
        db.EntityPageManifests.Add(Manifest(entityId, "source:v1"));
        db.EntityPageEntries.AddRange(
            Page(entityId, 1, "Story/010.5.png", PageType.Story),
            Page(entityId, 0, "Covers/Front Cover.jpg", PageType.FrontCover));
        await db.SaveChangesAsync();
        var service = new EfEntityReaderService(db, new Visibility(true));

        var manifest = await service.GetManifestAsync(entityId, CancellationToken.None);
        var page = await service.GetPageAsync(entityId, 1, CancellationToken.None);

        Assert.NotNull(manifest);
        Assert.Equal(PageReadingDirection.RightToLeft, manifest.Direction);
        Assert.Equal(ReaderMode.Paged, manifest.DefaultMode);
        Assert.Equal([0, 1], manifest.Pages.Select(item => item.Ordinal));
        Assert.Equal(PageType.FrontCover, manifest.Pages[0].PageType);
        Assert.Equal(
            EntitySourcePath.ArchiveMember("/media/chapter.cbz", "Story/010.5.png"),
            page!.Path);
        Assert.Equal("image/png", page.MimeType);
    }

    [Fact]
    public async Task HiddenOrSourceLessManifestsBehaveAsMissing() {
        await using var db = CreateContext();
        var entityId = AddReadableEntity(db, withSource: false);
        db.EntityPageManifests.Add(Manifest(entityId, "source:v1"));
        db.EntityPageEntries.Add(Page(entityId, 0, "001.jpg", PageType.Story));
        await db.SaveChangesAsync();

        var hidden = new EfEntityReaderService(db, new Visibility(false));
        var sourceLess = new EfEntityReaderService(db, new Visibility(true));

        Assert.Null(await hidden.GetManifestAsync(entityId, CancellationToken.None));
        Assert.Null(await hidden.GetPageAsync(entityId, 0, CancellationToken.None));
        Assert.Null(await sourceLess.GetManifestAsync(entityId, CancellationToken.None));
        Assert.Null(await sourceLess.GetPageAsync(entityId, 0, CancellationToken.None));
        Assert.Null(await sourceLess.GetPageAsync(entityId, -1, CancellationToken.None));
    }

    [Fact]
    public async Task ReplacesChangedManifestsAndSkipsAnUnchangedSourceSignature() {
        await using var db = CreateContext();
        var entityId = AddReadableEntity(db, withSource: true);
        await db.SaveChangesAsync();
        var service = new EfEntityReaderService(db, new Visibility(true));
        var original = DomainManifest(entityId, "source:v1", "001.jpg");
        var changed = DomainManifest(entityId, "source:v2", "chapter/001.png");

        Assert.True(await service.ReplaceAsync(original, CancellationToken.None));
        Assert.False(await service.ReplaceAsync(original, CancellationToken.None));
        Assert.True(await service.ReplaceAsync(changed, CancellationToken.None));

        var header = Assert.Single(db.EntityPageManifests);
        var page = Assert.Single(db.EntityPageEntries);
        Assert.Equal("source:v2", header.SourceSignature);
        Assert.Equal("chapter/001.png", page.ArchiveMember);
        Assert.Equal("image/png", page.MimeType);
        Assert.True(await service.RemoveAsync(entityId, CancellationToken.None));
        Assert.False(await service.RemoveAsync(entityId, CancellationToken.None));
        Assert.Empty(db.EntityPageManifests);
        Assert.Empty(db.EntityPageEntries);
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"entity-reader-{Guid.NewGuid():N}")
            .Options);

    private static Guid AddReadableEntity(PrismediaDbContext db, bool withSource) {
        var entityId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = entityId,
            KindCode = EntityKind.ComicInstallment.ToCode(),
            Title = "Chapter 1",
            CreatedAt = now,
            UpdatedAt = now
        });
        if (withSource) {
            db.EntityFiles.Add(new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                Role = EntityFileRole.Source,
                Path = "/media/chapter.cbz",
                MimeType = "application/vnd.comicbook+zip",
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        return entityId;
    }

    private static EntityPageManifestRow Manifest(Guid entityId, string signature) => new() {
        EntityId = entityId,
        Direction = PageReadingDirection.RightToLeft,
        DefaultMode = ReaderMode.Paged,
        CoverOrdinal = 0,
        SourceSignature = signature,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static EntityPageEntryRow Page(Guid entityId, int ordinal, string member, PageType pageType) => new() {
        EntityId = entityId,
        Ordinal = ordinal,
        ArchiveMember = member,
        MimeType = Path.GetExtension(member).Equals(".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : "image/jpeg",
        Width = 1200,
        Height = 1800,
        PageType = pageType,
        IsDoublePage = false
    };

    private static EntityPageManifest DomainManifest(Guid entityId, string signature, string member) =>
        new(
            entityId,
            PageReadingDirection.RightToLeft,
            ReaderMode.Paged,
            coverOrdinal: 0,
            sourceSignature: signature,
            pages:
            [
                new EntityPageEntry(
                    0,
                    member,
                    Path.GetExtension(member).Equals(".png", StringComparison.OrdinalIgnoreCase)
                        ? "image/png"
                        : "image/jpeg",
                    1200,
                    1800,
                    PageType.Story,
                    false,
                    null)
            ]);

    private sealed class Visibility(bool visible) : IEntityVisibilityChecker {
        public Task<bool> IsVisibleAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult(visible);
    }
}
