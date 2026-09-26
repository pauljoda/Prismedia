using System.Net;
using System.Text.Json;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Tests;

public sealed class SlskdPublicationSearchTests {
    [Theory]
    [InlineData("Books/Integration Writer/Soulseek Field Notes.epub")]
    [InlineData("Books\\Integration Writer\\Soulseek Field Notes.epub")]
    public async Task PeerPathSeparatorsPreserveTheTitleAndAuthorMatchingBoundary(string path) {
        var release = Assert.Single(await Search(EntityKind.Book, BookRendition.Ebook, (path, 1000)));
        var rules = BookAcquisitionRules.Default with { TargetTitle = "Soulseek Field Notes", TargetAuthor = "Integration Writer" };
        Assert.Null(new BookTitleIdentitySpecification().Evaluate(release, rules));
        Assert.Equal(path, Assert.Single(SoulseekLocator.Decode(Assert.IsType<string>(release.DownloadUrl)).Files).Filename);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(BookRendition.Ebook)]
    public async Task EbookFormatsAndBooksRemainIndividuallySelectable(BookRendition? rendition) {
        var releases = await Search(EntityKind.Book, rendition,
            ("Books\\Author\\One.epub", 100), ("Books\\Author\\One.pdf", 200),
            ("Books\\Author\\Two.epub", 300), ("Books\\Author\\One.m4b", 400),
            ("Books\\Author\\One.mobi", 500), ("Books\\Author\\cover.jpg", 10));
        Assert.Equal(3, releases.Count);
        Assert.All(releases, release => Assert.Single(SoulseekLocator.Decode(Assert.IsType<string>(release.DownloadUrl)).Files));
        Assert.Equal(new long[] { 100, 200, 300 }, releases.Select(release => release.SizeBytes));
        Assert.All(releases, release => Assert.Contains("Author", release.Title));
        Assert.Equal("Books\\Author\\One.epub", Assert.Single(releases[0].KnownFileNames!));
    }

    [Theory]
    [InlineData(EntityKind.ComicSeries)]
    [InlineData(EntityKind.ComicVolume)]
    [InlineData(EntityKind.ComicInstallment)]
    public async Task ComicSearchKeepsExactIssuesAndArchivesSeparate(EntityKind kind) {
        var releases = await Search(kind, null, ("Comics/Run/Issue 12.cbz", 100),
            ("Comics/Run/Issue 12.5.CBZ", 200), ("Comics/Run/Annual 1.zip", 300),
            ("Comics/Run/Issue 12.cbr", 400), ("Comics/Run/Issue 12.pdf", 500));
        Assert.Equal(3, releases.Count);
        Assert.All(releases, release => Assert.Single(SoulseekLocator.Decode(Assert.IsType<string>(release.DownloadUrl)).Files));
        Assert.Contains(releases, release => release.Title.Contains("12.5.CBZ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AudiobooksKeepWholeBookFilesSeparateFromOrderedChapterFoldersAndAlternativeFormats() {
        var releases = await Search(EntityKind.Book, BookRendition.Audiobook,
            ("Audio/Author/One.m4b", 500), ("Audio/Author/Two.m4b", 600),
            ("Audio/Author/One/02 Chapter.mp3", 200), ("Audio/Author/One/01 Chapter.mp3", 100),
            ("Audio/Author/One/01 Chapter.m4a", 300), ("Audio/Author/One/One.epub", 50),
            ("Audio/Author/One/cover.jpg", 20), ("Audio/Author/One/bonus.flac", 30));
        Assert.Equal(4, releases.Count);
        var chapters = Assert.Single(releases, release => release.KnownFileNames!.Count == 2);
        Assert.Equal(new[] { "Audio/Author/One/01 Chapter.mp3", "Audio/Author/One/02 Chapter.mp3" }, chapters.KnownFileNames);
        Assert.Equal(300, chapters.SizeBytes);
        Assert.Contains("One", chapters.Title);
        Assert.Contains("MP3", chapters.Title);
        Assert.All(releases, release => Assert.Equal(DownloadProtocol.Soulseek, release.Protocol));
    }

    [Fact]
    public async Task DuplicateOrInvalidFileEvidenceDoesNotInflatePublicationDownloads() {
        var releases = await Search(EntityKind.Book, null,
            ("Books/One.epub", 100), ("Books/One.epub", 100),
            ("Books/Ambiguous.epub", 200), ("Books/Ambiguous.epub", 300),
            ("Books/Empty.epub", 0), ("Books/Invalid.epub", -1));
        var release = Assert.Single(releases);
        Assert.Equal("Books/One.epub", Assert.Single(release.KnownFileNames!));
        Assert.Equal(100, release.SizeBytes);
    }

    [Fact]
    public async Task TimedOutPeerWindowStillReturnsCollectedPublicationResults() {
        using var http = new HttpClient(new Fixture([("Books/Author/One.epub", 100)], SoulseekProtocol.SearchTimedOutState));
        var releases = await new SlskdIndexerClient(http).SearchAsync(
            new(Guid.NewGuid(), IndexerKind.Slskd, "http://slskd.test", "fixture-key", []),
            new IndexerQuery("Author One", [], EntityKind.Book) { BookRendition = BookRendition.Ebook }, default);

        Assert.Equal("Books/Author/One.epub", Assert.Single(Assert.Single(releases).KnownFileNames!));
    }

    private static async Task<IReadOnlyList<IndexerRelease>> Search(EntityKind kind, BookRendition? rendition,
        params (string Path, long Size)[] files) {
        using var http = new HttpClient(new Fixture(files));
        return await new SlskdIndexerClient(http).SearchAsync(
            new(Guid.NewGuid(), IndexerKind.Slskd, "http://slskd.test", "fixture-key", []),
            new IndexerQuery("Author One", [], kind) { BookRendition = rendition }, default);
    }

    private sealed class Fixture((string Path, long Size)[] files, string finalState = SoulseekProtocol.SearchCompletedState) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
            object body = request.RequestUri!.AbsolutePath.EndsWith("/responses", StringComparison.Ordinal)
                ? new[] { new { username = "fixture-peer", files = files.Select(file => new { filename = file.Path, size = file.Size }) } }
                : new { state = request.Method == HttpMethod.Post && finalState == SoulseekProtocol.SearchTimedOutState
                    ? SoulseekProtocol.SearchInProgressState : finalState };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
