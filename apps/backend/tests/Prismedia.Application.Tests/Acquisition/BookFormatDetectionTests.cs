using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class BookFormatDetectionTests {
    [Theory]
    [InlineData("Saga Vol 1 (2012) (Digital) (CBR)", BookFormat.ImageArchive)]
    [InlineData("Some Comic v01 (cbz)", BookFormat.ImageArchive)]
    [InlineData("A Novel [EPUB]", BookFormat.Epub)]
    [InlineData("A Manual.pdf", BookFormat.Pdf)]
    [InlineData("A Novel.mobi", BookFormat.Epub)]
    public void DetectsNamedFormat(string title, BookFormat expected) {
        Assert.Contains(expected, BookFormatDetection.Detect(title));
    }

    [Fact]
    public void ReturnsEmptyWhenNoFormatTokenPresent() {
        Assert.Empty(BookFormatDetection.Detect("Just A Book Title 2020"));
    }

    [Fact]
    public void DetectsMultipleFormats() {
        var detected = BookFormatDetection.Detect("Bundle (epub + pdf)");

        Assert.Contains(BookFormat.Epub, detected);
        Assert.Contains(BookFormat.Pdf, detected);
    }
}
