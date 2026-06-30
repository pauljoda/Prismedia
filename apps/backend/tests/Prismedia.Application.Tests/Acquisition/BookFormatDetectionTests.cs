using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class BookFormatDetectionTests {
    [Theory]
    [InlineData("Saga Vol 1 (2012) (Digital) (CBZ)", BookFormat.ImageArchive)]
    [InlineData("Some Comic v01 (cbz)", BookFormat.ImageArchive)]
    [InlineData("A Novel [EPUB]", BookFormat.Epub)]
    [InlineData("A Manual.pdf", BookFormat.Pdf)]
    public void DetectsImportableFormat(string title, BookFormat expected) {
        Assert.Contains(expected, BookFormatDetection.Detect(title));
    }

    [Fact]
    public void ReturnsEmptyWhenNoFormatTokenPresent() {
        Assert.Empty(BookFormatDetection.Detect("Just A Book Title 2020"));
    }

    [Theory]
    [InlineData("A Novel.mobi")]
    [InlineData("A Novel (AZW3)")]
    [InlineData("Saga Vol 1 (CBR)")]
    public void DoesNotDetectUnimportableFormatsButFlagsThem(string title) {
        // The pipeline can't import mobi/azw3/cbr, so they must not be claimed as importable...
        Assert.Empty(BookFormatDetection.Detect(title));
        // ...but the title is recognized as declaring an unimportable format so the engine can reject it.
        Assert.True(BookFormatDetection.NamesUnsupportedFormat(title));
    }

    [Fact]
    public void DetectsMultipleImportableFormats() {
        var detected = BookFormatDetection.Detect("Bundle (epub + pdf)");

        Assert.Contains(BookFormat.Epub, detected);
        Assert.Contains(BookFormat.Pdf, detected);
    }

    [Theory]
    [InlineData("The Book (Retail EPUB)", BookSourceTier.Retail)]
    [InlineData("The Book [Official]", BookSourceTier.Retail)]
    [InlineData("The Book (web)", BookSourceTier.Web)]
    [InlineData("The Book (Calibre converted)", BookSourceTier.Web)]
    [InlineData("The Book 2020", BookSourceTier.Unknown)]
    public void DetectsSourceTier(string title, BookSourceTier expected) =>
        Assert.Equal(expected, BookFormatDetection.DetectSource(title));

    [Fact]
    public void RetailWinsWhenBothSourceTokensPresent() =>
        Assert.Equal(BookSourceTier.Retail, BookFormatDetection.DetectSource("Retail web conversion"));

    [Theory]
    [InlineData("A Novel [EPUB]", BookFormatTier.Reflowable)]
    [InlineData("A Manual.pdf", BookFormatTier.Fixed)]
    [InlineData("Saga Vol 1 (CBZ)", BookFormatTier.Archive)]
    [InlineData("Just A Title 2020", BookFormatTier.Unknown)]
    public void DetectsFormatTier(string title, BookFormatTier expected) =>
        Assert.Equal(expected, BookFormatDetection.DetectFormatTier(title));

    [Fact]
    public void FormatTierTakesTheBestNamedFormat() =>
        // PDF + EPUB named together resolves to the higher (reflowable) tier.
        Assert.Equal(BookFormatTier.Reflowable, BookFormatDetection.DetectFormatTier("Bundle (epub + pdf)"));

    [Fact]
    public void DetectQualityCombinesBothAxes() {
        var quality = BookFormatDetection.DetectQuality("The Book (Retail EPUB)");

        Assert.Equal(BookSourceTier.Retail, quality.Source);
        Assert.Equal(BookFormatTier.Reflowable, quality.Format);
    }
}
