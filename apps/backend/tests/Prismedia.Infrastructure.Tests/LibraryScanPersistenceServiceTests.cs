using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers.Probe;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Media.Persistence;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace Prismedia.Infrastructure.Tests;

public sealed class LibraryScanPersistenceServiceTests {
    private static readonly Guid RootId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task SuccessfulFirstAudioProbePersistsMediaSourceAndSchedulesDefinitionPreview() {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"prismedia-audio-probe-{Guid.NewGuid():N}.flac");
        await File.WriteAllBytesAsync(sourcePath, [0x66, 0x4c, 0x61, 0x43]);
        try {
            await using var db = CreateContext();
            var trackId = Guid.NewGuid();
            SeedSourceEntity(db, trackId, EntityKind.AudioTrack.ToCode(), sourcePath);
            db.AudioTrackDetails.Add(new AudioTrackDetailRow { EntityId = trackId });
            await db.SaveChangesAsync();
            var persistence = new LibraryScanPersistenceService(db);
            var handler = new ProbeAudioJobHandler(
                NullLogger<ProbeAudioJobHandler>.Instance,
                new FixedAudioProbe(),
                persistence,
                persistence,
                persistence);
            var job = new JobRunSnapshot(
                Guid.NewGuid(),
                JobType.ProbeAudio,
                JobRunStatus.Running,
                0,
                null,
                "{}",
                EntityKind.AudioTrack.ToCode(),
                trackId.ToString(),
                "Track",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null);

            var queue = new MergedImportTestSupport.RecordingJobQueue();
            await handler.HandleAsync(new JobContext(job, queue), CancellationToken.None);

            var source = await db.MediaSources.AsNoTracking().SingleAsync(row => row.EntityId == trackId);
            Assert.Equal(sourcePath, source.Path);
            Assert.Equal("flac", source.AudioCodec);
            var stream = await db.MediaStreams.AsNoTracking().SingleAsync(row => row.MediaSourceId == source.Id);
            Assert.Equal("Audio", stream.Type);
            Assert.Equal("flac", stream.Codec);
            var needs = await persistence.CheckDownstreamNeedsBatchAsync([trackId], CancellationToken.None);
            Assert.False(needs[trackId].NeedsProbe);
            Assert.Collection(queue.Enqueued, request => {
                Assert.Equal(
                    EntityKindRegistry.Describe(EntityKind.AudioTrack).Processing.PreviewJobType,
                    request.Type);
                Assert.Equal(EntityKind.AudioTrack.ToCode(), request.TargetEntityKind);
                Assert.Equal(trackId.ToString(), request.TargetEntityId);
            });
        } finally {
            File.Delete(sourcePath);
        }
    }

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
    public async Task UnreadableVideoStillAllowsIndependentSidecarReconciliation() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("77777777-7777-7777-7777-777777777771");
        SeedVideo(db, videoId);
        db.EntityTechnical.Add(new EntityTechnicalRow {
            EntityId = videoId,
            ProbeFailedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var needs = await service.CheckDownstreamNeedsBatchAsync([videoId], CancellationToken.None);

        Assert.False(needs[videoId].NeedsProbe);
        Assert.False(needs[videoId].NeedsPreview);
        Assert.False(needs[videoId].NeedsTrickplay);
        Assert.True(needs[videoId].NeedsSubtitleExtraction);
        // Fingerprints hash the raw file, which works regardless of media corruption.
        Assert.True(needs[videoId].MissingOshash);
        Assert.True(needs[videoId].MissingMd5);
    }

    [Fact]
    public async Task ClearProbeFailuresForPathsGivesChangedFileAFreshProbingChance() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("77777777-7777-7777-7777-777777777772");
        const string sourcePath = "/media/shows/corrupt-episode.mp4";
        SeedVideo(db, videoId, sourcePath);
        db.EntitySubtitleStates.Add(new EntitySubtitleStateRow {
            EntityId = videoId,
            SubtitlesExtractedAt = DateTimeOffset.UtcNow,
            SubtitleSidecarSignature = new string('a', 64)
        });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        await service.MarkEntityProbeFailedAsync(videoId, CancellationToken.None);

        var suppressed = await service.CheckDownstreamNeedsBatchAsync([videoId], CancellationToken.None);
        Assert.False(suppressed[videoId].NeedsProbe);
        Assert.False(suppressed[videoId].NeedsSubtitleExtraction);

        await service.ClearProbeFailuresForPathsAsync([sourcePath], CancellationToken.None);
        await service.ClearManagedSubtitleCompletionForPathsAsync([sourcePath], CancellationToken.None);

        var restored = await service.CheckDownstreamNeedsBatchAsync([videoId], CancellationToken.None);
        Assert.True(restored[videoId].NeedsProbe);
        Assert.True(restored[videoId].NeedsSubtitleExtraction);
        var detail = await db.EntitySubtitleStates.FindAsync([videoId]);
        Assert.Null(detail!.SubtitlesExtractedAt);
        Assert.Equal(new string('a', 64), detail.SubtitleSidecarSignature);
    }

    [Fact]
    public async Task SuccessfulTechnicalUpsertClearsProbeFailureMarker() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("77777777-7777-7777-7777-777777777773");
        SeedVideo(db, videoId);
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        await service.MarkEntityProbeFailedAsync(videoId, CancellationToken.None);
        await service.UpsertEntityTechnicalAsync(
            videoId, 120, 1920, 1080, null, null, null, null, "h264", "mp4", null, CancellationToken.None);

        var technical = await service.GetEntityTechnicalAsync(videoId, CancellationToken.None);
        Assert.NotNull(technical);
        Assert.Null(technical.ProbeFailedAt);
        Assert.Equal(120, technical.DurationSeconds);
    }

    [Fact]
    public async Task TechnicalUpsertRejectsEntityOwnedByDestructiveLifecycle() {
        await using var db = CreateContext();
        var videoId = Guid.NewGuid();
        SeedVideo(db, videoId);
        var video = db.Entities.Local.Single(row => row.Id == videoId);
        video.LifecycleClaimKind = EntityLifecycleClaimKind.DeletingFiles;
        video.LifecycleClaimId = Guid.NewGuid();
        video.LifecycleClaimedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);

        await Assert.ThrowsAsync<EntityLifecycleMutationConflictException>(() =>
            service.UpsertEntityTechnicalAsync(
                videoId, 120, 1920, 1080, null, null, null, null, "h264", "mp4", null,
                CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.False(await db.EntityTechnical.AnyAsync(row => row.EntityId == videoId));
    }

    [Fact]
    public async Task NewScannerChildRejectsClaimedExistingParentAcrossEntityKinds() {
        await using var db = CreateContext();
        var galleryId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        SeedLibraryRoot(db, rootId, "/media/deleting-gallery");
        db.Entities.Add(new EntityRow {
            Id = galleryId,
            KindCode = EntityKind.Gallery.ToCode(),
            Title = "Deleting gallery",
            LifecycleClaimKind = EntityLifecycleClaimKind.DeletingFiles,
            LifecycleClaimId = Guid.NewGuid(),
            LifecycleClaimedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);

        await Assert.ThrowsAsync<EntityLifecycleMutationConflictException>(() =>
            service.UpsertImageAsync(
                "/media/deleting-gallery/new-image.jpg",
                "New image",
                rootId,
                galleryId,
                42,
                1,
                false,
                CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.False(await db.Entities.AnyAsync(row => row.ParentEntityId == galleryId));
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
    public async Task DownstreamNeedsPreviewWhenStoredThumbnailFileIsMissing() {
        var cacheRoot = CreateCacheRoot();
        try {
            await using var db = CreateContext();
            var videoId = Guid.Parse("22222222-aaaa-1111-1111-111111111111");
            var thumbnailPath = $"/assets/videos/{videoId}/thumb.jpg";
            SeedVideo(db, videoId);
            db.EntityFiles.Add(new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = videoId,
                Role = EntityFileRole.Thumbnail,
                Path = thumbnailPath,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();

            var service = new LibraryScanPersistenceService(db, Assets(cacheRoot));
            var needs = await service.CheckDownstreamNeedsBatchAsync([videoId], CancellationToken.None);
            Assert.True(needs[videoId].NeedsPreview);

            WriteCacheFile(cacheRoot, thumbnailPath);
            needs = await service.CheckDownstreamNeedsBatchAsync([videoId], CancellationToken.None);
            Assert.False(needs[videoId].NeedsPreview);
        } finally {
            DeleteDirectory(cacheRoot);
        }
    }

    [Fact]
    public async Task DownstreamNeedsGridThumbnailWhenStoredGridFileIsMissing() {
        var cacheRoot = CreateCacheRoot();
        try {
            await using var db = CreateContext();
            var videoId = Guid.Parse("22222222-bbbb-1111-1111-111111111111");
            var thumbnailPath = $"/assets/videos/{videoId}/thumb.jpg";
            var gridPath = $"/assets/grid-thumbs/{videoId}.jpg";
            SeedVideo(db, videoId);
            db.EntityFiles.AddRange(
                new EntityFileRow {
                    Id = Guid.NewGuid(),
                    EntityId = videoId,
                    Role = EntityFileRole.Thumbnail,
                    Path = thumbnailPath,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                },
                new EntityFileRow {
                    Id = Guid.NewGuid(),
                    EntityId = videoId,
                    Role = EntityFileRole.GridThumbnail,
                    Path = gridPath,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            await db.SaveChangesAsync();
            WriteCacheFile(cacheRoot, thumbnailPath);

            var service = new LibraryScanPersistenceService(db, Assets(cacheRoot));
            var needs = await service.CheckDownstreamNeedsBatchAsync([videoId], CancellationToken.None);

            Assert.False(needs[videoId].NeedsPreview);
            Assert.True(needs[videoId].NeedsGridThumbnail);
        } finally {
            DeleteDirectory(cacheRoot);
        }
    }

    [Fact]
    public async Task DownstreamDoesNotRequestRasterVariantsForOriginalArtworkKinds() {
        var cacheRoot = CreateCacheRoot();
        try {
            await using var db = CreateContext();
            var studioId = Guid.Parse("22222222-bbbb-2222-2222-222222222222");
            var logoPath = $"/assets/custom/artwork/{studioId}/logo.svg";
            var now = DateTimeOffset.UtcNow;
            db.Entities.Add(new EntityRow {
                Id = studioId,
                KindCode = EntityKind.Studio.ToCode(),
                Title = "Original Logo Studio",
                CreatedAt = now,
                UpdatedAt = now
            });
            db.EntityFiles.Add(new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = studioId,
                Role = EntityFileRole.Logo,
                Path = logoPath,
                MimeType = "image/svg+xml",
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();
            WriteCacheFile(cacheRoot, logoPath);

            var service = new LibraryScanPersistenceService(db, Assets(cacheRoot));
            var needs = await service.CheckDownstreamNeedsBatchAsync([studioId], CancellationToken.None);

            Assert.False(needs[studioId].NeedsGridThumbnail);
        } finally {
            DeleteDirectory(cacheRoot);
        }
    }

    [Fact]
    public async Task HasEntityFileAsyncReturnsFalseWhenStoredAssetFileIsMissing() {
        var cacheRoot = CreateCacheRoot();
        try {
            await using var db = CreateContext();
            var imageId = Guid.Parse("22222222-cccc-1111-1111-111111111111");
            SeedSourceEntity(db, imageId, EntityKind.Image.ToCode(), "/media/images/photo.jpg");
            db.EntityFiles.Add(new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = imageId,
                Role = EntityFileRole.Thumbnail,
                Path = $"/assets/images/{imageId}/thumb.jpg",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();

            var service = new LibraryScanPersistenceService(db, Assets(cacheRoot));

            Assert.False(await service.HasEntityFileAsync(imageId, EntityFileRole.Thumbnail, CancellationToken.None));
        } finally {
            DeleteDirectory(cacheRoot);
        }
    }

    [Fact]
    public async Task DownstreamNeedsAudioPreviewUsesWaveformRole() {
        await using var db = CreateContext();
        var trackWithWaveformId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
        var trackWithThumbnailId = Guid.Parse("aaaaaaaa-2222-2222-2222-222222222222");
        SeedSourceEntity(db, trackWithWaveformId, EntityKind.AudioTrack.ToCode(), "/media/audio/with-waveform.m4a");
        SeedSourceEntity(db, trackWithThumbnailId, EntityKind.AudioTrack.ToCode(), "/media/audio/with-thumbnail.m4a");
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
        SeedSourceEntity(db, animatedImageId, EntityKind.Image.ToCode(), "/media/images/animated.webm");
        SeedSourceEntity(db, stillImageId, EntityKind.Image.ToCode(), "/media/images/photo.jpg");
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
        db.EntitySubtitleStates.Add(new EntitySubtitleStateRow {
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
    public async Task MissingManualSubtitleDoesNotRequeueManagedExtraction() {
        await using var db = CreateContext();
        var videoId = Guid.NewGuid();
        SeedVideo(db, videoId);
        db.EntitySubtitleStates.Add(new EntitySubtitleStateRow {
            EntityId = videoId,
            SubtitlesExtractedAt = DateTimeOffset.UtcNow
        });
        var manualId = Guid.NewGuid();
        db.EntitySubtitles.Add(new EntitySubtitleRow {
            Id = manualId,
            EntityId = videoId,
            Language = "eng",
            Format = "vtt",
            Source = EntitySubtitleSource.Manual,
            SourceKey = SubtitleSourceKeys.Capability(manualId),
            StoragePath = "/tmp/prismedia/missing-manual-subtitle.vtt",
            SourceFormat = "vtt",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var needs = await new LibraryScanPersistenceService(db)
            .CheckDownstreamNeedsBatchAsync([videoId], CancellationToken.None);

        Assert.False(needs[videoId].NeedsSubtitleExtraction);
    }

    [Fact]
    public async Task MissingStyledSidecarSourceRequeuesManagedExtraction() {
        var cacheRoot = CreateCacheRoot();
        try {
            await using var db = CreateContext();
            var videoId = Guid.NewGuid();
            var assets = Assets(cacheRoot);
            var subtitleDir = assets.SubtitleDir(videoId);
            Directory.CreateDirectory(subtitleDir);
            var storagePath = Path.Combine(subtitleDir, "sidecar-test.vtt");
            await File.WriteAllTextAsync(storagePath, "WEBVTT\n\n");
            SeedVideo(db, videoId);
            db.EntitySubtitleStates.Add(new EntitySubtitleStateRow {
                EntityId = videoId,
                SubtitlesExtractedAt = DateTimeOffset.UtcNow
            });
            db.EntitySubtitles.Add(new EntitySubtitleRow {
                Id = Guid.NewGuid(),
                EntityId = videoId,
                Language = "eng",
                Format = SubtitleFormats.Vtt,
                Source = EntitySubtitleSource.Sidecar,
                SourceKey = new string('a', 64),
                StoragePath = storagePath,
                SourceFormat = SubtitleFormats.Ass,
                SourcePath = Path.ChangeExtension(storagePath, SubtitleFileExtensions.Ass),
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();

            var needs = await new LibraryScanPersistenceService(db, assets)
                .CheckDownstreamNeedsBatchAsync([videoId], CancellationToken.None);

            Assert.True(needs[videoId].NeedsSubtitleExtraction);
        } finally {
            DeleteDirectory(cacheRoot);
        }
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
        Assert.Contains(subtitles, subtitle => subtitle.Language == "eng"
            && subtitle.SourceKey == "stream:3"
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
            && subtitle.Language == "eng"
            && subtitle.SourceKey == "stream:3"
            && subtitle.StoragePath == "/data/cache/videos/888/subtitles/embedded-eng-3.vtt"
            && subtitle.SourcePath == "3");
    }

    [Fact]
    public async Task ReconcileManagedSubtitlesKeepsStableIdentityAndValidDuplicateLanguages() {
        await using var db = CreateContext();
        var videoId = Guid.NewGuid();
        var sidecarId = Guid.NewGuid();
        var manualId = Guid.NewGuid();
        SeedVideo(db, videoId);
        db.EntitySubtitleStates.Add(new EntitySubtitleStateRow {
            EntityId = videoId,
            SubtitleSidecarSignature = "previous",
            SubtitlesExtractedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        db.EntitySubtitles.AddRange(
            new EntitySubtitleRow {
                Id = sidecarId,
                EntityId = videoId,
                Language = "und",
                Format = "vtt",
                Source = EntitySubtitleSource.Sidecar,
                SourceKey = "sidecar:one",
                StoragePath = "/cache/old-one.vtt",
                SourceFormat = "ass",
                SourcePath = "/cache/old-one.ass",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            },
            new EntitySubtitleRow {
                Id = Guid.NewGuid(),
                EntityId = videoId,
                Language = "eng",
                Format = "vtt",
                Source = EntitySubtitleSource.Embedded,
                SourceKey = "stream:9",
                StoragePath = "/cache/stale-embedded.vtt",
                SourceFormat = "subrip",
                SourcePath = "9",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            },
            new EntitySubtitleRow {
                Id = manualId,
                EntityId = videoId,
                Language = "eng",
                Format = "vtt",
                Source = EntitySubtitleSource.Manual,
                SourceKey = $"manual:{manualId:N}",
                StoragePath = "/cache/manual.vtt",
                SourceFormat = "vtt",
                CreatedAt = DateTimeOffset.UtcNow
            });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var result = await service.ReconcileManagedSubtitlesAsync(
            videoId,
            new string('a', 64),
            [
                new ManagedSubtitleTrackData(
                    EntitySubtitleSource.Sidecar, "sidecar:one", "und", "Forced", "vtt",
                    "/cache/new-one.vtt", "ass", "/cache/new-one.ass"),
                new ManagedSubtitleTrackData(
                    EntitySubtitleSource.Sidecar, "sidecar:two", "und", null, "vtt",
                    "/cache/two.vtt", "srt", null),
                new ManagedSubtitleTrackData(
                    EntitySubtitleSource.Embedded, "stream:3", "eng", "SDH", "vtt",
                    "/cache/embedded-3.vtt", "subrip", "3")
            ],
            isComplete: true,
            CancellationToken.None);

        var rows = db.EntitySubtitles.Where(row => row.EntityId == videoId).ToArray();
        Assert.Equal(4, rows.Length);
        Assert.Contains(rows, row => row.Id == sidecarId && row.SourceKey == "sidecar:one" && row.Language == "und");
        Assert.Contains(rows, row => row.SourceKey == "sidecar:two" && row.Language == "und");
        Assert.Contains(rows, row => row.SourceKey == "stream:3" && row.Language == "eng");
        Assert.Contains(rows, row => row.Id == manualId && row.Source == EntitySubtitleSource.Manual);
        Assert.DoesNotContain(rows, row => row.SourceKey == "stream:9");
        Assert.Contains("/cache/old-one.vtt", result.ObsoleteAssetPaths);
        Assert.Contains("/cache/old-one.ass", result.ObsoleteAssetPaths);
        Assert.Contains("/cache/stale-embedded.vtt", result.ObsoleteAssetPaths);
        var detail = await db.EntitySubtitleStates.FindAsync([videoId]);
        Assert.Equal(new string('a', 64), detail!.SubtitleSidecarSignature);
        Assert.NotNull(detail.SubtitlesExtractedAt);
    }

    [Fact]
    public async Task IncompleteSubtitleReconciliationKeepsValidRowsButStillNeedsExtraction() {
        await using var db = CreateContext();
        var videoId = Guid.NewGuid();
        SeedVideo(db, videoId);
        db.EntitySubtitleStates.Add(new EntitySubtitleStateRow {
            EntityId = videoId,
            SubtitleSidecarSignature = new string('0', 64),
            SubtitlesExtractedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        db.EntitySubtitles.Add(new EntitySubtitleRow {
            Id = Guid.NewGuid(),
            EntityId = videoId,
            Language = "fr",
            Format = SubtitleFormats.Vtt,
            Source = EntitySubtitleSource.Sidecar,
            SourceKey = new string('f', 64),
            StoragePath = "/cache/stale-failed-sidecar.vtt",
            SourceFormat = SubtitleFormats.Srt,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        await service.ReconcileManagedSubtitlesAsync(
            videoId,
            new string('a', 64),
            [new ManagedSubtitleTrackData(
                EntitySubtitleSource.Embedded, "stream:3", "en", null, SubtitleFormats.Vtt,
                "/cache/embedded-3.vtt", SubtitleFormats.Srt, "3")],
            isComplete: false,
            CancellationToken.None);

        var rows = await db.EntitySubtitles.Where(row => row.EntityId == videoId).ToListAsync();
        Assert.Single(rows);
        Assert.Equal("stream:3", rows[0].SourceKey);
        var detail = await db.EntitySubtitleStates.FindAsync([videoId]);
        Assert.Null(detail!.SubtitleSidecarSignature);
        Assert.Null(detail.SubtitlesExtractedAt);

        var needs = await service.CheckDownstreamNeedsBatchAsync([videoId], CancellationToken.None);
        Assert.True(needs[videoId].NeedsSubtitleExtraction);
    }

    [Fact]
    public async Task ReconcileManagedSubtitlesRejectsDestructiveLifecycleOwner() {
        await using var db = CreateContext();
        var videoId = Guid.NewGuid();
        SeedVideo(db, videoId);
        var video = db.Entities.Local.Single(row => row.Id == videoId);
        video.LifecycleClaimKind = EntityLifecycleClaimKind.DeletingFiles;
        video.LifecycleClaimId = Guid.NewGuid();
        video.LifecycleClaimedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        await Assert.ThrowsAsync<EntityLifecycleMutationConflictException>(() =>
            service.ReconcileManagedSubtitlesAsync(
                videoId,
                new string('b', 64),
                [new ManagedSubtitleTrackData(
                    EntitySubtitleSource.Sidecar, "sidecar:one", "eng", null, "vtt",
                    "/cache/one.vtt", "srt", null)],
                isComplete: true,
                CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.Empty(await db.EntitySubtitles.Where(row => row.EntityId == videoId).ToListAsync());
        Assert.Null(await db.EntitySubtitleStates.FindAsync([videoId]));
    }

    [Fact]
    public async Task SidecarSignatureChangeInvalidatesExtractionWithoutAdvancingSignature() {
        await using var db = CreateContext();
        var videoId = Guid.NewGuid();
        SeedLibraryRoot(db, RootId, "/media/videos");
        SeedVideo(db, videoId, "/media/videos/movie.mkv");
        db.EntitySubtitleStates.Add(new EntitySubtitleStateRow {
            EntityId = videoId,
            SubtitleSidecarSignature = "old",
            SubtitlesExtractedAt = DateTimeOffset.UtcNow
        });
        db.EntityLibraryRoots.Add(new EntityLibraryRootRow { EntityId = videoId, LibraryRootId = RootId });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        await service.InvalidateSubtitleStateAsync(
            [new VideoSubtitleSidecarState(videoId, "new")], CancellationToken.None);

        var detail = await db.EntitySubtitleStates.FindAsync([videoId]);
        Assert.Null(detail!.SubtitlesExtractedAt);
        Assert.Equal("old", detail.SubtitleSidecarSignature);
        var target = Assert.Single(await service.GetPlayableVideoTargetsInRootAsync(RootId, CancellationToken.None));
        Assert.Equal(videoId, target.Id);
        Assert.Equal("/media/videos/movie.mkv", target.SourcePath);
    }

    [Fact]
    public async Task RebindingVideoSourceClearsManagedSubtitleStateForReplacement() {
        await using var db = CreateContext();
        var videoId = Guid.NewGuid();
        const string previousPath = "/media/videos/movie.mkv";
        const string replacementPath = "/media/videos/movie.mp4";
        SeedVideo(db, videoId, previousPath);
        db.EntitySubtitleStates.Add(new EntitySubtitleStateRow {
            EntityId = videoId,
            SubtitleSidecarSignature = new string('a', 64),
            SubtitlesExtractedAt = DateTimeOffset.UtcNow
        });
        db.EntitySubtitles.Add(new EntitySubtitleRow {
            Id = Guid.NewGuid(),
            EntityId = videoId,
            Language = "en",
            Format = SubtitleFormats.Vtt,
            Source = EntitySubtitleSource.Sidecar,
            SourceKey = new string('b', 64),
            StoragePath = "/cache/old-sidecar.vtt",
            SourceFormat = SubtitleFormats.Srt,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var owners = await new LibraryScanPersistenceService(db).RebindPlayableVideoSourceAsync(
            previousPath,
            replacementPath,
            CancellationToken.None);

        Assert.Equal([videoId], owners);
        Assert.Empty(await db.EntitySubtitles.Where(row => row.EntityId == videoId).ToArrayAsync());
        var detail = await db.EntitySubtitleStates.FindAsync([videoId]);
        Assert.Null(detail!.SubtitlesExtractedAt);
        Assert.Null(detail.SubtitleSidecarSignature);
    }

    [Fact]
    public async Task PlayableVideoSourceOwnersExcludeNonVideoEntitiesSharingTheSameFile() {
        await using var db = CreateContext();
        var videoId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        const string sharedPath = "/media/mixed/animated.webm";
        SeedVideo(db, videoId, sharedPath);
        SeedSourceEntity(db, imageId, EntityKind.Image.ToCode(), sharedPath);
        await db.SaveChangesAsync();

        var owners = await new LibraryScanPersistenceService(db).ListPlayableVideoSourceOwnersAsync(
            [sharedPath],
            CancellationToken.None);

        var owner = Assert.Single(owners);
        Assert.Equal(videoId, owner.EntityId);
        Assert.Equal(sharedPath, owner.FilePath);
    }

    [Fact]
    public async Task RebindingPlayableVideoSourceLeavesNonPlayableCoOwnersUntouched() {
        await using var db = CreateContext();
        var playableId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        const string previousPath = "/media/mixed/animated.webm";
        const string replacementPath = "/media/mixed/animated.mp4";
        SeedVideo(db, playableId, previousPath);
        SeedSourceEntity(db, imageId, EntityKind.Image.ToCode(), previousPath);
        await db.SaveChangesAsync();

        var rebound = await new LibraryScanPersistenceService(db).RebindPlayableVideoSourceAsync(
            previousPath,
            replacementPath,
            CancellationToken.None);

        Assert.Equal([playableId], rebound);
        Assert.Equal(replacementPath, (await db.EntityFiles
            .SingleAsync(file => file.EntityId == playableId && file.Role == EntityFileRole.Source)).Path);
        Assert.Equal(previousPath, (await db.EntityFiles
            .SingleAsync(file => file.EntityId == imageId && file.Role == EntityFileRole.Source)).Path);
    }

    [Fact]
    public async Task VideoRecoveryIncludesNullRootOwnersCoveredByPathOrScanSnapshot() {
        await using var db = CreateContext();
        var otherRootId = Guid.NewGuid();
        var assignedId = Guid.NewGuid();
        var pathCoveredId = Guid.NewGuid();
        var snapshotCoveredId = Guid.NewGuid();
        var unrelatedId = Guid.NewGuid();
        var assignedElsewhereId = Guid.NewGuid();
        const string snapshotPath = "/mounted/video-alias/movie.mkv";
        SeedLibraryRoot(db, RootId, "/media/videos");
        SeedLibraryRoot(db, otherRootId, "/media/other");
        SeedVideo(db, assignedId, "/media/videos/assigned.mkv");
        SeedVideo(db, pathCoveredId, "/media/videos/legacy/path-covered.mkv");
        SeedVideo(db, snapshotCoveredId, snapshotPath);
        SeedVideo(db, unrelatedId, "/media/unrelated/movie.mkv");
        SeedVideo(db, assignedElsewhereId, "/media/videos/owned-by-other-root.mkv");
        db.EntityLibraryRoots.AddRange(
            new EntityLibraryRootRow { EntityId = assignedId, LibraryRootId = RootId  },
            new EntityLibraryRootRow { EntityId = assignedElsewhereId, LibraryRootId = otherRootId  });
        db.ScannedFiles.Add(new ScannedFileRow {
            LibraryRootId = RootId,
            ScanKind = JobType.ScanLibrary.ToCode(),
            Path = snapshotPath,
            SizeBytes = 1,
            ModifiedTicks = 2,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var targets = await service.GetPlayableVideoTargetsInRootAsync(RootId, CancellationToken.None);

        Assert.Equal(
            new[] { assignedId, pathCoveredId, snapshotCoveredId }.Order().ToArray(),
            targets.Select(target => target.Id).Order().ToArray());
    }

    [Fact]
    public async Task RootWideVideoRecoveryLoadsNeedsForEveryTargetInOnePersistenceCall() {
        await using var db = CreateContext();
        SeedLibraryRoot(db, RootId, "/media/videos");
        var ids = Enumerable.Range(0, 75).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in ids) {
            SeedVideo(db, id, $"/media/videos/{id:N}.mkv");
            db.EntityLibraryRoots.Add(new EntityLibraryRootRow { EntityId = id, LibraryRootId = RootId  });
        }
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var targets = await service.GetPlayableVideoRecoveryTargetsInRootAsync(RootId, CancellationToken.None);

        Assert.Equal(ids.Order().ToArray(), targets.Select(target => target.Id).Order().ToArray());
        Assert.All(targets, target => Assert.True(target.Needs.NeedsSubtitleExtraction));
    }

    [Fact]
    public async Task RefreshTreeProjectsSourcePathsForVideoDescendants() {
        await using var db = CreateContext();
        var seriesId = Guid.NewGuid();
        var videoId = Guid.NewGuid();
        const string sourcePath = "/media/videos/Show/episode.mkv";
        db.Entities.Add(new EntityRow {
            Id = seriesId,
            KindCode = EntityKind.VideoSeries.ToCode(),
            Title = "Show",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        SeedSourceEntity(
            db,
            videoId,
            EntityKind.Video.ToCode(),
            sourcePath,
            parentEntityId: seriesId);
        await db.SaveChangesAsync();

        var tree = await new LibraryScanPersistenceService(db)
            .GetEntityTreeAsync(seriesId, CancellationToken.None);

        Assert.Null(Assert.Single(tree, target => target.Id == seriesId).SourcePath);
        Assert.Equal(sourcePath, Assert.Single(tree, target => target.Id == videoId).SourcePath);
    }

    [Fact]
    public async Task RemoveEntitiesInExcludedPathsRemovesExistingSourcesUnderExcludedDirectories() {
        await using var db = CreateContext();
        var keepId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var skipId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        SeedLibraryRoot(db, RootId, "/media/videos");
        SeedSourceEntity(db, keepId, EntityKind.Video.ToCode(), "/media/videos/Keep/movie.mkv");
        SeedSourceEntity(db, skipId, EntityKind.Video.ToCode(), "/media/videos/Skip/movie.mkv");
        db.EntityLibraryRoots.Add(new EntityLibraryRootRow { EntityId = keepId, LibraryRootId = RootId  });
        db.EntityLibraryRoots.Add(new EntityLibraryRootRow { EntityId = skipId, LibraryRootId = RootId  });
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
            KindCode = EntityKind.VideoSeries.ToCode(),
            Title = "The Chair Company",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.EntitySources.Add(new EntitySourceRow {
            EntityId = seriesId,
            Code = EntitySourceCode.Folder.ToCode(),
            Value = "/media/The Chair Company",
            UpdatedAt = now
        });
        db.UserEntityStates.Add(new UserEntityStateRow {
            UserId = TestUserContext.UserId, EntityId = seriesId, RatingValue = 4, UpdatedAt = now });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var ids = await service.UpsertVideosBatchAsync([
            new VideoUpsertItem(
                "/media/The Chair Company/Season 1/The Chair Company - S01E01.mkv",
                "Life goes by too fast",
                rootId,
                IsNsfw: false,
                ScanPlacement: PlayableVideoScanPlacement.Episode,
                new VideoSeriesScanInfo("/media/The Chair Company", "The Chair Company"),
                new VideoSeasonScanInfo("/media/The Chair Company/Season 1", "Season 1", 1),
                EpisodeNumber: 1,
                AbsoluteEpisodeNumber: null)
        ], CancellationToken.None);

        var videoId = Assert.Single(ids);
        Assert.Equal(seriesId, Assert.Single(db.Entities.Where(entity => entity.KindCode == EntityKind.VideoSeries.ToCode())).Id);
        Assert.Equal(4, db.UserEntityStates.Single(state => state.EntityId == seriesId).RatingValue);

        var season = Assert.Single(db.Entities.Where(entity => entity.KindCode == EntityKind.VideoSeason.ToCode()));
        Assert.Equal(1, season.SortOrder);

        Assert.Equal(seriesId, season.ParentEntityId);
        Assert.Equal(1, season.SortOrder);
        var video = Assert.Single(db.Entities.Where(entity => entity.Id == videoId));
        Assert.Equal(EntityKind.VideoEpisode.ToCode(), video.KindCode);
        Assert.Equal(season.Id, video.ParentEntityId);
        Assert.Equal(1, video.SortOrder);
        Assert.Contains(db.EntitySources, source =>
            source.EntityId == seriesId && source.Code == EntitySourceCode.Folder.ToCode());
        Assert.Contains(db.EntitySources, source =>
            source.EntityId == season.Id && source.Code == EntitySourceCode.Folder.ToCode());
        Assert.DoesNotContain(db.EntityFiles, file =>
            file.Role == EntityFileRole.Source && (file.EntityId == seriesId || file.EntityId == season.Id));
        Assert.Contains(db.EntityPositions, position =>
            position.EntityId == season.Id &&
            position.Code == EntityPositionCodes.Season &&
            position.Value == 1);
        Assert.Contains(db.EntityPositions, position =>
            position.EntityId == videoId &&
            position.Code == EntityPositionCodes.Episode &&
            position.Value == 1);
    }

    [Fact]
    public async Task UpsertVideosBatchToleratesOneFileSharedByTwoEpisodes() {
        // A multi-episode file (S01E05-E06) is bound as the source of BOTH episodes it covers, so one
        // path legitimately maps to two video entities. A rescan must not crash on the duplicate path
        // and must keep each episode's provider-assigned position instead of re-deriving both from the
        // single filename parse.
        await using var db = CreateContext();
        var seriesId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var seasonId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var rootId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var firstEpisodeId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var secondEpisodeId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        const string sharedPath = "/media/The Chair Company/Season 1/The Chair Company - S01E05-E06.mkv";
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow { Id = seriesId, KindCode = EntityKind.VideoSeries.ToCode(), Title = "The Chair Company", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = seasonId, KindCode = EntityKind.VideoSeason.ToCode(), Title = "Season 1", ParentEntityId = seriesId, SortOrder = 1, CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = firstEpisodeId, KindCode = EntityKind.VideoEpisode.ToCode(), Title = "Episode 5", ParentEntityId = seasonId, SortOrder = 5, CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = secondEpisodeId, KindCode = EntityKind.VideoEpisode.ToCode(), Title = "Episode 6", ParentEntityId = seasonId, SortOrder = 6, CreatedAt = now.AddSeconds(1), UpdatedAt = now });
        db.EntityFiles.AddRange(
            new EntityFileRow { Id = Guid.NewGuid(), EntityId = firstEpisodeId, Role = EntityFileRole.Source, Path = sharedPath, CreatedAt = now, UpdatedAt = now },
            new EntityFileRow { Id = Guid.NewGuid(), EntityId = secondEpisodeId, Role = EntityFileRole.Source, Path = sharedPath, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var ids = await service.UpsertVideosBatchAsync([
            new VideoUpsertItem(
                sharedPath,
                "The Chair Company - S01E05-E06",
                rootId,
                IsNsfw: false,
                ScanPlacement: PlayableVideoScanPlacement.Episode,
                new VideoSeriesScanInfo("/media/The Chair Company", "The Chair Company"),
                new VideoSeasonScanInfo("/media/The Chair Company/Season 1", "Season 1", 1),
                EpisodeNumber: 5,
                AbsoluteEpisodeNumber: null),
            new VideoUpsertItem(
                sharedPath,
                "The Chair Company - S01E05-E06",
                rootId,
                IsNsfw: false,
                ScanPlacement: PlayableVideoScanPlacement.Episode,
                new VideoSeriesScanInfo("/media/The Chair Company", "The Chair Company"),
                new VideoSeasonScanInfo("/media/The Chair Company/Season 1", "Season 1", 1),
                EpisodeNumber: 6,
                AbsoluteEpisodeNumber: null)
        ], CancellationToken.None);

        Assert.Equal([firstEpisodeId, secondEpisodeId], ids);
        // No third video was minted for the already-owned path.
        Assert.Equal(2, db.Entities.Count(entity => entity.KindCode == EntityKind.VideoEpisode.ToCode()));
        // Both episodes keep their own positions — the second was not clobbered by the filename parse.
        Assert.Equal(5, (await db.Entities.FindAsync([firstEpisodeId]))!.SortOrder);
        Assert.Equal(6, (await db.Entities.FindAsync([secondEpisodeId]))!.SortOrder);
        // Both owners retain their direct source association without legacy VideoDetail rows.
        Assert.NotNull(await db.EntityLibraryRoots.FindAsync([firstEpisodeId]));
        Assert.NotNull(await db.EntityLibraryRoots.FindAsync([secondEpisodeId]));
    }

    [Fact]
    public async Task UpsertVideosBatchReusesCaseVariantSourceOwnerOnWindows() {
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        await using var db = CreateContext();
        var videoId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        const string persistedPath = "C:\\Media\\Movies\\Arrival.mkv";
        const string discoveredPath = "C:\\MEDIA\\MOVIES\\ARRIVAL.MKV";
        SeedVideo(db, videoId, persistedPath);
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var ids = await service.UpsertVideosBatchAsync([
            new VideoUpsertItem(
                discoveredPath,
                "Arrival",
                rootId,
                IsNsfw: false,
                ScanPlacement: PlayableVideoScanPlacement.Standalone)
        ], CancellationToken.None);

        Assert.Equal(videoId, Assert.Single(ids));
        Assert.Single(db.Entities.Where(entity => entity.KindCode == EntityKind.Video.ToCode()));
        Assert.Single(db.EntityFiles.Where(file => file.Role == EntityFileRole.Source));
    }

    [Fact]
    public async Task UpsertVideosBatchMarksOrganizedSeriesChainUnorganizedWhenNewEpisodeIsDiscovered() {
        await using var db = CreateContext();
        var seriesId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var seasonId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var rootId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow {
                Id = seriesId,
                KindCode = EntityKind.VideoSeries.ToCode(),
                Title = "The Chair Company",
                IsOrganized = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = seasonId,
                KindCode = EntityKind.VideoSeason.ToCode(),
                Title = "Season 1",
                ParentEntityId = seriesId,
                SortOrder = 1,
                IsOrganized = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.EntitySources.Add(new EntitySourceRow {
            EntityId = seriesId,
            Code = EntitySourceCode.Folder.ToCode(),
            Value = "/media/The Chair Company",
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var ids = await service.UpsertVideosBatchAsync([
            new VideoUpsertItem(
                "/media/The Chair Company/Season 1/The Chair Company - S01E02.mkv",
                "The man upstairs",
                rootId,
                IsNsfw: false,
                ScanPlacement: PlayableVideoScanPlacement.Episode,
                new VideoSeriesScanInfo("/media/The Chair Company", "The Chair Company"),
                new VideoSeasonScanInfo("/media/The Chair Company/Season 1", "Season 1", 1),
                EpisodeNumber: 2,
                AbsoluteEpisodeNumber: null)
        ], CancellationToken.None);

        var episodeId = Assert.Single(ids);
        Assert.False((await db.Entities.FindAsync([seriesId]))!.IsOrganized);
        Assert.False((await db.Entities.FindAsync([seasonId]))!.IsOrganized);
        var root = Assert.Single(await service.ResolveAutoIdentifyRootsAsync([episodeId], CancellationToken.None));
        Assert.Equal(seriesId, root.Id);
        Assert.False(root.IsOrganized);
    }

    [Fact]
    public async Task UpsertVideosBatchMaterializesMovieAsTheDirectPlayableSourceOwner() {
        await using var db = CreateContext();
        var rootId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var service = new LibraryScanPersistenceService(db);

        var ids = await service.UpsertVideosBatchAsync([
            new VideoUpsertItem(
                "/media/Friendship/Friendship.mp4",
                "Friendship",
                rootId,
                IsNsfw: false,
                ScanPlacement: PlayableVideoScanPlacement.Movie,
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

        var movieId = Assert.Single(ids);
        var movie = Assert.Single(db.Entities.Where(entity => entity.KindCode == EntityKind.Movie.ToCode()));
        Assert.Equal(movieId, movie.Id);
        Assert.Equal("Friendship", movie.Title);
        Assert.False(movie.IsNsfw);
        Assert.Contains(db.EntityFiles, file =>
            file.EntityId == movie.Id &&
            file.Role == EntityFileRole.Source &&
            file.Path == "/media/Friendship/Friendship.mp4");
        Assert.DoesNotContain(db.Entities, entity =>
            entity.KindCode == EntityKind.Video.ToCode() && entity.ParentEntityId == movie.Id);
        Assert.Contains(db.EntitySources, source =>
            source.EntityId == movie.Id &&
            source.Code == EntitySourceCode.Folder.ToCode() &&
            source.Value == "/media/Friendship");
        Assert.Equal("Movie description", (await db.EntityDescriptions.FindAsync([movie.Id]))?.Value);
        Assert.False(db.UserEntityStates.Any(state => state.EntityId == movie.Id));
        Assert.Contains(db.EntityDates, date =>
            date.EntityId == movie.Id &&
            date.Code == EntityDateType.Release.ToCode() &&
            date.Value == "2025-05-09");
        Assert.Contains(db.EntityRelationshipLinks, relationship =>
            relationship.EntityId == movie.Id &&
            relationship.RelationshipCode == RelationshipKind.Studio.ToCode());
        Assert.Contains(db.EntityRelationshipLinks, relationship =>
            relationship.EntityId == movie.Id &&
            relationship.RelationshipCode == RelationshipKind.Tags.ToCode());
        Assert.Equal(2, db.EntityRelationshipLinks.Count(relationship =>
            relationship.EntityId == movie.Id &&
            relationship.RelationshipCode == RelationshipKind.Cast.ToCode() &&
            relationship.MetadataJson!.Contains(CreditRole.Actor.ToCode())));
    }

    [Fact]
    public async Task MovieFolderProvenanceReusesTheWantedMovieButOnlyThePayloadSourceFulfillsIt() {
        await using var db = CreateContext();
        var rootId = Guid.NewGuid();
        var movieId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = movieId,
            KindCode = EntityKind.Movie.ToCode(),
            Title = "Wanted film",
            IsWanted = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.EntitySources.Add(new EntitySourceRow {
            EntityId = movieId,
            Code = EntitySourceCode.Folder.ToCode(),
            Value = "/media/Wanted film",
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var ids = await new LibraryScanPersistenceService(db).UpsertVideosBatchAsync([
            new VideoUpsertItem(
                "/media/Wanted film/Wanted film.mkv",
                "Wanted film",
                rootId,
                IsNsfw: false,
                ScanPlacement: PlayableVideoScanPlacement.Movie,
                Movie: new MovieScanInfo("/media/Wanted film", "Wanted film"))
        ], CancellationToken.None);

        Assert.Equal(movieId, Assert.Single(ids));
        Assert.False((await db.Entities.FindAsync([movieId]))!.IsWanted);
        Assert.Single(db.EntityFiles.Where(file => file.EntityId == movieId && file.Role == EntityFileRole.Source));
    }

    [Fact]
    public async Task UpsertVideosBatchMaterializesStandaloneVideoWithoutAParent() {
        await using var db = CreateContext();
        var rootId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var service = new LibraryScanPersistenceService(db);
        var ids = await service.UpsertVideosBatchAsync([
            new VideoUpsertItem(
                "/media/Friendship/Friendship.mp4",
                "Friendship",
                rootId,
                IsNsfw: false,
                ScanPlacement: PlayableVideoScanPlacement.Standalone)
        ], CancellationToken.None);

        var video = await db.Entities.FindAsync([Assert.Single(ids)]);
        Assert.Equal(EntityKind.Video.ToCode(), video?.KindCode);
        Assert.Null(video?.ParentEntityId);
        Assert.Null(video?.SortOrder);
    }

    [Fact]
    public async Task RemoveStaleMoviesByRootUsesFolderProvenanceWithoutProxyChildren() {
        await using var db = CreateContext();
        var rootId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var staleMovieId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var keepMovieId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        SeedLibraryRoot(db, rootId, "/media/videos");
        SeedSourceEntity(db, staleMovieId, EntityKind.Movie.ToCode(), "/media/videos/Stale/Stale.mp4");
        SeedSourceEntity(db, keepMovieId, EntityKind.Movie.ToCode(), "/media/videos/Keep/Keep.mp4");
        db.EntitySources.AddRange(
            new EntitySourceRow { EntityId = staleMovieId, Code = EntitySourceCode.Folder.ToCode(), Value = "/media/videos/Stale", UpdatedAt = DateTimeOffset.UtcNow },
            new EntitySourceRow { EntityId = keepMovieId, Code = EntitySourceCode.Folder.ToCode(), Value = "/media/videos/Keep", UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var removed = await service.RemoveStaleMoviesByRootAsync(
            rootId,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "/media/videos/Keep" },
            CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.False(await db.Entities.AnyAsync(entity => entity.Id == staleMovieId));
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == keepMovieId));
    }

    [Fact]
    public async Task RemoveOrphanSeriesAndSeasonsPreservesWantedPlaceholders() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var wantedMovieId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var wantedSeasonId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var wantedSeriesId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var orphanMovieId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var orphanSeasonId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var orphanSeriesId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        db.Entities.AddRange(
            new EntityRow { Id = wantedMovieId, KindCode = EntityKind.Movie.ToCode(), Title = "Wanted movie", IsWanted = true, CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = wantedSeasonId, KindCode = EntityKind.VideoSeason.ToCode(), Title = "Wanted season", IsWanted = true, CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = wantedSeriesId, KindCode = EntityKind.VideoSeries.ToCode(), Title = "Wanted series", IsWanted = true, CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = orphanMovieId, KindCode = EntityKind.Movie.ToCode(), Title = "Orphan movie", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = orphanSeasonId, KindCode = EntityKind.VideoSeason.ToCode(), Title = "Orphan season", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = orphanSeriesId, KindCode = EntityKind.VideoSeries.ToCode(), Title = "Orphan series", CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();

        var removed = await new LibraryScanPersistenceService(db)
            .RemoveOrphanSeriesAndSeasonsAsync(CancellationToken.None);

        Assert.Equal(2, removed);
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == wantedMovieId));
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == wantedSeasonId));
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == wantedSeriesId));
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == orphanMovieId));
        Assert.False(await db.Entities.AnyAsync(entity => entity.Id == orphanSeasonId));
        Assert.False(await db.Entities.AnyAsync(entity => entity.Id == orphanSeriesId));
    }

    [Fact]
    public async Task RemoveOrphanSeriesAndSeasonsPreservesActivelyMonitoredMovie() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var monitoredMovieId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var orphanMovieId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        db.Entities.AddRange(
            new EntityRow { Id = monitoredMovieId, KindCode = EntityKind.Movie.ToCode(), Title = "Monitored movie", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = orphanMovieId, KindCode = EntityKind.Movie.ToCode(), Title = "Orphan movie", CreatedAt = now, UpdatedAt = now });
        db.Monitors.Add(new MonitorRow {
            Id = Guid.Parse("99999999-9999-9999-9999-999999999999"),
            EntityId = monitoredMovieId,
            Kind = EntityKind.Movie,
            Status = MonitorStatus.Active,
            Title = "Monitored movie",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var removed = await new LibraryScanPersistenceService(db)
            .RemoveOrphanSeriesAndSeasonsAsync(CancellationToken.None);

        Assert.Equal(0, removed);
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == monitoredMovieId));
        Assert.True(await db.Entities.AnyAsync(entity => entity.Id == orphanMovieId));
    }

    [Fact]
    public async Task RemoveOrphanTagsRemovesOnlyUnreferencedTags() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var referencedTag = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var orphanTag = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var videoId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        db.Entities.AddRange(
            new EntityRow { Id = referencedTag, KindCode = EntityKind.Tag.ToCode(), Title = "Used", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = orphanTag, KindCode = EntityKind.Tag.ToCode(), Title = "Unused", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = videoId, KindCode = EntityKind.Video.ToCode(), Title = "Film", CreatedAt = now, UpdatedAt = now });
        db.EntityRelationshipLinks.Add(new EntityRelationshipLinkRow {
            EntityId = videoId,
            RelationshipCode = "tags",
            Label = "Tags",
            TargetEntityId = referencedTag,
            TargetKindCode = EntityKind.Tag.ToCode(),
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
        db.EntityLibraryRoots.Add(new EntityLibraryRootRow { EntityId = videoId });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var removed = await service.RemoveStalePlayableVideosByRootAsync(rootId, new HashSet<string>(), CancellationToken.None);

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
        var detail = await db.EntityLibraryRoots.SingleAsync(row => row.EntityId == childId);
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
        var detail = await db.EntityLibraryRoots.SingleAsync(row => row.EntityId == childId);
        Assert.Equal(parentId, child.ParentEntityId);
        Assert.Equal(3, child.SortOrder);
        Assert.Equal(rootId, detail.LibraryRootId);
    }

    [Fact]
    public async Task StructuralFolderUpsertsUseFolderSourcesWhilePayloadUpsertsUseSourceFiles() {
        await using var db = CreateContext();
        var rootId = Guid.NewGuid();
        SeedLibraryRoot(db, rootId, "/media");
        await db.SaveChangesAsync();
        var service = new LibraryScanPersistenceService(db);
        var galleryId = await service.UpsertGalleryAsync(
            "/media/images/Gallery", "Gallery", rootId, null, 0, false, CancellationToken.None);
        var imageId = await service.UpsertImageAsync(
            "/media/images/Gallery/page.jpg", "page", rootId, galleryId, 1, 0, false, CancellationToken.None);
        var artistId = await service.UpsertMusicArtistAsync(
            "/media/audio/Artist", "Artist", rootId, 0, false, CancellationToken.None);
        var libraryId = await service.UpsertAudioLibraryAsync(
            "/media/audio/Artist/Album", "Album", rootId, artistId, 0, false, CancellationToken.None);
        var trackId = await service.UpsertAudioTrackAsync(
            "/media/audio/Artist/Album/track.flac", "track", rootId, libraryId, 0, null, 0, false, CancellationToken.None);
        var authorId = await service.UpsertBookAuthorAsync(
            "/media/books/Author", "Author", null, false, CancellationToken.None);
        var bookId = await service.UpsertBookSeriesAsync(
            "/media/books/Series", "Series", rootId, false, BookType.Novel, BookFormat.Audio, CancellationToken.None);
        var volumeId = await service.UpsertBookVolumeAsync(
            "/media/books/Series/Volume 1", "Volume 1", bookId, 0, false, CancellationToken.None);

        var structuralIds = new HashSet<Guid> { galleryId, artistId, libraryId, authorId, bookId, volumeId };
        var folderSources = await db.EntitySources.AsNoTracking()
            .Where(source => structuralIds.Contains(source.EntityId))
            .ToArrayAsync();
        var payloadFiles = await db.EntityFiles.AsNoTracking()
            .Where(file => new[] { imageId, trackId }.Contains(file.EntityId))
            .ToArrayAsync();

        Assert.Equal(structuralIds.Count, folderSources.Length);
        Assert.All(folderSources, source => Assert.Equal(EntitySourceCode.Folder.ToCode(), source.Code));
        Assert.DoesNotContain(db.EntityFiles, file => structuralIds.Contains(file.EntityId) && file.Role == EntityFileRole.Source);
        Assert.Equal(2, payloadFiles.Length);
        Assert.All(payloadFiles, file => Assert.Equal(EntityFileRole.Source, file.Role));
    }

    [Fact]
    public async Task DirectSourceUpsertsUseMostSpecificDisabledRootForNewImageAndAudioTrack() {
        await using var db = CreateContext();
        var outerRootId = Guid.Parse("10101010-1010-1010-1010-101010101010");
        var nestedRootId = Guid.Parse("20202020-2020-2020-2020-202020202020");
        SeedLibraryRoot(db, outerRootId, "/media");
        SeedLibraryRoot(db, nestedRootId, "/media/private");
        var nestedRoot = db.LibraryRoots.Local.Single(row => row.Id == nestedRootId);
        nestedRoot.Enabled = false;
        nestedRoot.ScanImages = false;
        nestedRoot.ScanAudio = false;
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var imageId = await service.UpsertImageAsync(
            "/media/private/image.jpg", "image", outerRootId, null, 12, 0, false, CancellationToken.None);
        var trackId = await service.UpsertAudioTrackAsync(
            "/media/private/track.flac", "track", outerRootId, null, 0, null, 0, false, CancellationToken.None);

        var ownership = await db.EntityLibraryRoots.AsNoTracking()
            .Where(row => row.EntityId == imageId || row.EntityId == trackId)
            .ToDictionaryAsync(row => row.EntityId, row => row.LibraryRootId);
        Assert.Equal(nestedRootId, ownership[imageId]);
        Assert.Equal(nestedRootId, ownership[trackId]);
    }

    [Fact]
    public async Task DirectSourceUpsertsRepairExistingImageAndAudioTrackOwnership() {
        await using var db = CreateContext();
        var rootId = Guid.Parse("30303030-3030-3030-3030-303030303030");
        var imageId = Guid.Parse("40404040-4040-4040-4040-404040404040");
        var trackId = Guid.Parse("50505050-5050-5050-5050-505050505050");
        SeedLibraryRoot(db, rootId, "/media");
        SeedSourceEntity(db, imageId, EntityKind.Image.ToCode(), "/media/image.jpg");
        SeedSourceEntity(db, trackId, EntityKind.AudioTrack.ToCode(), "/media/track.flac");
        db.EntityLibraryRoots.AddRange(
            new EntityLibraryRootRow { EntityId = imageId },
            new EntityLibraryRootRow { EntityId = trackId });
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var rescannedImageId = await service.UpsertImageAsync(
            "/media/image.jpg", "image", rootId, null, 12, 0, false, CancellationToken.None);
        var rescannedTrackId = await service.UpsertAudioTrackAsync(
            "/media/track.flac", "track", rootId, null, 0, null, 0, false, CancellationToken.None);

        Assert.Equal(imageId, rescannedImageId);
        Assert.Equal(trackId, rescannedTrackId);
        Assert.All(
            await db.EntityLibraryRoots.AsNoTracking()
                .Where(row => row.EntityId == imageId || row.EntityId == trackId)
                .ToArrayAsync(),
            row => Assert.Equal(rootId, row.LibraryRootId));
    }

    [Fact]
    public async Task UpsertImageCanRelinkExistingImageBackToLooseRootFile() {
        await using var db = CreateContext();
        var rootId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        SeedLibraryRoot(db, rootId, "/media/images");
        await db.SaveChangesAsync();
        var service = new LibraryScanPersistenceService(db);
        var galleryId = await service.UpsertGalleryAsync(
            "/media/images/Gallery",
            "Gallery",
            rootId,
            parentGalleryEntityId: null,
            sortOrder: 0,
            isNsfw: false,
            CancellationToken.None);
        var imageId = await service.UpsertImageAsync(
            "/media/images/cover.jpg",
            "cover",
            rootId,
            galleryId,
            sizeBytes: 12,
            sortOrder: 4,
            isNsfw: false,
            CancellationToken.None);

        var relinkedId = await service.UpsertImageAsync(
            "/media/images/cover.jpg",
            "cover",
            libraryRootId: rootId,
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
        var rootId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        SeedLibraryRoot(db, rootId, "/media/audio");
        await db.SaveChangesAsync();
        var service = new LibraryScanPersistenceService(db);
        var libraryId = await service.UpsertAudioLibraryAsync(
            "/media/audio/Album",
            "Album",
            rootId,
            parentEntityId: null,
            sortOrder: 0,
            isNsfw: false,
            CancellationToken.None);
        var trackId = await service.UpsertAudioTrackAsync(
            "/media/audio/song.flac",
            "song",
            rootId,
            libraryId,
            sortOrder: 2,
            sectionLabel: null,
            sectionOrder: 0,
            isNsfw: false,
            CancellationToken.None);

        var relinkedId = await service.UpsertAudioTrackAsync(
            "/media/audio/song.flac",
            "song",
            libraryRootId: rootId,
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
    public async Task UpsertAudioLibraryPreservesOrganizedTitleOnRescan() {
        await using var db = CreateContext();
        var service = new LibraryScanPersistenceService(db);
        var libraryId = await service.UpsertAudioLibraryAsync(
            "/media/audio/NateWantsToBattle/What You Want (2020)",
            "What You Want (2020)",
            Guid.NewGuid(),
            parentEntityId: null,
            sortOrder: 0,
            isNsfw: false,
            CancellationToken.None);
        var album = await db.Entities.SingleAsync(entity => entity.Id == libraryId);
        album.Title = "What You Want";
        album.IsOrganized = true;
        await db.SaveChangesAsync();

        var rescannedId = await service.UpsertAudioLibraryAsync(
            "/media/audio/NateWantsToBattle/What You Want (2020)",
            "What You Want (2020)",
            Guid.NewGuid(),
            parentEntityId: null,
            sortOrder: 0,
            isNsfw: true,
            CancellationToken.None);

        var rescannedAlbum = await db.Entities.SingleAsync(entity => entity.Id == libraryId);
        Assert.Equal(libraryId, rescannedId);
        Assert.Equal("What You Want", rescannedAlbum.Title);
        Assert.True(rescannedAlbum.IsOrganized);
        Assert.True(rescannedAlbum.IsNsfw);
    }

    [Fact]
    public async Task UpsertAudioTrackPreservesOrganizedTitleOnRescan() {
        await using var db = CreateContext();
        var rootId = Guid.NewGuid();
        SeedLibraryRoot(db, rootId, "/media/audio");
        await db.SaveChangesAsync();
        var service = new LibraryScanPersistenceService(db);
        var trackId = await service.UpsertAudioTrackAsync(
            "/media/audio/song.flac",
            "song",
            libraryRootId: rootId,
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
            libraryRootId: rootId,
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
    public async Task UpsertAudioTrackLeavesOrganizedAlbumAloneWhenExistingTrackIsRescanned() {
        await using var db = CreateContext();
        var rootId = Guid.NewGuid();
        SeedLibraryRoot(db, rootId, "/media/audio");
        await db.SaveChangesAsync();
        var service = new LibraryScanPersistenceService(db);
        var albumId = await service.UpsertAudioLibraryAsync(
            "/media/audio/Album",
            "Album",
            rootId,
            parentEntityId: null,
            sortOrder: 0,
            isNsfw: false,
            CancellationToken.None);
        var trackId = await service.UpsertAudioTrackAsync(
            "/media/audio/Album/song.flac",
            "song",
            rootId,
            albumId,
            sortOrder: 0,
            sectionLabel: null,
            sectionOrder: 0,
            isNsfw: false,
            CancellationToken.None);
        var album = await db.Entities.FindAsync([albumId]);
        album!.IsOrganized = true;
        await db.SaveChangesAsync();

        var rescannedId = await service.UpsertAudioTrackAsync(
            "/media/audio/Album/song.flac",
            "song",
            rootId,
            albumId,
            sortOrder: 0,
            sectionLabel: null,
            sectionOrder: 0,
            isNsfw: false,
            CancellationToken.None);

        Assert.Equal(trackId, rescannedId);
        Assert.True((await db.Entities.FindAsync([albumId]))!.IsOrganized);
    }

    [Fact]
    public async Task UpsertAudioTrackMarksOrganizedAlbumUnorganizedWhenNewTrackIsDiscovered() {
        await using var db = CreateContext();
        var rootId = Guid.NewGuid();
        SeedLibraryRoot(db, rootId, "/media/audio");
        await db.SaveChangesAsync();
        var service = new LibraryScanPersistenceService(db);
        var artistId = await service.UpsertMusicArtistAsync(
            "/media/audio/Artist",
            "Artist",
            rootId,
            sortOrder: 0,
            isNsfw: false,
            CancellationToken.None);
        var albumId = await service.UpsertAudioLibraryAsync(
            "/media/audio/Artist/Album",
            "Album",
            rootId,
            artistId,
            sortOrder: 0,
            isNsfw: false,
            CancellationToken.None);
        var artist = await db.Entities.FindAsync([artistId]);
        var album = await db.Entities.FindAsync([albumId]);
        artist!.IsOrganized = true;
        album!.IsOrganized = true;
        await db.SaveChangesAsync();

        var trackId = await service.UpsertAudioTrackAsync(
            "/media/audio/Artist/Album/new-song.flac",
            "new-song",
            rootId,
            albumId,
            sortOrder: 1,
            sectionLabel: null,
            sectionOrder: 0,
            isNsfw: false,
            CancellationToken.None);

        Assert.False((await db.Entities.FindAsync([albumId]))!.IsOrganized);
        Assert.True((await db.Entities.FindAsync([artistId]))!.IsOrganized);
        var root = Assert.Single(await service.ResolveAutoIdentifyRootsAsync([trackId], CancellationToken.None));
        Assert.Equal(albumId, root.Id);
        Assert.False(root.IsOrganized);
    }

    [Fact]
    public async Task ProcessingRootStillCollapsesTracksIntoTheirAlbumAfterIdentifyAttemptsAreExhausted() {
        await using var db = CreateContext();
        var rootId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow {
                Id = albumId,
                KindCode = EntityKind.AudioLibrary.ToCode(),
                Title = "Frozen",
                AutoIdentifyAttempts = AutoIdentifyPolicy.MaxAttemptsPerEntity,
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = trackId,
                KindCode = EntityKind.AudioTrack.ToCode(),
                Title = "Vuelie",
                ParentEntityId = albumId,
                CreatedAt = now,
                UpdatedAt = now
            });
        SeedLibraryRoot(db, rootId, "/media/audio");
        await db.SaveChangesAsync();
        var service = new LibraryScanPersistenceService(db);

        Assert.Empty(await service.ResolveAutoIdentifyRootsAsync([trackId], CancellationToken.None));
        var processingRoot = Assert.Single(await service.ResolveEntityProcessingRootsAsync(
            [trackId],
            CancellationToken.None));

        Assert.Equal(albumId, processingRoot.Id);
        Assert.Equal(EntityKind.AudioLibrary.ToCode(), processingRoot.KindCode);
    }

    [Fact]
    public async Task UpsertAudioTrackUpdatesUnorganizedTitleOnRescan() {
        await using var db = CreateContext();
        var rootId = Guid.NewGuid();
        SeedLibraryRoot(db, rootId, "/media/audio");
        await db.SaveChangesAsync();
        var service = new LibraryScanPersistenceService(db);
        var trackId = await service.UpsertAudioTrackAsync(
            "/media/audio/song.flac",
            "song",
            libraryRootId: rootId,
            audioLibraryId: null,
            sortOrder: 0,
            sectionLabel: null,
            sectionOrder: 0,
            isNsfw: false,
            CancellationToken.None);

        await service.UpsertAudioTrackAsync(
            "/media/audio/song.flac",
            "Better Tag Title",
            libraryRootId: rootId,
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
        SeedSourceEntity(db, galleryId, EntityKind.Gallery.ToCode(), "/media/images/Gallery");
        SeedSourceEntity(db, staleLooseId, EntityKind.Image.ToCode(), "/media/images/stale.jpg");
        SeedSourceEntity(db, validLooseId, EntityKind.Image.ToCode(), "/media/images/valid.jpg");
        SeedSourceEntity(db, containedId, EntityKind.Image.ToCode(), "/media/images/Gallery/contained.jpg", galleryId, 0);
        SeedSourceEntity(db, outsideId, EntityKind.Image.ToCode(), "/other/stale.jpg");
        SeedSourceEntity(db, subfolderOrphanId, EntityKind.Image.ToCode(), "/media/images/Sub/orphan.png");
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
        SeedSourceEntity(db, libraryId, EntityKind.AudioLibrary.ToCode(), "/media/audio/Album");
        SeedSourceEntity(db, staleLooseId, EntityKind.AudioTrack.ToCode(), "/media/audio/stale.flac");
        SeedSourceEntity(db, validLooseId, EntityKind.AudioTrack.ToCode(), "/media/audio/valid.flac");
        SeedSourceEntity(db, containedId, EntityKind.AudioTrack.ToCode(), "/media/audio/Album/contained.flac", libraryId, 0);
        SeedSourceEntity(db, outsideId, EntityKind.AudioTrack.ToCode(), "/other/stale.flac");
        SeedSourceEntity(db, subfolderOrphanId, EntityKind.AudioTrack.ToCode(), "/media/audio/Sub/orphan.flac");
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
    public async Task RemoveStaleAudioTracksPreservesSourceAddedAfterDiscoverySnapshot() {
        var directory = Path.Combine(Path.GetTempPath(), $"prismedia-scan-race-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try {
            var sourcePath = Path.Combine(directory, "late-import.flac");
            await File.WriteAllTextAsync(sourcePath, "audio-bytes");
            await using var db = CreateContext();
            var libraryId = Guid.NewGuid();
            var trackId = Guid.NewGuid();
            SeedSourceEntity(db, libraryId, EntityKind.AudioLibrary.ToCode(), directory);
            SeedSourceEntity(db, trackId, EntityKind.AudioTrack.ToCode(), sourcePath, libraryId, 0);
            await db.SaveChangesAsync();

            var removed = await new LibraryScanPersistenceService(db).RemoveStaleAudioTracksInLibraryAsync(
                libraryId,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                CancellationToken.None);

            Assert.Equal(0, removed);
            Assert.True(await db.Entities.AnyAsync(entity => entity.Id == trackId));
        } finally {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task RemoveStaleGalleriesInRootRemovesStaleFolderSubtree() {
        await using var db = CreateContext();
        var rootId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        SeedLibraryRoot(db, rootId, "/media/images");
        await db.SaveChangesAsync();
        var service = new LibraryScanPersistenceService(db);

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
            rootId,
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
            rootId,
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
            rootId,
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
        var rootId = Guid.Parse("a1a1a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1");
        SeedLibraryRoot(db, rootId, "/media/images");
        await db.SaveChangesAsync();
        var service = new LibraryScanPersistenceService(db);

        // Seed the library as it was scanned under the old rule: "Solo" is a one-image gallery nested
        // under the surviving "Set" gallery.
        var setGalleryId = await service.UpsertGalleryAsync(
            "/media/images/Set", "Set", rootId, parentGalleryEntityId: null, sortOrder: 0, isNsfw: false, CancellationToken.None);
        var soloGalleryId = await service.UpsertGalleryAsync(
            "/media/images/Set/Solo", "Solo", rootId, setGalleryId, sortOrder: 0, isNsfw: false, CancellationToken.None);
        var soloImageId = await service.UpsertImageAsync(
            "/media/images/Set/Solo/only.jpg", "only", rootId, soloGalleryId, sizeBytes: 12, sortOrder: 0, isNsfw: false, CancellationToken.None);

        // The new scan reparents the lone image to the survivor first, then drops the collapsed folder
        // from the valid gallery set.
        await service.UpsertImageAsync(
            "/media/images/Set/Solo/only.jpg", "only", rootId, setGalleryId, sizeBytes: 12, sortOrder: 1, isNsfw: false, CancellationToken.None);
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
        var rootId = Guid.Parse("b2b2b2b2-b2b2-b2b2-b2b2-b2b2b2b2b2b2");
        SeedLibraryRoot(db, rootId, "/media/images");
        await db.SaveChangesAsync();
        var service = new LibraryScanPersistenceService(db);

        // Seed a one-image gallery directly under the root with no surviving ancestor.
        var soloGalleryId = await service.UpsertGalleryAsync(
            "/media/images/Solo", "Solo", rootId, parentGalleryEntityId: null, sortOrder: 0, isNsfw: false, CancellationToken.None);
        var soloImageId = await service.UpsertImageAsync(
            "/media/images/Solo/only.jpg", "only", rootId, soloGalleryId, sizeBytes: 12, sortOrder: 0, isNsfw: false, CancellationToken.None);

        // The new scan makes the image loose, then removes the now-empty gallery folder.
        await service.UpsertImageAsync(
            "/media/images/Solo/only.jpg", "only", libraryRootId: rootId, galleryEntityId: null, sizeBytes: 12, sortOrder: 0, isNsfw: false, CancellationToken.None);
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
        var rootId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        SeedLibraryRoot(db, rootId, "/media/audio");
        await db.SaveChangesAsync();
        var service = new LibraryScanPersistenceService(db);

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
            rootId,
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
            rootId,
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
            rootId,
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
        var rootId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        SeedLibraryRoot(db, rootId, "/media/images");
        await db.SaveChangesAsync();
        var service = new LibraryScanPersistenceService(db);
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
            rootId,
            oldRootGalleryId,
            sizeBytes: 12,
            sortOrder: 0,
            isNsfw: false,
            CancellationToken.None);
        var validLooseImageId = await service.UpsertImageAsync(
            "/media/images/valid.jpg",
            "valid",
            libraryRootId: rootId,
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
        var rootId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        SeedLibraryRoot(db, rootId, "/media/audio");
        await db.SaveChangesAsync();
        var service = new LibraryScanPersistenceService(db);
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
            rootId,
            oldRootLibraryId,
            sortOrder: 0,
            sectionLabel: null,
            sectionOrder: 0,
            isNsfw: false,
            CancellationToken.None);
        var validLooseTrackId = await service.UpsertAudioTrackAsync(
            "/media/audio/valid.flac",
            "valid",
            libraryRootId: rootId,
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
                KindCode = EntityKind.Book.ToCode(),
                Title = "Promised Neverland Ch.1",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = volumeId,
                KindCode = EntityKind.BookVolume.ToCode(),
                Title = "Volume 01",
                ParentEntityId = bookId,
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = chapterId,
                KindCode = EntityKind.BookChapter.ToCode(),
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
        var chapter = Assert.Single(db.Entities.Where(entity => entity.KindCode == EntityKind.BookChapter.ToCode()));
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
        Assert.Equal(EntityKind.Book.ToCode(), series!.KindCode);
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
            KindCode = EntityKind.Book.ToCode(),
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
        Assert.Equal(RootId, (await db.EntityLibraryRoots.FindAsync([bookId]))!.LibraryRootId);
    }

    [Fact]
    public async Task RemoveEntitiesOutsideLibraryRootsDeletesStaleSourceMediaAndKeepsConfiguredRootMedia() {
        await using var db = CreateContext();
        var keptId = Guid.Parse("bbbbbbbb-1111-1111-1111-111111111111");
        var staleMovieId = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");
        var staleVideoId = Guid.Parse("bbbbbbbb-3333-3333-3333-333333333333");
        SeedLibraryRoot(db, RootId, "/media/kept");
        SeedSourceEntity(db, keptId, EntityKind.Video.ToCode(), "/media/kept/video.mkv");
        SeedSourceEntity(db, staleMovieId, EntityKind.Movie.ToCode(), "/media/deleted/movie");
        SeedSourceEntity(db, staleVideoId, EntityKind.Video.ToCode(), "/media/deleted/video.mkv");
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var removed = await service.RemoveEntitiesOutsideLibraryRootsAsync(CancellationToken.None);

        Assert.Equal(2, removed);
        Assert.NotNull(await db.Entities.FindAsync([keptId]));
        Assert.Null(await db.Entities.FindAsync([staleMovieId]));
        Assert.Null(await db.Entities.FindAsync([staleVideoId]));
    }

    [Fact]
    public async Task RemoveEntitiesOutsideLibraryRootsKeepsMediaInDisabledLibraryRoots() {
        await using var db = CreateContext();
        var disabledRootId = Guid.Parse("bbbbbbbb-4444-4444-4444-444444444444");
        var disabledVideoId = Guid.Parse("bbbbbbbb-5555-5555-5555-555555555555");
        SeedLibraryRoot(db, disabledRootId, "/media/disabled");
        SeedSourceEntity(db, disabledVideoId, EntityKind.Video.ToCode(), "/media/disabled/video.mkv");
        await db.SaveChangesAsync();

        var service = new LibraryScanPersistenceService(db);
        var removed = await service.RemoveEntitiesOutsideLibraryRootsAsync(CancellationToken.None);

        Assert.Equal(0, removed);
        Assert.NotNull(await db.Entities.FindAsync([disabledVideoId]));
    }

    [Fact]
    public async Task RemoveEntitiesOutsideLibraryRootsUsesFolderProvenanceForStructuralEntities() {
        await using var db = CreateContext();
        var keptGalleryId = Guid.NewGuid();
        var staleGalleryId = Guid.NewGuid();
        SeedLibraryRoot(db, RootId, "/media/kept");
        db.Entities.AddRange(
            new EntityRow { Id = keptGalleryId, KindCode = EntityKind.Gallery.ToCode(), Title = "Kept", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow },
            new EntityRow { Id = staleGalleryId, KindCode = EntityKind.Gallery.ToCode(), Title = "Stale", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        db.EntitySources.AddRange(
            new EntitySourceRow { EntityId = keptGalleryId, Code = EntitySourceCode.Folder.ToCode(), Value = "/media/kept/Gallery", UpdatedAt = DateTimeOffset.UtcNow },
            new EntitySourceRow { EntityId = staleGalleryId, Code = EntitySourceCode.Folder.ToCode(), Value = "/media/deleted/Gallery", UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var removed = await new LibraryScanPersistenceService(db)
            .RemoveEntitiesOutsideLibraryRootsAsync(CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.NotNull(await db.Entities.FindAsync([keptGalleryId]));
        Assert.Null(await db.Entities.FindAsync([staleGalleryId]));
    }

    [Fact]
    public async Task ApplyVideoSidecarMetadataFillsMissingFieldsAndKeepsExistingDescription() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
        SeedVideo(db, videoId, "/media/movie.mkv");
        var video = await db.Entities.FindAsync([videoId]);
        video!.Title = "movie";
        db.UserEntityStates.Add(new UserEntityStateRow {
            UserId = TestUserContext.UserId, EntityId = videoId, RatingValue = 4, UpdatedAt = DateTimeOffset.UtcNow });
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
        Assert.Equal(4, db.UserEntityStates.Single(state => state.EntityId == videoId).RatingValue);
        Assert.Equal("User description", (await db.EntityDescriptions.FindAsync([videoId]))?.Value);
        var release = await db.EntityDates.FindAsync([videoId, EntityDateType.Release.ToCode()]);
        Assert.Equal("2026-05-01", release?.Value);
        Assert.Equal(new DateOnly(2026, 5, 1), release?.SortableValue);
        Assert.Equal(["https://example.test/video"], db.EntityUrls.Where(row => row.EntityId == videoId).Select(row => row.Url).ToArray());
        Assert.Contains(db.EntityRelationshipLinks, row =>
            row.EntityId == videoId && row.RelationshipCode == RelationshipKind.Tags.ToCode());
        Assert.Contains(db.EntityRelationshipLinks, row =>
            row.EntityId == videoId && row.RelationshipCode == RelationshipKind.Studio.ToCode());
        Assert.Contains(db.EntityRelationshipLinks, row =>
            row.EntityId == videoId &&
            row.RelationshipCode == RelationshipKind.Cast.ToCode() &&
            row.MetadataJson!.Contains(CreditRole.Actor.ToCode()));
        Assert.Contains(db.Entities, row => row.KindCode == EntityKind.Tag.ToCode() && row.Title == "Noir" && row.IsNsfw);
    }

    [Fact]
    public async Task ApplyComicInfoMetadataAddsMetadataWithoutOverwritingExistingBookTitle() {
        await using var db = CreateContext();
        var bookId = Guid.Parse("bbbbbbbb-1111-1111-1111-111111111111");
        db.Entities.Add(new EntityRow {
            Id = bookId,
            KindCode = EntityKind.Book.ToCode(),
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
        Assert.Equal(
            "2026-05",
            (await db.EntityDates.FindAsync([bookId, EntityDateType.Release.ToCode()]))?.Value);
        Assert.Equal(["https://example.test/comic"], db.EntityUrls.Where(row => row.EntityId == bookId).Select(row => row.Url).ToArray());
        Assert.Contains(db.EntityRelationshipLinks, row =>
            row.EntityId == bookId && row.RelationshipCode == RelationshipKind.Tags.ToCode());
        Assert.Contains(db.EntityRelationshipLinks, row =>
            row.EntityId == bookId && row.RelationshipCode == RelationshipKind.Studio.ToCode());
        Assert.Contains(db.EntityRelationshipLinks, row =>
            row.EntityId == bookId &&
            row.RelationshipCode == RelationshipKind.Cast.ToCode() &&
            row.MetadataJson!.Contains(CreditRole.Creator.ToCode()));
    }

    [Fact]
    public async Task ComicUpsertsPersistSerializedHierarchyAndSourceOnlyOnInstallment() {
        await using var db = CreateContext();
        var rootPath = Path.Combine(Path.GetTempPath(), $"prismedia-comics-{Guid.NewGuid():N}");
        var seriesPath = Path.Combine(rootPath, "The Series");
        var archivePath = Path.Combine(seriesPath, "Volume 2", "Chapter 12.cbz");
        SeedLibraryRoot(db, RootId, rootPath);
        await db.SaveChangesAsync();
        var service = new LibraryScanPersistenceService(db);

        var seriesId = await service.UpsertComicSeriesAsync(
            seriesPath,
            "The Series",
            RootId,
            isNsfw: false,
            CancellationToken.None);
        var volumeId = await service.UpsertComicVolumeAsync(
            seriesId,
            "Volume 2",
            volumeNumber: 2,
            isNsfw: false,
            CancellationToken.None);
        var installmentId = await service.UpsertComicInstallmentAsync(
            archivePath,
            "Chapter Twelve",
            RootId,
            volumeId,
            sortOrder: 0,
            position: 12,
            positionLabel: "12.5",
            ComicInstallmentKind.Chapter,
            sizeBytes: 4096,
            isNsfw: true,
            sourceProvenance: null,
            CancellationToken.None);

        var series = await db.Entities.FindAsync([seriesId]);
        var volume = await db.Entities.FindAsync([volumeId]);
        var installment = await db.Entities.FindAsync([installmentId]);
        Assert.Equal(EntityKind.ComicSeries.ToCode(), series!.KindCode);
        Assert.Null(series.ParentEntityId);
        Assert.Equal(EntityKind.ComicVolume.ToCode(), volume!.KindCode);
        Assert.Equal(seriesId, volume.ParentEntityId);
        Assert.Equal(2, volume.SortOrder);
        Assert.Equal(EntityKind.ComicInstallment.ToCode(), installment!.KindCode);
        Assert.Equal(volumeId, installment.ParentEntityId);
        Assert.True(installment.IsNsfw);
        Assert.Equal(
            ComicInstallmentKind.Chapter,
            (await db.ComicInstallmentDetails.FindAsync([installmentId]))!.InstallmentKind);
        Assert.Equal(
            (12, "12.5"),
            db.EntityPositions
                .Where(row => row.EntityId == installmentId && row.Code == EntityPositionCodes.Chapter)
                .Select(row => new ValueTuple<int, string?>(row.Value, row.Label))
                .Single());
        var source = Assert.Single(db.EntityFiles.Where(row => row.EntityId == installmentId));
        Assert.Equal(EntityFileRole.Source, source.Role);
        Assert.Equal(archivePath, source.Path);
        Assert.Equal(4096, source.SizeBytes);
        Assert.Empty(db.EntityFiles.Where(row => row.EntityId == seriesId || row.EntityId == volumeId));
        Assert.Equal(
            seriesPath,
            (await db.EntitySources.FindAsync([seriesId, EntitySourceCode.Folder.ToCode()]))!.Value);
        Assert.Contains(db.EntityLibraryRoots, row =>
            row.EntityId == seriesId && row.LibraryRootId == RootId);
        Assert.Contains(db.EntityLibraryRoots, row =>
            row.EntityId == installmentId && row.LibraryRootId == RootId);
    }

    [Fact]
    public async Task ComicUpsertsAdoptLegacyBookHierarchyAndPromoteSavedChapterProgress() {
        await using var db = CreateContext();
        var rootPath = Path.Combine(Path.GetTempPath(), $"prismedia-legacy-comics-{Guid.NewGuid():N}");
        var seriesPath = Path.Combine(rootPath, "The Series");
        var volumePath = Path.Combine(seriesPath, "Volume 2");
        var archivePath = Path.Combine(volumePath, "Chapter 12.cbz");
        SeedLibraryRoot(db, RootId, rootPath);
        await db.SaveChangesAsync();
        var service = new LibraryScanPersistenceService(db);
        var legacySeriesId = await service.UpsertBookAsync(
            seriesPath,
            "The Series",
            RootId,
            isNsfw: false,
            CancellationToken.None);
        var legacyVolumeId = await service.UpsertBookVolumeAsync(
            volumePath,
            "Volume 2",
            legacySeriesId,
            sortOrder: 2,
            isNsfw: false,
            CancellationToken.None);
        var legacyInstallmentId = await service.UpsertBookChapterAsync(
            archivePath,
            "Chapter 12",
            legacyVolumeId,
            sortOrder: 0,
            pageCount: 12,
            isNsfw: false,
            CancellationToken.None);
        var legacyPageId = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = legacyPageId,
            KindCode = EntityKind.BookPage.ToCode(),
            Title = "Page 1",
            ParentEntityId = legacyInstallmentId,
            SortOrder = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.UserEntityStates.Add(new UserEntityStateRow {
            UserId = TestUserContext.UserId,
            EntityId = legacySeriesId,
            IsFavorite = true,
            ProgressCurrentEntityId = legacyInstallmentId,
            ProgressUnit = ProgressUnit.Page.ToCode(),
            ProgressIndex = 4,
            ProgressTotal = 12,
            ProgressMode = ReaderMode.Paged.ToCode(),
            ProgressUpdatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var seriesId = await service.UpsertComicSeriesAsync(
            seriesPath,
            "The Series",
            RootId,
            isNsfw: false,
            CancellationToken.None);
        var volumeId = await service.UpsertComicVolumeAsync(
            seriesId,
            "Volume 2",
            volumeNumber: 2,
            isNsfw: false,
            CancellationToken.None);
        var installmentId = await service.UpsertComicInstallmentAsync(
            archivePath,
            "Chapter 12",
            RootId,
            volumeId,
            sortOrder: 0,
            position: 12,
            positionLabel: "12",
            ComicInstallmentKind.Chapter,
            sizeBytes: null,
            isNsfw: false,
            sourceProvenance: null,
            CancellationToken.None);

        Assert.Equal(legacySeriesId, seriesId);
        Assert.Equal(legacyVolumeId, volumeId);
        Assert.Equal(legacyInstallmentId, installmentId);
        Assert.Equal(EntityKind.ComicSeries.ToCode(), (await db.Entities.FindAsync([seriesId]))!.KindCode);
        Assert.Equal(EntityKind.ComicVolume.ToCode(), (await db.Entities.FindAsync([volumeId]))!.KindCode);
        Assert.Equal(EntityKind.ComicInstallment.ToCode(), (await db.Entities.FindAsync([installmentId]))!.KindCode);
        Assert.Null(await db.BookDetails.FindAsync([seriesId]));
        Assert.Null(await db.BookChapterDetails.FindAsync([installmentId]));
        Assert.Null(await db.Entities.FindAsync([legacyPageId]));
        Assert.Empty(db.EntityFiles.Where(file => file.EntityId == seriesId));
        Assert.Equal(
            seriesPath,
            (await db.EntitySources.FindAsync([seriesId, EntitySourceCode.Folder.ToCode()]))!.Value);
        Assert.True((await db.UserEntityStates.FindAsync([TestUserContext.UserId, seriesId]))!.IsFavorite);
        var installmentProgress = await db.UserEntityStates.FindAsync([
            TestUserContext.UserId,
            installmentId
        ]);
        Assert.Equal(installmentId, installmentProgress!.ProgressCurrentEntityId);
        Assert.Equal(4, installmentProgress.ProgressIndex);
        Assert.Equal(12, installmentProgress.ProgressTotal);
    }

    [Fact]
    public async Task RootArchiveBookIsAdoptedAsTheInstallmentUnderANewSeries() {
        await using var db = CreateContext();
        var rootPath = Path.Combine(Path.GetTempPath(), $"prismedia-root-comic-{Guid.NewGuid():N}");
        var archivePath = Path.Combine(rootPath, "One Shot.cbz");
        SeedLibraryRoot(db, RootId, rootPath);
        await db.SaveChangesAsync();
        var service = new LibraryScanPersistenceService(db);
        var legacyBookId = await service.UpsertBookAsync(
            archivePath,
            "One Shot",
            RootId,
            isNsfw: false,
            CancellationToken.None);

        var seriesId = await service.UpsertComicSeriesAsync(
            folderPath: null,
            "One Shot",
            RootId,
            isNsfw: false,
            CancellationToken.None);
        var installmentId = await service.UpsertComicInstallmentAsync(
            archivePath,
            "One Shot",
            RootId,
            seriesId,
            sortOrder: 0,
            position: 1,
            positionLabel: "1",
            ComicInstallmentKind.OneShot,
            sizeBytes: null,
            isNsfw: false,
            sourceProvenance: null,
            CancellationToken.None);

        Assert.Equal(legacyBookId, installmentId);
        Assert.NotEqual(seriesId, installmentId);
        var installment = await db.Entities.FindAsync([installmentId]);
        Assert.Equal(EntityKind.ComicInstallment.ToCode(), installment!.KindCode);
        Assert.Equal(seriesId, installment.ParentEntityId);
        Assert.Null(await db.BookDetails.FindAsync([installmentId]));
        Assert.NotNull(await db.ComicInstallmentDetails.FindAsync([installmentId]));
    }

    [Fact]
    public async Task BookCleanupDefersLegacyArchivesUntilComicCleanupCanOwnTheirRemoval() {
        await using var db = CreateContext();
        var rootPath = Path.Combine(Path.GetTempPath(), $"prismedia-comic-cutover-{Guid.NewGuid():N}");
        var archivePath = Path.Combine(rootPath, "Missing.cbz");
        var pdfPath = Path.Combine(rootPath, "Missing.pdf");
        SeedLibraryRoot(db, RootId, rootPath);
        await db.SaveChangesAsync();
        var service = new LibraryScanPersistenceService(db);
        var legacyComicId = await service.UpsertBookAsync(
            archivePath,
            "Missing Comic",
            RootId,
            isNsfw: false,
            CancellationToken.None);
        var proseBookId = await service.UpsertSingleFileBookAsync(
            pdfPath,
            "Missing Book",
            RootId,
            isNsfw: false,
            BookType.Book,
            BookFormat.Pdf,
            Prismedia.Contracts.Media.MediaContentTypes.Pdf,
            parentBookEntityId: null,
            sortOrder: null,
            CancellationToken.None);

        var removedByBookScan = await service.RemoveStaleBooksInRootAsync(
            RootId,
            new HashSet<string>(),
            CancellationToken.None);

        Assert.Equal(1, removedByBookScan);
        Assert.Null(await db.Entities.FindAsync([proseBookId]));
        Assert.NotNull(await db.Entities.FindAsync([legacyComicId]));

        var removedByComicScan = await service.RemoveStaleComicInstallmentsInRootAsync(
            RootId,
            new HashSet<string>(),
            CancellationToken.None);

        Assert.Equal(1, removedByComicScan);
        Assert.Null(await db.Entities.FindAsync([legacyComicId]));
    }

    [Fact]
    public async Task GeneratedComicSourceKeepsStableIdentityAndFolderProvenance() {
        await using var db = CreateContext();
        var rootPath = Path.Combine(Path.GetTempPath(), $"prismedia-comics-{Guid.NewGuid():N}");
        var originPath = Path.Combine(rootPath, "Series", "Chapter 1");
        var firstManagedPath = Path.Combine(Path.GetTempPath(), $"managed-{Guid.NewGuid():N}.cbz");
        var secondManagedPath = Path.Combine(Path.GetTempPath(), $"managed-{Guid.NewGuid():N}.cbz");
        SeedLibraryRoot(db, RootId, rootPath);
        await db.SaveChangesAsync();
        var service = new LibraryScanPersistenceService(db);
        var seriesId = await service.UpsertComicSeriesAsync(
            Path.Combine(rootPath, "Series"),
            "Series",
            RootId,
            isNsfw: false,
            CancellationToken.None);

        var firstId = await service.UpsertComicInstallmentAsync(
            firstManagedPath,
            "Chapter 1",
            RootId,
            seriesId,
            sortOrder: 0,
            position: 1,
            positionLabel: "1",
            ComicInstallmentKind.Chapter,
            sizeBytes: 100,
            isNsfw: false,
            new ComicSourceProvenance(originPath, "first-signature"),
            CancellationToken.None);
        var secondId = await service.UpsertComicInstallmentAsync(
            secondManagedPath,
            "Chapter 1",
            RootId,
            seriesId,
            sortOrder: 0,
            position: 1,
            positionLabel: "1",
            ComicInstallmentKind.Chapter,
            sizeBytes: 200,
            isNsfw: false,
            new ComicSourceProvenance(originPath, "second-signature"),
            CancellationToken.None);

        Assert.Equal(firstId, secondId);
        Assert.Equal(
            secondManagedPath,
            Assert.Single(db.EntityFiles.Where(row => row.EntityId == firstId)).Path);
        Assert.Equal(
            originPath,
            (await db.EntitySources.FindAsync([
                firstId,
                EntitySourceCode.GeneratedFromFolder.ToCode()
            ]))!.Value);
        Assert.Equal(0, await service.RemoveEntitiesOutsideLibraryRootsAsync(CancellationToken.None));
        Assert.NotNull(await db.Entities.FindAsync([firstId]));
    }

    [Fact]
    public async Task RootLevelComicSeriesUsesCatalogIdentityAndPrunesAfterSourceDisappears() {
        await using var db = CreateContext();
        var rootPath = Path.Combine(Path.GetTempPath(), $"prismedia-comics-{Guid.NewGuid():N}");
        var archivePath = Path.Combine(rootPath, "Issue 1.cbz");
        SeedLibraryRoot(db, RootId, rootPath);
        await db.SaveChangesAsync();
        var service = new LibraryScanPersistenceService(db);

        var firstSeriesId = await service.UpsertComicSeriesAsync(
            folderPath: null,
            "Metadata Series",
            RootId,
            isNsfw: false,
            CancellationToken.None);
        var secondSeriesId = await service.UpsertComicSeriesAsync(
            folderPath: null,
            "metadata series",
            RootId,
            isNsfw: false,
            CancellationToken.None);
        var installmentId = await service.UpsertComicInstallmentAsync(
            archivePath,
            "Issue 1",
            RootId,
            firstSeriesId,
            sortOrder: 0,
            position: 1,
            positionLabel: "1",
            ComicInstallmentKind.Issue,
            sizeBytes: null,
            isNsfw: false,
            sourceProvenance: null,
            CancellationToken.None);

        Assert.Equal(firstSeriesId, secondSeriesId);
        Assert.Empty(db.EntitySources.Where(row => row.EntityId == firstSeriesId));
        Assert.Equal(0, await service.RemoveStaleComicInstallmentsInRootAsync(
            RootId,
            new HashSet<string> { archivePath },
            CancellationToken.None));
        Assert.Equal(1, await service.RemoveStaleComicInstallmentsInRootAsync(
            RootId,
            new HashSet<string>(),
            CancellationToken.None));
        Assert.Null(await db.Entities.FindAsync([installmentId]));
        Assert.Equal(1, await service.RemoveEmptyComicContainersAsync(CancellationToken.None));
        Assert.Null(await db.Entities.FindAsync([firstSeriesId]));
    }

    private static string CreateCacheRoot() {
        var path = Path.Combine(Path.GetTempPath(), $"prismedia-test-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static AssetPathService Assets(string cacheRoot) =>
        new(Path.GetDirectoryName(cacheRoot) ?? cacheRoot, cacheRoot);

    private static void WriteCacheFile(string cacheRoot, string assetPath) {
        const string prefix = "/assets/";
        var relative = assetPath[prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
        var path = Path.Combine(cacheRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0xff, 0xd8, 0xff, 0xd9]);
    }

    private static void DeleteDirectory(string path) {
        if (Directory.Exists(path)) {
            Directory.Delete(path, recursive: true);
        }
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"library-scan-persistence-{Guid.NewGuid():N}")
            .Options;

        return new PrismediaDbContext(options);
    }

    [Fact]
    public async Task VideoAutoIdentifyRootsIncludeEveryDirectPlayableSourceOwner() {
        await using var db = CreateContext();
        var rootId = Guid.NewGuid();
        var movieId = Guid.NewGuid();
        var videoId = Guid.NewGuid();
        var seriesId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        SeedLibraryRoot(db, rootId, "/media/videos");
        db.Entities.AddRange(
            new EntityRow { Id = movieId, KindCode = EntityKind.Movie.ToCode(), Title = "Movie", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = videoId, KindCode = EntityKind.Video.ToCode(), Title = "Video", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = seriesId, KindCode = EntityKind.VideoSeries.ToCode(), Title = "Series", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = episodeId, KindCode = EntityKind.VideoEpisode.ToCode(), Title = "Episode", ParentEntityId = seriesId, CreatedAt = now, UpdatedAt = now });
        db.EntityLibraryRoots.AddRange(
            new EntityLibraryRootRow { EntityId = movieId, LibraryRootId = rootId },
            new EntityLibraryRootRow { EntityId = videoId, LibraryRootId = rootId },
            new EntityLibraryRootRow { EntityId = episodeId, LibraryRootId = rootId });
        await db.SaveChangesAsync();

        var roots = await new LibraryScanPersistenceService(db)
            .ResolveAutoIdentifyRootsForLibraryRootAsync(rootId, [MediaCategory.Video], CancellationToken.None);

        Assert.Equal(
            new[] { movieId, seriesId, videoId }.OrderBy(id => id),
            roots.Select(root => root.Id).OrderBy(id => id));
    }

    private static void SeedVideo(PrismediaDbContext db, Guid videoId, string? sourcePath = null) {
        db.Entities.Add(new EntityRow {
            Id = videoId,
            KindCode = EntityKind.Video.ToCode(),
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

    private sealed class FixedAudioProbe : IMediaProbe {
        public Task<AudioProbeData?> ProbeAudioAsync(string filePath, CancellationToken cancellationToken) =>
            Task.FromResult<AudioProbeData?>(new AudioProbeData(
                180,
                4,
                900_000,
                "flac",
                "flac",
                48_000,
                2,
                "Artist",
                "Album",
                "Track",
                "1"));

        public Task<VideoProbeData?> ProbeVideoAsync(string filePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ImageProbeData?> ProbeImageAsync(string filePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SubtitleStreamData>> ProbeSubtitleStreamsAsync(
            string filePath,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
