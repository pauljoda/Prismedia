using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Jobs;

public sealed class LibraryScanJobsTests {
    [Fact]
    public void SelectionUnionCombinesFamiliesAndIdentifiesEmptySelection() {
        var media = new LibraryScanSelection(Videos: true, Images: false, Audio: false, Books: false);
        var booksAndAudio = new LibraryScanSelection(Videos: false, Images: false, Audio: true, Books: true);

        var selection = media.Union(booksAndAudio);

        Assert.True(LibraryScanSelection.None.IsEmpty);
        Assert.False(selection.IsEmpty);
        Assert.Equal(
            new LibraryScanSelection(Videos: true, Images: false, Audio: true, Books: true),
            selection);
    }

    [Fact]
    public void ScanJobTypesForMapsEachSelectedFamilyInCanonicalOrder() {
        var selection = new LibraryScanSelection(Videos: true, Images: true, Audio: true, Books: true);

        var types = LibraryScanJobs.ScanJobTypesFor(selection);

        Assert.Equal(
            [JobType.ScanLibrary, JobType.ScanGallery, JobType.ScanAudio, JobType.ScanBook],
            types);
    }
}
