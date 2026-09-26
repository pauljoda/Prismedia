using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Tests;

public sealed class IntegrationImportPolicyTests {
    [Fact]
    public void ImportableKindsDeclareTheirOwnFormatsLimitsAndRoots() {
        var book = IntegrationImportPolicy.For(EntityKind.Book);
        Assert.True(book.AcceptsFileName("Dracula.EPUB"));
        Assert.True(book.AcceptsMediaType("application/pdf; charset=binary"));
        Assert.False(book.AcceptsFileName("issue.cbz"));

        var gallery = IntegrationImportPolicy.For(EntityKind.Gallery);
        Assert.False(gallery.AcceptsDirectFiles);
        Assert.True(gallery.RequiresRecursiveRoot);
        Assert.True(gallery.Content.AcceptsFileName("page-001.png"));
        Assert.Equal(64L * 1024 * 1024, gallery.Content.MaximumBytes);

        Assert.Equal(LibraryRootMediaCapability.ScanBooks, IntegrationImportPolicy.For(EntityKind.ComicInstallment).RootCapability);
        Assert.False(IntegrationImportPolicy.Supports(EntityKind.Movie));
    }
}
