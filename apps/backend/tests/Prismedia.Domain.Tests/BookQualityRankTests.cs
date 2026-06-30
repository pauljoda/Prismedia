using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Tests;

/// <summary>
/// Locks the book quality-rank value object that the upgrade-until-cutoff loop is built on. The critical
/// guarantee is <see cref="BookQualityRank.StrictlyDominates"/>: an auto-upgrade must never downgrade either
/// axis, so a gain on one axis that regresses the other (the classic retail-PDF-over-web-EPUB trap) is not a
/// dominating upgrade — even though its scalar <see cref="BookQualityRank.Value"/> is higher.
/// </summary>
public sealed class BookQualityRankTests {
    [Theory]
    [InlineData(BookFormat.Epub, BookFormatTier.Reflowable)]
    [InlineData(BookFormat.ImageArchive, BookFormatTier.Archive)]
    [InlineData(BookFormat.Pdf, BookFormatTier.Fixed)]
    public void TierForMapsEveryImportableFormat(BookFormat format, BookFormatTier expected) =>
        Assert.Equal(expected, BookQualityRank.TierFor(format));

    [Fact]
    public void FloorIsLowestOnBothAxes() {
        Assert.Equal(BookSourceTier.Unknown, BookQualityRank.Floor.Source);
        Assert.Equal(BookFormatTier.Unknown, BookQualityRank.Floor.Format);
    }

    [Fact]
    public void EqualQualityDoesNotDominate() {
        var owned = new BookQualityRank(BookSourceTier.Retail, BookFormatTier.Reflowable);
        Assert.False(owned.StrictlyDominates(owned));
    }

    [Fact]
    public void BetterOnBothAxesDominates() {
        var owned = new BookQualityRank(BookSourceTier.Web, BookFormatTier.Fixed);
        var candidate = new BookQualityRank(BookSourceTier.Retail, BookFormatTier.Reflowable);
        Assert.True(candidate.StrictlyDominates(owned));
    }

    [Fact]
    public void BetterOnOneAxisAndEqualOnTheOtherDominates() {
        var owned = new BookQualityRank(BookSourceTier.Web, BookFormatTier.Reflowable);
        Assert.True(new BookQualityRank(BookSourceTier.Retail, BookFormatTier.Reflowable).StrictlyDominates(owned)); // source up
        Assert.True(new BookQualityRank(BookSourceTier.Web, BookFormatTier.Archive).StrictlyDominates(owned));       // format up
    }

    [Fact]
    public void WorseOnEitherAxisDoesNotDominate() {
        var owned = new BookQualityRank(BookSourceTier.Web, BookFormatTier.Reflowable);
        // Worse on both.
        Assert.False(new BookQualityRank(BookSourceTier.Unknown, BookFormatTier.Fixed).StrictlyDominates(owned));
        // Better format, worse source.
        Assert.False(new BookQualityRank(BookSourceTier.Unknown, BookFormatTier.Archive).StrictlyDominates(owned));
    }

    [Fact]
    public void HigherSourceWithLowerFormatIsNotAnUpgrade() {
        // The load-bearing case: a retail PDF must NOT be able to replace a web EPUB, because that swaps a
        // reflowable ebook for a fixed-layout one — a real downgrade for the reader.
        var ownedWebEpub = new BookQualityRank(BookSourceTier.Web, BookFormatTier.Reflowable);
        var retailPdf = new BookQualityRank(BookSourceTier.Retail, BookFormatTier.Fixed);

        Assert.False(retailPdf.StrictlyDominates(ownedWebEpub));
        // ...and this is exactly why acceptance does NOT use the scalar Value, which would wrongly prefer it:
        Assert.True(retailPdf.Value > ownedWebEpub.Value);
    }

    [Fact]
    public void ValueOrdersSourceMajorThenFormatForAcceptedTieBreaks() {
        // Among already-accepted candidates, Value is a stable source-major ordering key.
        var retailFixed = new BookQualityRank(BookSourceTier.Retail, BookFormatTier.Fixed);
        var webArchive = new BookQualityRank(BookSourceTier.Web, BookFormatTier.Archive);
        var retailReflowable = new BookQualityRank(BookSourceTier.Retail, BookFormatTier.Reflowable);

        Assert.True(retailFixed.Value > webArchive.Value);          // source dominates format
        Assert.True(retailReflowable.Value > retailFixed.Value);    // within a source, higher format wins
    }

    [Theory]
    [InlineData(BookFormatTier.Unknown)]
    [InlineData(BookFormatTier.Fixed)]
    [InlineData(BookFormatTier.Reflowable)]
    [InlineData(BookFormatTier.Archive)]
    public void FormatTierCodeRoundTrips(BookFormatTier tier) =>
        Assert.Equal(tier, tier.ToCode().DecodeAs<BookFormatTier>());

    [Theory]
    [InlineData(BookSourceTier.Unknown)]
    [InlineData(BookSourceTier.Web)]
    [InlineData(BookSourceTier.Retail)]
    public void SourceTierCodeRoundTrips(BookSourceTier tier) =>
        Assert.Equal(tier, tier.ToCode().DecodeAs<BookSourceTier>());
}
