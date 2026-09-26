using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class AudiobookReleaseShapeTests {
    private const long MiB = 1024 * 1024;

    public static TheoryData<string, ImportCandidateFile[]> Payloads => new() {
        { "single-m4b", [new("Author - Book/Book.m4b", 400 * MiB), new("Author - Book/cover.jpg", 1)] },
        { "single-m4b", [new("Book.m4a", 300 * MiB)] },
        { "single-large-file", [new("Book (Unabridged).mp3", 700 * MiB)] },
        {
            "chapter-files", [
                new("Book/01 - Opening Credits.mp3", 2 * MiB),
                new("Book/02 - Chapter 1.mp3", 31 * MiB),
                new("Book/03 - Chapter 2.mp3", 18 * MiB),
                new("Book/04 - Chapter 3.mp3", 44 * MiB)
            ]
        },
        {
            // LibriVox-style sections: sequential numbers, uneven lengths, no part markers.
            "chapter-files", [
                new("book_01_author_64kb.mp3", 12 * MiB),
                new("book_02_author_64kb.mp3", 21 * MiB),
                new("book_03_author_64kb.mp3", 9 * MiB),
                new("book_04_author_64kb.mp3", 17 * MiB)
            ]
        },
        {
            // OverDrive splits by part, not at chapters.
            "part-files", Enumerable.Range(1, 10)
                .Select(part => new ImportCandidateFile($"Book/Book-Part{part:00}.mp3", (30 + part) * MiB)).ToArray()
        },
        {
            "part-files", [
                new("Book/CD1/01 Track.mp3", 5 * MiB),
                new("Book/CD1/02 Track.mp3", 6 * MiB),
                new("Book/CD2/01 Track.mp3", 5 * MiB),
                new("Book/CD2/02 Track.mp3", 4 * MiB)
            ]
        },
        {
            // An unlabelled length split: equal long files and a shorter tail.
            "part-files", [
                new("Book 01.mp3", 57 * MiB),
                new("Book 02.mp3", 57 * MiB),
                new("Book 03.mp3", 56 * MiB),
                new("Book 04.mp3", 19 * MiB)
            ]
        },
        { "mixed", [new("Book/Book.m4b", 400 * MiB), new("Book/mp3/01.mp3", 30 * MiB), new("Book/mp3/02.mp3", 30 * MiB)] },
        { "unimportable", [new("Book/01.flac", 90 * MiB), new("Book/02.flac", 80 * MiB)] },
        { "unimportable", [new("Book.aax", 300 * MiB)] },
        { "unknown", [] }
    };

    [Theory]
    [MemberData(nameof(Payloads))]
    public void ClassifiesADownloadFromItsFileNamesAndSizes(string expected, ImportCandidateFile[] files) {
        Assert.Equal(expected, AudiobookReleaseShape.Of(files).Code);
    }

    [Fact]
    public void AMixedDownloadImportsAndRanksOnlyItsPreferredFormat() {
        ImportCandidateFile[] files = [
            new("Book/mp3/01 - Chapter 1.mp3", 30 * MiB),
            new("Book/Book.m4b", 400 * MiB),
            new("Book/mp3/02 - Chapter 2.mp3", 30 * MiB)
        ];

        Assert.Same(AudiobookReleaseShape.Mixed, AudiobookReleaseShape.Of(files));
        Assert.Same(AudiobookReleaseShape.SingleM4b, AudiobookReleaseShape.Resolve(files));
        Assert.Equal("Book/Book.m4b", Assert.Single(AudiobookReleaseShape.ImportSet(files)).RelativePath);
    }

    [Theory]
    [InlineData("Author - Book (2020) [M4B]", null, "single-m4b")]
    [InlineData("Author - Book [MP3 + M4B]", null, "single-m4b")]
    [InlineData("Author - Book MP3 64kbps", null, "unknown")]
    [InlineData("Author - Book MP3 64kbps", 1, "single-large-file")]
    [InlineData("Author - Book MP3 64kbps", 40, "unknown")]
    [InlineData("Author - Book (FLAC)", null, "unimportable")]
    [InlineData("Author - Book [AAX]", null, "unimportable")]
    [InlineData("Author - Book Opus", null, "unimportable")]
    [InlineData("Author - Book FLAC MP3", null, "unknown")]
    [InlineData("Author - Book (Unabridged)", null, "unknown")]
    public void ExpectsASearchResultShapeFromItsTitle(string title, int? fileCount, string expected) {
        var release = Release(title) with { AdvertisedFileCount = fileCount };

        Assert.Equal(expected, AudiobookReleaseShape.Expected(release).Code);
    }

    [Fact]
    public void AdvertisedFileNamesOutweighTheTitle() {
        var release = Release("Author - Book [M4B]") with {
            KnownFileNames = ["Audio\\Author\\Book\\Book-Part01.mp3", "Audio\\Author\\Book\\Book-Part02.mp3"]
        };

        Assert.Same(AudiobookReleaseShape.PartFiles, AudiobookReleaseShape.Expected(release));
    }

    [Fact]
    public void ChapteredShapesOutrankUnknownWhichOutranksChapterlessShapes() {
        AudiobookReleaseShape[] preferred = [
            AudiobookReleaseShape.SingleM4b,
            AudiobookReleaseShape.ChapterFiles,
            AudiobookReleaseShape.Unknown,
            AudiobookReleaseShape.PartFiles,
            AudiobookReleaseShape.SingleLargeFile
        ];

        Assert.Equal(preferred, preferred.OrderByDescending(shape => shape.Rank));
        Assert.All(preferred[..2], shape => Assert.True(shape.IsChaptered));
        Assert.All(preferred[3..], shape => Assert.True(shape.IsChapterless));
        Assert.False(AudiobookReleaseShape.Unimportable.IsAdmissible);
        Assert.Same(AudiobookReleaseShape.ChapterFiles, AudiobookReleaseShape.ChapteredFloor);
    }

    [Fact]
    public void StoredSpellingsRoundTrip() {
        Assert.All(AudiobookReleaseShape.All, shape => Assert.Same(shape, AudiobookReleaseShape.Parse(shape.Code)));
        Assert.Same(AudiobookReleaseShape.Unknown, AudiobookReleaseShape.Parse("future-layout"));
    }

    private static IndexerRelease Release(string title) =>
        new(title, 500 * MiB, 10, 1, DownloadProtocol.Torrent, "http://dl", null, "hash", null, null, null);
}
