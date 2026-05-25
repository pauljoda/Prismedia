using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Persistence;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class LibraryScanPersistenceServiceTests {
    [Fact]
    public async Task DownstreamNeedsProbeWhenTechnicalRowsLackMediaSources() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        SeedVideo(db, videoId);
        db.EntityTechnical.Add(new EntityTechnicalRow {
            EntityId = videoId,
            DurationSeconds = 60,
            Width = 1920,
            Height = 1080,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var needs = await service.CheckDownstreamNeedsBatchAsync([videoId], CancellationToken.None);

        Assert.True(needs[videoId].NeedsProbe);
    }

    [Fact]
    public async Task DownstreamNeedsTrickplayWhenThumbnailExistsWithoutTrickplayInfo() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        SeedVideo(db, videoId);
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = videoId,
            Role = EntityFileRole.Thumbnail,
            Path = "/assets/videos/222/thumb.jpg",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var needs = await service.CheckDownstreamNeedsBatchAsync([videoId], CancellationToken.None);

        Assert.False(needs[videoId].NeedsPreview);
        Assert.True(needs[videoId].NeedsTrickplay);
    }

    [Fact]
    public async Task DownstreamNeedsSubtitleExtractionWhenStoredSubtitleFileIsMissing() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        SeedVideo(db, videoId);
        db.VideoDetails.Add(new VideoDetailRow {
            EntityId = videoId,
            SubtitlesExtractedAt = DateTimeOffset.UtcNow
        });
        db.EntitySubtitles.Add(new EntitySubtitleRow {
            Id = Guid.NewGuid(),
            EntityId = videoId,
            Language = "eng",
            Format = "vtt",
            Source = EntitySubtitleSource.Embedded,
            StoragePath = "/tmp/prismedia/missing-subtitle.vtt",
            SourceFormat = "vtt",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var needs = await service.CheckDownstreamNeedsBatchAsync([videoId], CancellationToken.None);

        Assert.True(needs[videoId].NeedsSubtitleExtraction);
    }

    [Fact]
    public async Task UpsertSubtitleRefreshesExistingStreamInsteadOfDuplicatingIt() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var subtitleId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        SeedVideo(db, videoId);
        db.EntitySubtitles.Add(new EntitySubtitleRow {
            Id = subtitleId,
            EntityId = videoId,
            Language = "eng",
            Format = "vtt",
            Source = EntitySubtitleSource.Embedded,
            StoragePath = "/tmp/prismedia/stale.vtt",
            SourceFormat = "vtt",
            SourcePath = "3",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        await service.UpsertSubtitleAsync(
            videoId,
            "eng",
            "SDH",
            "vtt",
            EntitySubtitleSource.Embedded,
            "/data/cache/videos/444/subtitles/embedded-eng-3.vtt",
            "vtt",
            3,
            CancellationToken.None);

        var subtitle = Assert.Single(db.EntitySubtitles.Where(row => row.EntityId == videoId));
        Assert.Equal(subtitleId, subtitle.Id);
        Assert.Equal("/data/cache/videos/444/subtitles/embedded-eng-3.vtt", subtitle.StoragePath);
        Assert.Equal("SDH", subtitle.Label);
    }

    [Fact]
    public async Task UpsertSubtitleCreatesStreamSpecificRowWhenLanguageRowAlreadyExists() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var subtitleId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        SeedVideo(db, videoId);
        db.EntitySubtitles.Add(new EntitySubtitleRow {
            Id = subtitleId,
            EntityId = videoId,
            Language = "eng",
            Format = "vtt",
            Source = EntitySubtitleSource.Embedded,
            StoragePath = "/tmp/prismedia/stale.vtt",
            SourceFormat = "vtt",
            SourcePath = null,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        await service.UpsertSubtitleAsync(
            videoId,
            "eng",
            "SDH",
            "vtt",
            EntitySubtitleSource.Embedded,
            "/data/cache/videos/666/subtitles/embedded-eng-3.vtt",
            "vtt",
            3,
            CancellationToken.None);

        var subtitles = db.EntitySubtitles.Where(row => row.EntityId == videoId).ToArray();
        Assert.Equal(2, subtitles.Length);
        Assert.Contains(subtitles, subtitle => subtitle.Id == subtitleId && subtitle.Language == "eng" && subtitle.SourcePath is null);
        Assert.Contains(subtitles, subtitle => subtitle.Language == "eng.3"
            && subtitle.StoragePath == "/data/cache/videos/666/subtitles/embedded-eng-3.vtt"
            && subtitle.SourcePath == "3");
    }

    [Fact]
    public async Task UpsertSubtitleKeepsStreamLanguageWhenRequestedLanguageAlreadyExists() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var conflictId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var streamId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        SeedVideo(db, videoId);
        db.EntitySubtitles.AddRange(
            new EntitySubtitleRow {
                Id = conflictId,
                EntityId = videoId,
                Language = "eng",
                Format = "vtt",
                Source = EntitySubtitleSource.Embedded,
                StoragePath = "/tmp/prismedia/other-stream.vtt",
                SourceFormat = "vtt",
                SourcePath = "1",
                CreatedAt = DateTimeOffset.UtcNow
            },
            new EntitySubtitleRow {
                Id = streamId,
                EntityId = videoId,
                Language = "eng.3",
                Format = "vtt",
                Source = EntitySubtitleSource.Embedded,
                StoragePath = "/tmp/prismedia/url-shaped.vtt",
                SourceFormat = "subrip",
                SourcePath = "3",
                CreatedAt = DateTimeOffset.UtcNow
            });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        await service.UpsertSubtitleAsync(
            videoId,
            "eng",
            "SDH",
            "vtt",
            EntitySubtitleSource.Embedded,
            "/data/cache/videos/888/subtitles/embedded-eng-3.vtt",
            "subrip",
            3,
            CancellationToken.None);

        var subtitles = db.EntitySubtitles.Where(row => row.EntityId == videoId).ToArray();
        Assert.Equal(2, subtitles.Length);
        Assert.Contains(subtitles, subtitle => subtitle.Id == conflictId && subtitle.Language == "eng");
        Assert.Contains(subtitles, subtitle => subtitle.Id == streamId
            && subtitle.Language == "eng.3"
            && subtitle.StoragePath == "/data/cache/videos/888/subtitles/embedded-eng-3.vtt"
            && subtitle.SourcePath == "3");
    }

    [Fact]
    public async Task UpsertVideosBatchMaterializesSeasonHierarchyAndReusesMigratedSeries() {
        await using var db = CreateContext();
        var seriesId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var rootId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = seriesId,
            KindCode = EntityKindRegistry.VideoSeries.Code,
            Title = "The Chair Company",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.EntitySources.Add(new EntitySourceRow {
            EntityId = seriesId,
            Code = "folder",
            Value = "/media/The Chair Company",
            UpdatedAt = now
        });
        var seriesEntity = await db.Entities.FindAsync(seriesId);
        seriesEntity!.RatingValue = 4;
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var ids = await service.UpsertVideosBatchAsync([
            new VideoUpsertItem(
                "/media/The Chair Company/Season 1/The Chair Company - S01E01.mkv",
                "Life goes by too fast",
                rootId,
                IsNsfw: false,
                new VideoSeriesScanInfo("/media/The Chair Company", "The Chair Company"),
                new VideoSeasonScanInfo("/media/The Chair Company/Season 1", "Season 1", 1),
                EpisodeNumber: 1,
                AbsoluteEpisodeNumber: null)
        ], CancellationToken.None);

        var videoId = Assert.Single(ids);
        Assert.Equal(seriesId, Assert.Single(db.Entities.Where(entity => entity.KindCode == EntityKindRegistry.VideoSeries.Code)).Id);
        Assert.Equal(4, db.Entities.Single(entity => entity.Id == seriesId).RatingValue);

        var season = Assert.Single(db.Entities.Where(entity => entity.KindCode == EntityKindRegistry.VideoSeason.Code));
        Assert.Equal(1, season.SortOrder);

        Assert.Equal(seriesId, season.ParentEntityId);
        Assert.Equal(1, season.SortOrder);
        var video = Assert.Single(db.Entities.Where(entity => entity.Id == videoId));
        Assert.Equal(season.Id, video.ParentEntityId);
        Assert.Equal(1, video.SortOrder);
        Assert.Contains(db.EntityPositions, position =>
            position.EntityId == season.Id &&
            position.Code == "season" &&
            position.Value == 1);
        Assert.Contains(db.EntityPositions, position =>
            position.EntityId == videoId &&
            position.Code == "episode" &&
            position.Value == 1);
    }

    [Fact]
    public async Task RemoveStaleVideosByRootRemovesRootPathVideosWithoutLinkedRoot() {
        await using var db = CreateContext();
        var rootId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var videoId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var now = DateTimeOffset.UtcNow;

        db.LibraryRoots.Add(new LibraryRootRow {
            Id = rootId,
            Path = "/media/videos",
            Label = "Videos",
            CreatedAt = now,
            UpdatedAt = now
        });
        SeedVideo(db, videoId, "/media/videos/004.mkv");
        db.VideoDetails.Add(new VideoDetailRow {
            EntityId = videoId,
            LibraryRootId = null
        });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var removed = await service.RemoveStaleVideosByRootAsync(rootId, new HashSet<string>(), CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.False(await db.Entities.AnyAsync(entity => entity.Id == videoId));
    }

    [Fact]
    public async Task UpsertBookChapterReusesChapterWhenArchivePathAlsoBelongsToBook() {
        await using var db = CreateContext();
        var bookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var volumeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var chapterId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var archivePath = "/media/Promised Neverland/Volume 01/Promised Neverland Ch.1.zip";
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow {
                Id = bookId,
                KindCode = EntityKindRegistry.Book.Code,
                Title = "Promised Neverland Ch.1",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = volumeId,
                KindCode = EntityKindRegistry.BookVolume.Code,
                Title = "Volume 01",
                ParentEntityId = bookId,
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = chapterId,
                KindCode = EntityKindRegistry.BookChapter.Code,
                Title = "Promised Neverland Ch.1",
                ParentEntityId = bookId,
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.BookDetails.Add(new BookDetailRow { EntityId = bookId });
        db.BookChapterDetails.Add(new BookChapterDetailRow { EntityId = chapterId });
        db.EntityFiles.AddRange(
            new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = bookId,
                Role = EntityFileRole.Source,
                Path = archivePath,
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = chapterId,
                Role = EntityFileRole.Source,
                Path = archivePath,
                CreatedAt = now,
                UpdatedAt = now
            });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var result = await service.UpsertBookChapterAsync(
            archivePath,
            "Promised Neverland Ch.1",
            volumeId,
            sortOrder: 3,
            pageCount: 20,
            isNsfw: false,
            CancellationToken.None);

        Assert.Equal(chapterId, result);
        var chapter = Assert.Single(db.Entities.Where(entity => entity.KindCode == EntityKindRegistry.BookChapter.Code));
        Assert.Equal(volumeId, chapter.ParentEntityId);
        Assert.Equal(3, chapter.SortOrder);
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"library-scan-persistence-{Guid.NewGuid():N}")
            .Options;

        return new PrismediaDbContext(options);
    }

    private static void SeedVideo(PrismediaDbContext db, Guid videoId, string? sourcePath = null) {
        db.Entities.Add(new EntityRow {
            Id = videoId,
            KindCode = EntityKindRegistry.Video.Code,
            Title = "Video",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = videoId,
            Role = EntityFileRole.Source,
            Path = sourcePath ?? $"/media/{videoId}.mkv",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }
}
