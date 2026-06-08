using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Persistence;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class LibraryScanPersistenceServiceTests {
    private static readonly Guid RootId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

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
    public async Task DownstreamNeedsAudioPreviewUsesWaveformRole() {
        await using var db = CreateContext();
        var trackWithWaveformId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
        var trackWithThumbnailId = Guid.Parse("aaaaaaaa-2222-2222-2222-222222222222");
        SeedSourceEntity(db, trackWithWaveformId, EntityKindRegistry.AudioTrack.Code, "/media/audio/with-waveform.m4a");
        SeedSourceEntity(db, trackWithThumbnailId, EntityKindRegistry.AudioTrack.Code, "/media/audio/with-thumbnail.m4a");
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = trackWithWaveformId,
            Role = EntityFileRole.Waveform,
            Path = $"/assets/audio-tracks/{trackWithWaveformId}/waveform.json",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = trackWithThumbnailId,
            Role = EntityFileRole.Thumbnail,
            Path = $"/assets/audio-tracks/{trackWithThumbnailId}/thumbnail.jpg",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var needs = await service.CheckDownstreamNeedsBatchAsync(
            [trackWithWaveformId, trackWithThumbnailId],
            CancellationToken.None);

        Assert.False(needs[trackWithWaveformId].NeedsPreview);
        Assert.True(needs[trackWithThumbnailId].NeedsPreview);
    }

    [Fact]
    public async Task DownstreamNeedsImagePreviewClipWhenVideoLikeImageHasOnlyThumbnail() {
        await using var db = CreateContext();
        var animatedImageId = Guid.Parse("bbbbbbbb-1111-1111-1111-111111111111");
        var stillImageId = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");
        SeedSourceEntity(db, animatedImageId, EntityKindRegistry.Image.Code, "/media/images/animated.webm");
        SeedSourceEntity(db, stillImageId, EntityKindRegistry.Image.Code, "/media/images/photo.jpg");
        db.EntityFiles.AddRange(
            new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = animatedImageId,
                Role = EntityFileRole.Thumbnail,
                Path = $"/assets/images/{animatedImageId}/thumb.jpg",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = stillImageId,
                Role = EntityFileRole.Thumbnail,
                Path = $"/assets/images/{stillImageId}/thumb.jpg",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var needs = await service.CheckDownstreamNeedsBatchAsync(
            [animatedImageId, stillImageId],
            CancellationToken.None);

        Assert.True(needs[animatedImageId].NeedsPreview);
        Assert.False(needs[stillImageId].NeedsPreview);
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
    public async Task RemoveEntitiesInExcludedPathsRemovesExistingSourcesUnderExcludedDirectories() {
        await using var db = CreateContext();
        var keepId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var skipId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        SeedLibraryRoot(db, RootId, "/media/videos");
        SeedSourceEntity(db, keepId, EntityKindRegistry.Video.Code, "/media/videos/Keep/movie.mkv");
        SeedSourceEntity(db, skipId, EntityKindRegistry.Video.Code, "/media/videos/Skip/movie.mkv");
        db.VideoDetails.Add(new VideoDetailRow { EntityId = keepId, LibraryRootId = RootId });
        db.VideoDetails.Add(new VideoDetailRow { EntityId = skipId, LibraryRootId = RootId });
        db.MediaFileIgnores.Add(new MediaFileIgnoreRow {
            LibraryRootId = RootId,
            Path = "Skip",
            Kind = "directory",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = new LibraryScanPersistenceService(db);

        var removed = await service.RemoveEntitiesInExcludedPathsAsync(RootId, CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.Contains(db.Entities, entity => entity.Id == keepId);
        Assert.DoesNotContain(db.Entities, entity => entity.Id == skipId);
    }

    [Fact]
    public async Task GetExcludedPathsForRootReturnsAbsolutePaths() {
        await using var db = CreateContext();
        SeedLibraryRoot(db, RootId, "/media/videos");
        db.MediaFileIgnores.Add(new MediaFileIgnoreRow {
            LibraryRootId = RootId,
            Path = "Skip/movie.mkv",
            Kind = "file",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = new LibraryScanPersistenceService(db);

        var paths = await service.GetExcludedPathsForRootAsync(RootId, CancellationToken.None);

        Assert.Equal([Path.GetFullPath("/media/videos/Skip/movie.mkv")], paths);
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
    public async Task UpsertVideosBatchMaterializesMovieHierarchy() {
        await using var db = CreateContext();
        var rootId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var service = new LibraryScanPersistenceService(db);

        var ids = await service.UpsertVideosBatchAsync([
            new VideoUpsertItem(
                "/media/Friendship/Friendship.mp4",
                "Friendship",
                rootId,
                IsNsfw: false,
                Metadata: new VideoSidecarMetadata {
                    Title = "Friendship",
                    Description = "Movie description",
                    Date = "2025-05-09",
                    Studio = "BoulderLight Pictures",
                    Tags = ["Comedy"],
                    Performers = ["Tim Robinson", "Paul Rudd"]
                },
                Movie: new MovieScanInfo("/media/Friendship", "Friendship"))
        ], CancellationToken.None);

        var videoId = Assert.Single(ids);
        var movie = Assert.Single(db.Entities.Where(entity => entity.KindCode == EntityKindRegistry.Movie.Code));
        Assert.Equal("Friendship", movie.Title);
        Assert.False(movie.IsNsfw);

        var video = await db.Entities.SingleAsync(entity => entity.Id == videoId);
        Assert.Equal(movie.Id, video.ParentEntityId);
        Assert.Equal(0, video.SortOrder);
        Assert.Contains(db.EntityFiles, file =>
            file.EntityId == movie.Id &&
            file.Role == EntityFileRole.Source &&
            file.Path == "/media/Friendship");
        Assert.Contains(db.EntitySources, source =>
            source.EntityId == movie.Id &&
            source.Code == "folder" &&
            source.Value == "/media/Friendship");
        Assert.Equal("Movie description", (await db.EntityDescriptions.FindAsync([movie.Id]))?.Value);
        Assert.Null(movie.RatingValue);
        Assert.Contains(db.EntityDates, date =>
            date.EntityId == movie.Id &&
            date.Code == "release" &&
            date.Value == "2025-05-09");
        Assert.Contains(db.EntityRelationshipLinks, relationship =>
            relationship.EntityId == movie.Id &&
            relationship.RelationshipCode == "studio");
        Assert.Contains(db.EntityRelationshipLinks, relationship =>
            relationship.EntityId == movie.Id &&
            relationship.RelationshipCode == "tags");
        Assert.Equal(2, db.EntityRelationshipLinks.Count(relationship =>
            relationship.EntityId == movie.Id &&
            relationship.RelationshipCode == "cast" &&
            relationship.MetadataJson!.Contains("performer")));
    }

    [Fact]
    public async Task UpsertVideosBatchClearsMovieParentWhenFileNoLongerClassifiesAsMovie() {
        await using var db = CreateContext();
        var rootId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var movieId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var videoId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        SeedSourceEntity(db, movieId, EntityKindRegistry.Movie.Code, "/media/Friendship");
        SeedSourceEntity(db, videoId, EntityKindRegistry.Video.Code, "/media/Friendship/Friendship.mp4", movieId, 0);
        db.VideoDetails.Add(new VideoDetailRow { EntityId = videoId, LibraryRootId = rootId });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var ids = await service.UpsertVideosBatchAsync([
            new VideoUpsertItem(
                "/media/Friendship/Friendship.mp4",
                "Friendship",
                rootId,
                IsNsfw: false)
        ], CancellationToken.None);

        Assert.Equal(videoId, Assert.Single(ids));
        var video = await db.Entities.FindAsync([videoId]);
        Assert.Null(video?.ParentEntityId);
        Assert.Null(video?.SortOrder);
    }

    [Fact]
    public async Task RemoveStaleMoviesByRootRemovesMissingMovieShells() {
        await using var db = CreateContext();
        var rootId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var staleMovieId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var staleVideoId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var keepMovieId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var keepVideoId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        SeedLibraryRoot(db, rootId, "/media/videos");
        SeedSourceEntity(db, staleMovieId, EntityKindRegistry.Movie.Code, "/media/videos/Stale");
        SeedSourceEntity(db, staleVideoId, EntityKindRegistry.Video.Code, "/media/videos/Stale/Stale.mp4", staleMovieId, 0);
        SeedSourceEntity(db, keepMovieId, EntityKindRegistry.Movie.Code, "/media/videos/Keep");
        SeedSourceEntity(db, keepVideoId, EntityKindRegistry.Video.Code, "/media/videos/Keep/Keep.mp4", keepMovieId, 0);
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var removed = await service.RemoveStaleMoviesByRootAsync(
            rootId,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "/media/videos/Keep" },
            CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.False(await db.Entities.AnyAsync(entity => entity.Id == staleMovieId));
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == staleVideoId));
        Assert.Null((await db.Entities.FindAsync([staleVideoId]))?.ParentEntityId);
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == keepMovieId));
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == keepVideoId));
    }

    [Fact]
    public async Task RemoveOrphanTagsRemovesOnlyUnreferencedTags() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var referencedTag = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var orphanTag = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var videoId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        db.Entities.AddRange(
            new EntityRow { Id = referencedTag, KindCode = EntityKindRegistry.Tag.Code, Title = "Used", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = orphanTag, KindCode = EntityKindRegistry.Tag.Code, Title = "Unused", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = videoId, KindCode = EntityKindRegistry.Video.Code, Title = "Film", CreatedAt = now, UpdatedAt = now });
        db.EntityRelationshipLinks.Add(new EntityRelationshipLinkRow {
            EntityId = videoId,
            RelationshipCode = "tags",
            Label = "Tags",
            TargetEntityId = referencedTag,
            TargetKindCode = EntityKindRegistry.Tag.Code,
            CreatedAt = now,
        });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var removed = await service.RemoveOrphanTagsAsync(CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.False(await db.Entities.AnyAsync(entity => entity.Id == orphanTag));
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == referencedTag));
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == videoId));
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
    public async Task UpsertGalleryStoresFolderParentAndSortOrder() {
        await using var db = CreateContext();
        var service = new LibraryScanPersistenceService(db);
        var rootId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var parentId = await service.UpsertGalleryAsync(
            "/media/images/Set",
            "Set",
            rootId,
            parentGalleryEntityId: null,
            sortOrder: 0,
            isNsfw: false,
            CancellationToken.None);

        var childId = await service.UpsertGalleryAsync(
            "/media/images/Set/Chapter 01",
            "Chapter 01",
            rootId,
            parentId,
            sortOrder: 7,
            isNsfw: true,
            CancellationToken.None);

        var child = await db.Entities.SingleAsync(entity => entity.Id == childId);
        var detail = await db.GalleryDetails.SingleAsync(row => row.EntityId == childId);
        Assert.Equal(parentId, child.ParentEntityId);
        Assert.Equal(7, child.SortOrder);
        Assert.Equal(rootId, detail.LibraryRootId);
        Assert.True(child.IsNsfw);
    }

    [Fact]
    public async Task UpsertAudioLibraryStoresFolderParentAndSortOrder() {
        await using var db = CreateContext();
        var service = new LibraryScanPersistenceService(db);
        var rootId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var parentId = await service.UpsertAudioLibraryAsync(
            "/media/audio/Albums",
            "Albums",
            rootId,
            parentEntityId: null,
            sortOrder: 0,
            isNsfw: false,
            CancellationToken.None);

        var childId = await service.UpsertAudioLibraryAsync(
            "/media/audio/Albums/Disc 01",
            "Disc 01",
            rootId,
            parentId,
            sortOrder: 3,
            isNsfw: false,
            CancellationToken.None);

        var child = await db.Entities.SingleAsync(entity => entity.Id == childId);
        var detail = await db.AudioLibraryDetails.SingleAsync(row => row.EntityId == childId);
        Assert.Equal(parentId, child.ParentEntityId);
        Assert.Equal(3, child.SortOrder);
        Assert.Equal(rootId, detail.LibraryRootId);
    }

    [Fact]
    public async Task UpsertImageCanRelinkExistingImageBackToLooseRootFile() {
        await using var db = CreateContext();
        var service = new LibraryScanPersistenceService(db);
        var galleryId = await service.UpsertGalleryAsync(
            "/media/images/Gallery",
            "Gallery",
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            parentGalleryEntityId: null,
            sortOrder: 0,
            isNsfw: false,
            CancellationToken.None);
        var imageId = await service.UpsertImageAsync(
            "/media/images/cover.jpg",
            "cover",
            galleryId,
            sizeBytes: 12,
            sortOrder: 4,
            isNsfw: false,
            CancellationToken.None);

        var relinkedId = await service.UpsertImageAsync(
            "/media/images/cover.jpg",
            "cover",
            galleryEntityId: null,
            sizeBytes: 12,
            sortOrder: 0,
            isNsfw: false,
            CancellationToken.None);

        var image = await db.Entities.SingleAsync(entity => entity.Id == imageId);
        Assert.Equal(imageId, relinkedId);
        Assert.Null(image.ParentEntityId);
        Assert.Null(image.SortOrder);
    }

    [Fact]
    public async Task UpsertAudioTrackCanRelinkExistingTrackBackToLooseRootFile() {
        await using var db = CreateContext();
        var service = new LibraryScanPersistenceService(db);
        var libraryId = await service.UpsertAudioLibraryAsync(
            "/media/audio/Album",
            "Album",
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            parentEntityId: null,
            sortOrder: 0,
            isNsfw: false,
            CancellationToken.None);
        var trackId = await service.UpsertAudioTrackAsync(
            "/media/audio/song.flac",
            "song",
            libraryId,
            sortOrder: 2,
            sectionLabel: null,
            sectionOrder: 0,
            isNsfw: false,
            CancellationToken.None);

        var relinkedId = await service.UpsertAudioTrackAsync(
            "/media/audio/song.flac",
            "song",
            audioLibraryId: null,
            sortOrder: 0,
            sectionLabel: null,
            sectionOrder: 0,
            isNsfw: false,
            CancellationToken.None);

        var track = await db.Entities.SingleAsync(entity => entity.Id == trackId);
        Assert.Equal(trackId, relinkedId);
        Assert.Null(track.ParentEntityId);
        Assert.Null(track.SortOrder);
    }

    [Fact]
    public async Task UpsertAudioTrackPreservesOrganizedTitleOnRescan() {
        await using var db = CreateContext();
        var service = new LibraryScanPersistenceService(db);
        var trackId = await service.UpsertAudioTrackAsync(
            "/media/audio/song.flac",
            "song",
            audioLibraryId: null,
            sortOrder: 0,
            sectionLabel: null,
            sectionOrder: 0,
            isNsfw: false,
            CancellationToken.None);
        var track = await db.Entities.SingleAsync(entity => entity.Id == trackId);
        track.Title = "Identified Song Title";
        track.IsOrganized = true;
        await db.SaveChangesAsync();

        var rescannedId = await service.UpsertAudioTrackAsync(
            "/media/audio/song.flac",
            "song",
            audioLibraryId: null,
            sortOrder: 0,
            sectionLabel: "Disc 1",
            sectionOrder: 1,
            isNsfw: true,
            CancellationToken.None);

        var rescannedTrack = await db.Entities.SingleAsync(entity => entity.Id == trackId);
        var detail = await db.AudioTrackDetails.SingleAsync(row => row.EntityId == trackId);
        Assert.Equal(trackId, rescannedId);
        Assert.Equal("Identified Song Title", rescannedTrack.Title);
        Assert.True(rescannedTrack.IsOrganized);
        Assert.True(rescannedTrack.IsNsfw);
        Assert.Equal("Disc 1", detail.SectionLabel);
        Assert.Equal(1, detail.SectionOrder);
    }

    [Fact]
    public async Task UpsertAudioTrackUpdatesUnorganizedTitleOnRescan() {
        await using var db = CreateContext();
        var service = new LibraryScanPersistenceService(db);
        var trackId = await service.UpsertAudioTrackAsync(
            "/media/audio/song.flac",
            "song",
            audioLibraryId: null,
            sortOrder: 0,
            sectionLabel: null,
            sectionOrder: 0,
            isNsfw: false,
            CancellationToken.None);

        await service.UpsertAudioTrackAsync(
            "/media/audio/song.flac",
            "Better Tag Title",
            audioLibraryId: null,
            sortOrder: 0,
            sectionLabel: null,
            sectionOrder: 0,
            isNsfw: false,
            CancellationToken.None);

        var track = await db.Entities.SingleAsync(entity => entity.Id == trackId);
        Assert.Equal("Better Tag Title", track.Title);
        Assert.False(track.IsOrganized);
    }

    [Fact]
    public async Task RemoveStaleLooseImagesInRootRemovesOnlyMissingRootLevelImages() {
        await using var db = CreateContext();
        var rootId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var staleLooseId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var validLooseId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var containedId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var outsideId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var galleryId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var subfolderOrphanId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        SeedLibraryRoot(db, rootId, "/media/images");
        SeedSourceEntity(db, galleryId, EntityKindRegistry.Gallery.Code, "/media/images/Gallery");
        SeedSourceEntity(db, staleLooseId, EntityKindRegistry.Image.Code, "/media/images/stale.jpg");
        SeedSourceEntity(db, validLooseId, EntityKindRegistry.Image.Code, "/media/images/valid.jpg");
        SeedSourceEntity(db, containedId, EntityKindRegistry.Image.Code, "/media/images/Gallery/contained.jpg", galleryId, 0);
        SeedSourceEntity(db, outsideId, EntityKindRegistry.Image.Code, "/other/stale.jpg");
        SeedSourceEntity(db, subfolderOrphanId, EntityKindRegistry.Image.Code, "/media/images/Sub/orphan.png");
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var removed = await service.RemoveStaleLooseImagesInRootAsync(
            rootId,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "/media/images/valid.jpg" },
            CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.False(await db.Entities.AnyAsync(entity => entity.Id == staleLooseId));
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == validLooseId));
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == containedId));
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == outsideId));
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == subfolderOrphanId));
    }

    [Fact]
    public async Task RemoveStaleLooseAudioTracksInRootRemovesOnlyMissingRootLevelTracks() {
        await using var db = CreateContext();
        var rootId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var staleLooseId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var validLooseId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var containedId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var outsideId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var libraryId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var subfolderOrphanId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        SeedLibraryRoot(db, rootId, "/media/audio");
        SeedSourceEntity(db, libraryId, EntityKindRegistry.AudioLibrary.Code, "/media/audio/Album");
        SeedSourceEntity(db, staleLooseId, EntityKindRegistry.AudioTrack.Code, "/media/audio/stale.flac");
        SeedSourceEntity(db, validLooseId, EntityKindRegistry.AudioTrack.Code, "/media/audio/valid.flac");
        SeedSourceEntity(db, containedId, EntityKindRegistry.AudioTrack.Code, "/media/audio/Album/contained.flac", libraryId, 0);
        SeedSourceEntity(db, outsideId, EntityKindRegistry.AudioTrack.Code, "/other/stale.flac");
        SeedSourceEntity(db, subfolderOrphanId, EntityKindRegistry.AudioTrack.Code, "/media/audio/Sub/orphan.flac");
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var removed = await service.RemoveStaleLooseAudioTracksInRootAsync(
            rootId,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "/media/audio/valid.flac" },
            CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.False(await db.Entities.AnyAsync(entity => entity.Id == staleLooseId));
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == validLooseId));
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == containedId));
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == outsideId));
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == subfolderOrphanId));
    }

    [Fact]
    public async Task RemoveStaleGalleriesInRootRemovesStaleFolderSubtree() {
        await using var db = CreateContext();
        var service = new LibraryScanPersistenceService(db);
        var rootId = Guid.Parse("77777777-7777-7777-7777-777777777777");

        var staleGalleryId = await service.UpsertGalleryAsync(
            "/media/images/Set",
            "Set",
            rootId,
            parentGalleryEntityId: null,
            sortOrder: 0,
            isNsfw: false,
            CancellationToken.None);
        var staleImageId = await service.UpsertImageAsync(
            "/media/images/Set/stale.jpg",
            "stale",
            staleGalleryId,
            sizeBytes: 12,
            sortOrder: 0,
            isNsfw: false,
            CancellationToken.None);
        var nestedGalleryId = await service.UpsertGalleryAsync(
            "/media/images/Set/Chapter 01",
            "Chapter 01",
            rootId,
            staleGalleryId,
            sortOrder: 1,
            isNsfw: false,
            CancellationToken.None);
        var nestedImageId = await service.UpsertImageAsync(
            "/media/images/Set/Chapter 01/nested.jpg",
            "nested",
            nestedGalleryId,
            sizeBytes: 34,
            sortOrder: 0,
            isNsfw: false,
            CancellationToken.None);
        var validGalleryId = await service.UpsertGalleryAsync(
            "/media/images/Keep",
            "Keep",
            rootId,
            parentGalleryEntityId: null,
            sortOrder: 1,
            isNsfw: false,
            CancellationToken.None);
        var validImageId = await service.UpsertImageAsync(
            "/media/images/Keep/valid.jpg",
            "valid",
            validGalleryId,
            sizeBytes: 56,
            sortOrder: 0,
            isNsfw: false,
            CancellationToken.None);

        var removed = await service.RemoveStaleGalleriesInRootAsync(
            rootId,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "/media/images/Keep" },
            CancellationToken.None);

        Assert.Equal(4, removed);
        Assert.False(await db.Entities.AnyAsync(entity => entity.Id == staleGalleryId));
        Assert.False(await db.Entities.AnyAsync(entity => entity.Id == staleImageId));
        Assert.False(await db.Entities.AnyAsync(entity => entity.Id == nestedGalleryId));
        Assert.False(await db.Entities.AnyAsync(entity => entity.Id == nestedImageId));
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == validGalleryId));
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == validImageId));
    }

    [Fact]
    public async Task RescanMigratesSingleImageGalleryByReparentingThenRemovingGallery() {
        await using var db = CreateContext();
        var service = new LibraryScanPersistenceService(db);
        var rootId = Guid.Parse("a1a1a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1");

        // Seed the library as it was scanned under the old rule: "Solo" is a one-image gallery nested
        // under the surviving "Set" gallery.
        var setGalleryId = await service.UpsertGalleryAsync(
            "/media/images/Set", "Set", rootId, parentGalleryEntityId: null, sortOrder: 0, isNsfw: false, CancellationToken.None);
        var soloGalleryId = await service.UpsertGalleryAsync(
            "/media/images/Set/Solo", "Solo", rootId, setGalleryId, sortOrder: 0, isNsfw: false, CancellationToken.None);
        var soloImageId = await service.UpsertImageAsync(
            "/media/images/Set/Solo/only.jpg", "only", soloGalleryId, sizeBytes: 12, sortOrder: 0, isNsfw: false, CancellationToken.None);

        // The new scan reparents the lone image to the survivor first, then drops the collapsed folder
        // from the valid gallery set.
        await service.UpsertImageAsync(
            "/media/images/Set/Solo/only.jpg", "only", setGalleryId, sizeBytes: 12, sortOrder: 1, isNsfw: false, CancellationToken.None);
        var removed = await service.RemoveStaleGalleriesInRootAsync(
            rootId,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "/media/images/Set" },
            CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.False(await db.Entities.AnyAsync(entity => entity.Id == soloGalleryId));
        var image = await db.Entities.SingleAsync(entity => entity.Id == soloImageId);
        Assert.Equal(setGalleryId, image.ParentEntityId);
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == setGalleryId));
    }

    [Fact]
    public async Task RescanMigratesSingleImageGalleryToLooseImage() {
        await using var db = CreateContext();
        var service = new LibraryScanPersistenceService(db);
        var rootId = Guid.Parse("b2b2b2b2-b2b2-b2b2-b2b2-b2b2b2b2b2b2");

        // Seed a one-image gallery directly under the root with no surviving ancestor.
        var soloGalleryId = await service.UpsertGalleryAsync(
            "/media/images/Solo", "Solo", rootId, parentGalleryEntityId: null, sortOrder: 0, isNsfw: false, CancellationToken.None);
        var soloImageId = await service.UpsertImageAsync(
            "/media/images/Solo/only.jpg", "only", soloGalleryId, sizeBytes: 12, sortOrder: 0, isNsfw: false, CancellationToken.None);

        // The new scan makes the image loose, then removes the now-empty gallery folder.
        await service.UpsertImageAsync(
            "/media/images/Solo/only.jpg", "only", galleryEntityId: null, sizeBytes: 12, sortOrder: 0, isNsfw: false, CancellationToken.None);
        var removed = await service.RemoveStaleGalleriesInRootAsync(
            rootId,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.False(await db.Entities.AnyAsync(entity => entity.Id == soloGalleryId));
        var image = await db.Entities.SingleAsync(entity => entity.Id == soloImageId);
        Assert.Null(image.ParentEntityId);
    }

    [Fact]
    public async Task RemoveStaleAudioLibrariesInRootRemovesStaleFolderSubtree() {
        await using var db = CreateContext();
        var service = new LibraryScanPersistenceService(db);
        var rootId = Guid.Parse("88888888-8888-8888-8888-888888888888");

        var staleLibraryId = await service.UpsertAudioLibraryAsync(
            "/media/audio/Album",
            "Album",
            rootId,
            parentEntityId: null,
            sortOrder: 0,
            isNsfw: false,
            CancellationToken.None);
        var staleTrackId = await service.UpsertAudioTrackAsync(
            "/media/audio/Album/stale.flac",
            "stale",
            staleLibraryId,
            sortOrder: 0,
            sectionLabel: null,
            sectionOrder: 0,
            isNsfw: false,
            CancellationToken.None);
        var nestedLibraryId = await service.UpsertAudioLibraryAsync(
            "/media/audio/Album/Disc 02",
            "Disc 02",
            rootId,
            staleLibraryId,
            sortOrder: 1,
            isNsfw: false,
            CancellationToken.None);
        var nestedTrackId = await service.UpsertAudioTrackAsync(
            "/media/audio/Album/Disc 02/nested.flac",
            "nested",
            nestedLibraryId,
            sortOrder: 0,
            sectionLabel: null,
            sectionOrder: 0,
            isNsfw: false,
            CancellationToken.None);
        var validLibraryId = await service.UpsertAudioLibraryAsync(
            "/media/audio/Keep",
            "Keep",
            rootId,
            parentEntityId: null,
            sortOrder: 1,
            isNsfw: false,
            CancellationToken.None);
        var validTrackId = await service.UpsertAudioTrackAsync(
            "/media/audio/Keep/valid.flac",
            "valid",
            validLibraryId,
            sortOrder: 0,
            sectionLabel: null,
            sectionOrder: 0,
            isNsfw: false,
            CancellationToken.None);

        var removed = await service.RemoveStaleAudioLibrariesInRootAsync(
            rootId,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "/media/audio/Keep" },
            CancellationToken.None);

        Assert.Equal(4, removed);
        Assert.False(await db.Entities.AnyAsync(entity => entity.Id == staleLibraryId));
        Assert.False(await db.Entities.AnyAsync(entity => entity.Id == staleTrackId));
        Assert.False(await db.Entities.AnyAsync(entity => entity.Id == nestedLibraryId));
        Assert.False(await db.Entities.AnyAsync(entity => entity.Id == nestedTrackId));
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == validLibraryId));
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == validTrackId));
    }

    [Fact]
    public async Task RemoveStaleGalleriesInRootRemovesOldRootGalleryWithMissingChild() {
        await using var db = CreateContext();
        var service = new LibraryScanPersistenceService(db);
        var rootId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var oldRootGalleryId = await service.UpsertGalleryAsync(
            "/media/images",
            "images",
            rootId,
            parentGalleryEntityId: null,
            sortOrder: 0,
            isNsfw: false,
            CancellationToken.None);
        var missingImageId = await service.UpsertImageAsync(
            "/media/images/missing.jpg",
            "missing",
            oldRootGalleryId,
            sizeBytes: 12,
            sortOrder: 0,
            isNsfw: false,
            CancellationToken.None);
        var validLooseImageId = await service.UpsertImageAsync(
            "/media/images/valid.jpg",
            "valid",
            galleryEntityId: null,
            sizeBytes: 34,
            sortOrder: 0,
            isNsfw: false,
            CancellationToken.None);

        var removed = await service.RemoveStaleGalleriesInRootAsync(
            rootId,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            CancellationToken.None);

        Assert.Equal(2, removed);
        Assert.False(await db.Entities.AnyAsync(entity => entity.Id == oldRootGalleryId));
        Assert.False(await db.Entities.AnyAsync(entity => entity.Id == missingImageId));
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == validLooseImageId));
    }

    [Fact]
    public async Task RemoveStaleAudioLibrariesInRootRemovesOldRootLibraryWithMissingChild() {
        await using var db = CreateContext();
        var service = new LibraryScanPersistenceService(db);
        var rootId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var oldRootLibraryId = await service.UpsertAudioLibraryAsync(
            "/media/audio",
            "audio",
            rootId,
            parentEntityId: null,
            sortOrder: 0,
            isNsfw: false,
            CancellationToken.None);
        var missingTrackId = await service.UpsertAudioTrackAsync(
            "/media/audio/missing.flac",
            "missing",
            oldRootLibraryId,
            sortOrder: 0,
            sectionLabel: null,
            sectionOrder: 0,
            isNsfw: false,
            CancellationToken.None);
        var validLooseTrackId = await service.UpsertAudioTrackAsync(
            "/media/audio/valid.flac",
            "valid",
            audioLibraryId: null,
            sortOrder: 0,
            sectionLabel: null,
            sectionOrder: 0,
            isNsfw: false,
            CancellationToken.None);

        var removed = await service.RemoveStaleAudioLibrariesInRootAsync(
            rootId,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            CancellationToken.None);

        Assert.Equal(2, removed);
        Assert.False(await db.Entities.AnyAsync(entity => entity.Id == oldRootLibraryId));
        Assert.False(await db.Entities.AnyAsync(entity => entity.Id == missingTrackId));
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == validLooseTrackId));
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

    [Fact]
    public async Task UpsertSingleFileBookCanAttachToFolderBackedBookSeries() {
        await using var db = CreateContext();
        var service = new LibraryScanPersistenceService(db);
        var rootId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var seriesId = await service.UpsertBookSeriesAsync(
            "/media/books/Game of Thrones",
            "Game of Thrones",
            rootId,
            isNsfw: false,
            BookType.Book,
            BookFormat.Pdf,
            CancellationToken.None);

        var bookId = await service.UpsertSingleFileBookAsync(
            "/media/books/Game of Thrones/01 - A Game of Thrones.pdf",
            "A Game of Thrones",
            rootId,
            isNsfw: false,
            BookType.Book,
            BookFormat.Pdf,
            Prismedia.Contracts.Media.MediaContentTypes.Pdf,
            seriesId,
            sortOrder: 0,
            CancellationToken.None);

        var series = await db.Entities.FindAsync([seriesId]);
        var book = await db.Entities.FindAsync([bookId]);
        var seriesDetail = await db.BookDetails.FindAsync([seriesId]);
        var detail = await db.BookDetails.FindAsync([bookId]);
        Assert.Equal(EntityKindRegistry.Book.Code, series!.KindCode);
        Assert.Null(series.ParentEntityId);
        Assert.Equal(BookType.Book, seriesDetail!.BookType);
        Assert.Equal(BookFormat.Pdf, seriesDetail.Format);
        Assert.Equal(seriesId, book!.ParentEntityId);
        Assert.Equal(0, book.SortOrder);
        Assert.Equal(BookFormat.Pdf, detail!.Format);
    }

    [Fact]
    public async Task UpsertBookSeriesReparentsExistingFlatSingleFileBooksUnderFolder() {
        await using var db = CreateContext();
        var service = new LibraryScanPersistenceService(db);
        var rootId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var firstBookId = await service.UpsertSingleFileBookAsync(
            "/media/books/Game of Thrones/A Song of Ice and Fire - vol 1 - A Game of Thrones.epub",
            "A Game of Thrones",
            rootId,
            isNsfw: false,
            BookType.Novel,
            BookFormat.Epub,
            Prismedia.Contracts.Media.MediaContentTypes.Epub,
            parentBookEntityId: null,
            sortOrder: null,
            CancellationToken.None);
        var secondBookId = await service.UpsertSingleFileBookAsync(
            "/media/books/Game of Thrones/A Song of Ice and Fire - vol 2 - A Clash of Kings.epub",
            "A Clash of Kings",
            rootId,
            isNsfw: false,
            BookType.Novel,
            BookFormat.Epub,
            Prismedia.Contracts.Media.MediaContentTypes.Epub,
            parentBookEntityId: null,
            sortOrder: null,
            CancellationToken.None);

        var seriesId = await service.UpsertBookSeriesAsync(
            "/media/books/Game of Thrones",
            "Game of Thrones",
            rootId,
            isNsfw: false,
            BookType.Novel,
            BookFormat.Epub,
            CancellationToken.None);

        var firstBook = await db.Entities.FindAsync([firstBookId]);
        var secondBook = await db.Entities.FindAsync([secondBookId]);
        var seriesDetail = await db.BookDetails.FindAsync([seriesId]);
        Assert.Equal(seriesId, firstBook!.ParentEntityId);
        Assert.Equal(0, firstBook.SortOrder);
        Assert.Equal(seriesId, secondBook!.ParentEntityId);
        Assert.Equal(1, secondBook.SortOrder);
        Assert.Equal(BookType.Novel, seriesDetail!.BookType);
        Assert.Equal(BookFormat.Epub, seriesDetail.Format);
    }

    [Fact]
    public async Task UpsertBookAsyncCorrectsExistingArchiveBookClassification() {
        await using var db = CreateContext();
        var service = new LibraryScanPersistenceService(db);
        var bookId = Guid.Parse("bbbbbbbb-1111-1111-1111-111111111111");
        const string sourcePath = "/media/comics/Always Go With the Flow";
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = bookId,
            KindCode = EntityKindRegistry.Book.Code,
            Title = "Always Go With the Flow",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.BookDetails.Add(new BookDetailRow {
            EntityId = bookId,
            BookType = BookType.Book,
            Format = BookFormat.Pdf
        });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = bookId,
            Role = EntityFileRole.Source,
            Path = sourcePath,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var result = await service.UpsertBookAsync(
            sourcePath,
            "Always Go With the Flow!",
            RootId,
            isNsfw: false,
            CancellationToken.None);

        Assert.Equal(bookId, result);
        var book = await db.Entities.FindAsync([bookId]);
        var detail = await db.BookDetails.FindAsync([bookId]);
        Assert.Equal("Always Go With the Flow!", book!.Title);
        Assert.Equal(BookType.Comic, detail!.BookType);
        Assert.Equal(BookFormat.ImageArchive, detail.Format);
        Assert.Equal(RootId, detail.LibraryRootId);
    }

    [Fact]
    public async Task ApplyVideoSidecarMetadataFillsMissingFieldsAndKeepsExistingDescription() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
        SeedVideo(db, videoId, "/media/movie.mkv");
        var video = await db.Entities.FindAsync([videoId]);
        video!.Title = "movie";
        db.EntityDescriptions.Add(new EntityDescriptionRow {
            EntityId = videoId,
            Value = "User description",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        await service.ApplyVideoSidecarMetadataAsync(
            videoId,
            new VideoSidecarMetadata {
                Title = "Sidecar Title",
                Description = "Sidecar description",
                Date = "2026-05-01",
                Studio = "Sidecar Studio",
                Tags = ["Noir"],
                Performers = ["Ada Actor"],
                Urls = ["https://example.test/video"]
            },
            "movie",
            markNsfw: true,
            CancellationToken.None);

        Assert.Equal("Sidecar Title", video.Title);
        Assert.Null(video.RatingValue);
        Assert.Equal("User description", (await db.EntityDescriptions.FindAsync([videoId]))?.Value);
        var release = await db.EntityDates.FindAsync([videoId, "release"]);
        Assert.Equal("2026-05-01", release?.Value);
        Assert.Equal(new DateOnly(2026, 5, 1), release?.SortableValue);
        Assert.Equal(["https://example.test/video"], db.EntityUrls.Where(row => row.EntityId == videoId).Select(row => row.Url).ToArray());
        Assert.Contains(db.EntityRelationshipLinks, row => row.EntityId == videoId && row.RelationshipCode == "tags");
        Assert.Contains(db.EntityRelationshipLinks, row => row.EntityId == videoId && row.RelationshipCode == "studio");
        Assert.Contains(db.EntityRelationshipLinks, row => row.EntityId == videoId && row.RelationshipCode == "cast" && row.MetadataJson!.Contains("performer"));
        Assert.Contains(db.Entities, row => row.KindCode == EntityKindRegistry.Tag.Code && row.Title == "Noir" && row.IsNsfw);
    }

    [Fact]
    public async Task ApplyComicInfoMetadataAddsMetadataWithoutOverwritingExistingBookTitle() {
        await using var db = CreateContext();
        var bookId = Guid.Parse("bbbbbbbb-1111-1111-1111-111111111111");
        db.Entities.Add(new EntityRow {
            Id = bookId,
            KindCode = EntityKindRegistry.Book.Code,
            Title = "User Book Title",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.BookDetails.Add(new BookDetailRow {
            EntityId = bookId,
            BookType = BookType.Comic
        });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        await service.ApplyComicInfoMetadataAsync(
            bookId,
            new ComicInfoMetadata {
                Series = "ComicInfo Series",
                Summary = "ComicInfo summary",
                Date = "2026-05",
                Publisher = "Comic Publisher",
                Tags = ["Drama"],
                Creators = ["Ada Writer"],
                Urls = ["https://example.test/comic"],
                MarksNsfw = true
            },
            markNsfw: true,
            CancellationToken.None);

        var book = await db.Entities.FindAsync([bookId]);
        Assert.Equal("User Book Title", book!.Title);
        Assert.True(book.IsNsfw);
        Assert.Equal("ComicInfo summary", (await db.EntityDescriptions.FindAsync([bookId]))?.Value);
        Assert.Equal("2026-05", (await db.EntityDates.FindAsync([bookId, "release"]))?.Value);
        Assert.Equal(["https://example.test/comic"], db.EntityUrls.Where(row => row.EntityId == bookId).Select(row => row.Url).ToArray());
        Assert.Contains(db.EntityRelationshipLinks, row => row.EntityId == bookId && row.RelationshipCode == "tags");
        Assert.Contains(db.EntityRelationshipLinks, row => row.EntityId == bookId && row.RelationshipCode == "studio");
        Assert.Contains(db.EntityRelationshipLinks, row => row.EntityId == bookId && row.RelationshipCode == "cast" && row.MetadataJson!.Contains("creator"));
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

    private static void SeedLibraryRoot(PrismediaDbContext db, Guid rootId, string path) {
        db.LibraryRoots.Add(new LibraryRootRow {
            Id = rootId,
            Path = path,
            Label = Path.GetFileName(path),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }

    private static void SeedSourceEntity(
        PrismediaDbContext db,
        Guid entityId,
        string kindCode,
        string sourcePath,
        Guid? parentEntityId = null,
        int? sortOrder = null) {
        db.Entities.Add(new EntityRow {
            Id = entityId,
            KindCode = kindCode,
            Title = Path.GetFileNameWithoutExtension(sourcePath),
            ParentEntityId = parentEntityId,
            SortOrder = sortOrder,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Role = EntityFileRole.Source,
            Path = sourcePath,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }
}
