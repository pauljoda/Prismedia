using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;

namespace Prismedia.Domain.Tests;

public sealed class BookModelTests {
    [Fact]
    public void BookCarriesBaseEntityFieldsAndBookSpecificDetails() {
        var book = new Book(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "The Brass Archive",
            bookType: BookType.Comic,
            coverPageId: null,
            capabilities:
            [
                new CapabilityStats([
                    new CapabilityStats.Item("pages", 120),
                    new CapabilityStats.Item("chapters", 6)
                ])
            ]);

        Assert.Equal(EntityKind.Book, book.Kind);
        Assert.Equal("The Brass Archive", book.Title);
        Assert.Equal(BookType.Comic, book.BookType);
        Assert.Equal(120, book.Stats!.Items.Single(stat => stat.Code == "pages").Value);
    }

    [Fact]
    public void BookMutatorsKeepBookRulesInsideTheBookModel() {
        var book = new Book(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "Draft Book",
            bookType: BookType.Book,
            coverPageId: null,
            capabilities:
            [
                new CapabilityProgress(),
                new CapabilityDescription("Initial")
            ]);

        book.Description!.SetValue("Updated from metadata.");
        book.MoveReaderToChapter(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            pageIndex: 4,
            pageCount: 12,
            readerMode: ReaderMode.Paged);

        Assert.Equal("Updated from metadata.", book.Description.Value);
        Assert.Equal(Guid.Parse("33333333-3333-3333-3333-333333333333"), book.Progress!.CurrentEntityId);
        Assert.Equal(4, book.Progress.Index);
        Assert.Null(book.Progress.CompletedAt);
    }
}
