using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;

namespace Prismedia.Domain.Tests;

public sealed class EntityPageManifestTests {
    [Fact]
    public void ManifestPreservesExactMembersAndCanonicalOrder() {
        var entityId = Guid.NewGuid();
        var manifest = new EntityPageManifest(
            entityId,
            PageReadingDirection.RightToLeft,
            ReaderMode.Paged,
            coverOrdinal: 0,
            sourceSignature: "sha256:archive",
            pages:
            [
                new EntityPageEntry(1, "Story/010.5.png", "image/png", 1200, 1800, PageType.Story, false, "sha256:p2"),
                new EntityPageEntry(0, "Covers/Front Cover.jpg", "image/jpeg", 2400, 3600, PageType.FrontCover, true, "sha256:p1")
            ]);

        Assert.Equal(entityId, manifest.EntityId);
        Assert.Equal(PageReadingDirection.RightToLeft, manifest.Direction);
        Assert.Equal(ReaderMode.Paged, manifest.DefaultMode);
        Assert.Equal(0, manifest.CoverOrdinal);
        Assert.Equal("sha256:archive", manifest.SourceSignature);
        Assert.Equal([0, 1], manifest.Pages.Select(page => page.Ordinal));
        Assert.Equal("Covers/Front Cover.jpg", manifest.Pages[0].ArchiveMember);
        Assert.True(manifest.Pages[0].IsDoublePage);
    }

    [Fact]
    public void ManifestRejectsUnsafeOrAmbiguousMembers() {
        Assert.Throws<ArgumentException>(() => Page("../secret.jpg"));
        Assert.Throws<ArgumentException>(() => Page("/absolute.jpg"));
        Assert.Throws<ArgumentException>(() => Page("folder\\..\\secret.jpg"));
        Assert.Throws<ArgumentException>(() => Page("C:\\secret.jpg"));
        Assert.Throws<ArgumentException>(() => new EntityPageEntry(
            0,
            "page.html",
            "text/html",
            null,
            null,
            PageType.Story,
            false,
            null));

        Assert.Throws<ArgumentException>(() => new EntityPageManifest(
            Guid.NewGuid(),
            PageReadingDirection.LeftToRight,
            ReaderMode.Paged,
            coverOrdinal: null,
            sourceSignature: "signature",
            pages: [Page("page.jpg"), Page("page.jpg", ordinal: 1)]));
    }

    [Fact]
    public void ManifestRequiresContiguousPagesAndAValidCover() {
        Assert.Throws<ArgumentException>(() => new EntityPageManifest(
            Guid.NewGuid(),
            PageReadingDirection.LeftToRight,
            ReaderMode.Paged,
            coverOrdinal: null,
            sourceSignature: "signature",
            pages: [Page("page-2.jpg", ordinal: 2)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EntityPageManifest(
            Guid.NewGuid(),
            PageReadingDirection.LeftToRight,
            ReaderMode.Paged,
            coverOrdinal: 2,
            sourceSignature: "signature",
            pages: [Page("page-0.jpg")]));
        Assert.Throws<ArgumentException>(() => new EntityPageManifest(
            Guid.NewGuid(),
            PageReadingDirection.TopToBottom,
            ReaderMode.Scrolled,
            coverOrdinal: null,
            sourceSignature: "signature",
            pages: [Page("page-0.jpg")]));
    }

    private static EntityPageEntry Page(string member, int ordinal = 0) =>
        new(ordinal, member, "image/jpeg", null, null, PageType.Story, false, null);
}
