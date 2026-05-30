using System.IO.Compression;
using Prismedia.Infrastructure.Media.Sidecars;

namespace Prismedia.Infrastructure.Tests;

public sealed class SidecarMetadataReaderTests {
    [Fact]
    public async Task VideoReaderMergesJsonAndNfoSidecarsWithNfoPriority() {
        var dir = Directory.CreateTempSubdirectory("prismedia-video-sidecars-");
        try {
            var videoPath = Path.Combine(dir.FullName, "scene.mkv");
            await File.WriteAllTextAsync(videoPath, "");
            await File.WriteAllTextAsync(
                Path.Combine(dir.FullName, "scene.info.json"),
                """
                {
                  "title": "JSON Title",
                  "description": "JSON description",
                  "upload_date": "20260501",
                  "tags": ["json-tag"],
                  "performers": ["Ada Actor"],
                  "webpage_url": "https://example.test/json",
                  "average_rating": 8
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(dir.FullName, "scene.nfo"),
                """
                <episodedetails>
                  <title>NFO Title</title>
                  <plot>NFO plot</plot>
                  <aired>2026-05-02</aired>
                  <studio>NFO Studio</studio>
                  <rating>4</rating>
                  <tag>nfo-tag</tag>
                  <genre>genre-tag</genre>
                  <url>https://example.test/nfo</url>
                </episodedetails>
                """);

            var metadata = await new VideoSidecarMetadataReader().ReadAsync(videoPath, CancellationToken.None);

            Assert.NotNull(metadata);
            Assert.Equal("NFO Title", metadata.Title);
            Assert.Equal("NFO plot", metadata.Description);
            Assert.Equal("2026-05-02", metadata.Date);
            Assert.Equal("NFO Studio", metadata.Studio);
            Assert.Equal(4, metadata.Rating);
            Assert.Equal(["https://example.test/json", "https://example.test/nfo"], metadata.Urls);
            Assert.Equal(["json-tag", "nfo-tag", "genre-tag"], metadata.Tags);
            Assert.Equal(["Ada Actor"], metadata.Performers);
        } finally {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ComicInfoReaderExtractsMetadataFromCbzArchive() {
        var dir = Directory.CreateTempSubdirectory("prismedia-comicinfo-reader-");
        try {
            var archivePath = Path.Combine(dir.FullName, "comic.cbz");
            using (var stream = File.Create(archivePath))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create)) {
                var info = archive.CreateEntry("ComicInfo.xml");
                await using (var infoStream = info.Open())
                await using (var writer = new StreamWriter(infoStream)) {
                    await writer.WriteAsync(
                        """
                        <ComicInfo>
                          <Title>Chapter One</Title>
                          <Series>Series Name</Series>
                          <Number>1</Number>
                          <Volume>2</Volume>
                          <Summary>Comic summary</Summary>
                          <Year>2026</Year>
                          <Month>5</Month>
                          <Publisher>Publisher Name</Publisher>
                          <Writer>Ada Writer; Bob Writer</Writer>
                          <Genre>Drama, Mystery</Genre>
                          <Tags>Noir</Tags>
                          <AgeRating>Adults Only 18+</AgeRating>
                          <Web>https://example.test/comic</Web>
                        </ComicInfo>
                        """);
                }

                archive.CreateEntry("001.jpg");
            }

            var metadata = await new ComicInfoMetadataReader().ReadAsync(archivePath, CancellationToken.None);

            Assert.NotNull(metadata);
            Assert.Equal("Chapter One", metadata.Title);
            Assert.Equal("Series Name", metadata.Series);
            Assert.Equal("1", metadata.Number);
            Assert.Equal(2, metadata.Volume);
            Assert.Equal("Comic summary", metadata.Summary);
            Assert.Equal("2026-05", metadata.Date);
            Assert.Equal("Publisher Name", metadata.Publisher);
            Assert.Equal(["Ada Writer", "Bob Writer"], metadata.Creators);
            Assert.Equal(["Drama", "Mystery", "Noir", "Adults Only 18+"], metadata.Tags);
            Assert.Equal(["https://example.test/comic"], metadata.Urls);
            Assert.True(metadata.MarksNsfw);
        } finally {
            dir.Delete(recursive: true);
        }
    }
}
