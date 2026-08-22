using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Domain.Taxonomy;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Entities.Mappers;
using Prismedia.Infrastructure.Entities.Thumbnails;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using ProviderIdentityCapability = Prismedia.Contracts.Entities.ProviderIdentityCapability;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfEntityRepositoryTests {
    private static readonly DateTimeOffset IdentityCreatedAt =
        DateTimeOffset.Parse("2026-01-02T03:04:05Z");
    private static readonly DateTimeOffset IdentityWriteTime =
        DateTimeOffset.Parse("2026-07-09T18:30:00Z");

    [Fact]
    public async Task ComicInstallmentHydrationPreservesSubtypeAndStructuralPlacement() {
        await using var db = CreateContext();
        var seriesId = Guid.NewGuid();
        var installmentId = Guid.NewGuid();
        SeedEntity(db, seriesId, EntityKind.ComicSeries, "Series");
        SeedEntity(db, installmentId, EntityKind.ComicInstallment, "Issue 10.5", seriesId, sortOrder: 11);
        db.ComicSeriesDetails.Add(new ComicSeriesDetailRow { EntityId = seriesId, Status = "releasing" });
        db.ComicInstallmentDetails.Add(new ComicInstallmentDetailRow {
            EntityId = installmentId,
            InstallmentKind = ComicInstallmentKind.Issue
        });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(
            db,
            TestUserContext.Admin(),
            EntityMappers.Kinds(db),
            EntityMappers.Capabilities(db, TestUserContext.Admin()));

        var series = await repository.RequireAsync<ComicSeries>(seriesId, CancellationToken.None);
        var installment = await repository.RequireAsync<ComicInstallment>(installmentId, CancellationToken.None);

        Assert.Equal("releasing", series.Status);
        Assert.Equal(ComicInstallmentKind.Issue, installment.InstallmentKind);
        Assert.Equal(seriesId, installment.ParentEntityId);
        Assert.Equal(11, installment.SortOrder);
    }

    [Fact]
    public async Task ComicInstallmentHydrationRejectsMissingRequiredSubtype() {
        await using var db = CreateContext();
        var seriesId = Guid.NewGuid();
        var installmentId = Guid.NewGuid();
        SeedEntity(db, seriesId, EntityKind.ComicSeries, "Series");
        SeedEntity(db, installmentId, EntityKind.ComicInstallment, "Unclassified", seriesId);
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(
            db,
            TestUserContext.Admin(),
            EntityMappers.Kinds(db),
            EntityMappers.Capabilities(db, TestUserContext.Admin()));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.RequireAsync<ComicInstallment>(installmentId, CancellationToken.None));
    }

    [Fact]
    public async Task PageManifestHydrationAddsTheGenericPageSequenceCapability() {
        await using var db = CreateContext();
        var seriesId = Guid.NewGuid();
        var installmentId = Guid.NewGuid();
        SeedEntity(db, seriesId, EntityKind.ComicSeries, "Series");
        SeedEntity(db, installmentId, EntityKind.ComicInstallment, "Chapter 1", seriesId);
        db.ComicInstallmentDetails.Add(new ComicInstallmentDetailRow {
            EntityId = installmentId,
            InstallmentKind = ComicInstallmentKind.Chapter
        });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = installmentId,
            Role = EntityFileRole.Source,
            Path = "/media/chapter.cbz",
            CreatedAt = IdentityCreatedAt,
            UpdatedAt = IdentityCreatedAt
        });
        db.EntityPageManifests.Add(new EntityPageManifestRow {
            EntityId = installmentId,
            Direction = PageReadingDirection.RightToLeft,
            DefaultMode = ReaderMode.Paged,
            CoverOrdinal = 0,
            SourceSignature = "source:v1",
            UpdatedAt = IdentityCreatedAt
        });
        db.EntityPageEntries.AddRange(
            new EntityPageEntryRow {
                EntityId = installmentId,
                Ordinal = 0,
                ArchiveMember = "001.jpg",
                MimeType = "image/jpeg",
                PageType = PageType.FrontCover
            },
            new EntityPageEntryRow {
                EntityId = installmentId,
                Ordinal = 1,
                ArchiveMember = "002.jpg",
                MimeType = "image/jpeg",
                PageType = PageType.Story
            });
        db.EntityStats.Add(new EntityStatRow {
            EntityId = installmentId,
            Code = EntityStatCodes.Pages,
            Value = 2,
            UpdatedAt = IdentityCreatedAt
        });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(
            db,
            TestUserContext.Admin(),
            EntityMappers.Kinds(db),
            EntityMappers.Capabilities(db, TestUserContext.Admin()));

        var installment = await repository.RequireAsync<ComicInstallment>(
            installmentId,
            CancellationToken.None);
        var capability = installment.GetCapability<CapabilityPageSequence>();

        Assert.NotNull(capability);
        Assert.Equal(2, capability.PageCount);
        Assert.Equal(PageReadingDirection.RightToLeft, capability.Direction);
        Assert.Equal(ReaderMode.Paged, capability.DefaultMode);
        Assert.Equal(0, capability.CoverOrdinal);
    }

    [Fact]
    public async Task PageManifestHydrationRequiresTheCachedPageCountStatistic() {
        await using var db = CreateContext();
        var seriesId = Guid.NewGuid();
        var installmentId = Guid.NewGuid();
        SeedEntity(db, seriesId, EntityKind.ComicSeries, "Series");
        SeedEntity(db, installmentId, EntityKind.ComicInstallment, "Chapter 1", seriesId);
        db.ComicInstallmentDetails.Add(new ComicInstallmentDetailRow {
            EntityId = installmentId,
            InstallmentKind = ComicInstallmentKind.Chapter
        });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = installmentId,
            Role = EntityFileRole.Source,
            Path = "/media/chapter.cbz",
            CreatedAt = IdentityCreatedAt,
            UpdatedAt = IdentityCreatedAt
        });
        db.EntityPageManifests.Add(new EntityPageManifestRow {
            EntityId = installmentId,
            Direction = PageReadingDirection.LeftToRight,
            DefaultMode = ReaderMode.Paged,
            SourceSignature = "source:v1",
            UpdatedAt = IdentityCreatedAt
        });
        db.EntityPageEntries.Add(new EntityPageEntryRow {
            EntityId = installmentId,
            Ordinal = 0,
            ArchiveMember = "001.jpg",
            MimeType = "image/jpeg",
            PageType = PageType.Story
        });
        await db.SaveChangesAsync();
        var repository = new EfEntityRepository(
            db,
            TestUserContext.Admin(),
            EntityMappers.Kinds(db),
            EntityMappers.Capabilities(db, TestUserContext.Admin()));

        var installment = await repository.RequireAsync<ComicInstallment>(
            installmentId,
            CancellationToken.None);

        Assert.Null(installment.GetCapability<CapabilityPageSequence>());
    }

    [Fact]
    public async Task SubtitleHydrationOnlyAttachesStateToSupportedOrPersistedEntities() {
        await using var db = CreateContext();
        var tagId = Guid.NewGuid();
        var videoId = Guid.NewGuid();
        SeedEntity(db, tagId, EntityKind.Tag, "No subtitle capability");
        SeedEntity(db, videoId, EntityKind.Video, "Playable video");
        db.EntitySubtitleStates.Add(new EntitySubtitleStateRow {
            EntityId = videoId,
            SubtitlesExtractedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(
            db,
            TestUserContext.Admin(),
            EntityMappers.Kinds(db),
            EntityMappers.Capabilities(db, TestUserContext.Admin()));

        var tag = await repository.RequireAsync<Tag>(tagId, CancellationToken.None);
        var video = await repository.RequireAsync<Video>(videoId, CancellationToken.None);

        Assert.Null(tag.GetCapability<CapabilitySubtitles>());
        Assert.NotNull(video.SubtitleCapability);
        Assert.NotNull(video.SubtitleCapability!.ExtractedAt);
    }

    [Fact]
    public async Task FindShallowAsyncPreservesStructuralPlacementForCoreMutations() {
        await using var db = CreateContext();
        var libraryId = Guid.Parse("10101010-1010-1010-1010-101010101010");
        var trackId = Guid.Parse("20202020-2020-2020-2020-202020202020");
        SeedEntity(db, libraryId, EntityKind.AudioLibrary, "Library");
        SeedEntity(db, trackId, EntityKind.AudioTrack, "Track 1", libraryId, sortOrder: 7);
        db.AudioTrackDetails.Add(new AudioTrackDetailRow { EntityId = trackId });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var track = await repository.FindShallowAsync(trackId, CancellationToken.None);

        Assert.NotNull(track);
        Assert.Equal(libraryId, track.ParentEntityId);
        Assert.Equal(7, track.SortOrder);

        track.Rate(4);
        await repository.SaveMutableStateAsync(
            track,
            new EntityMutableStateChange(userOpinionChanged: true),
            CancellationToken.None);

        var row = await db.Entities.SingleAsync(entity => entity.Id == trackId);
        Assert.Equal(libraryId, row.ParentEntityId);
        Assert.Equal(7, row.SortOrder);
        var state = await db.UserEntityStates.SingleAsync(s => s.EntityId == trackId);
        Assert.Equal(TestUserContext.UserId, state.UserId);
        Assert.Equal(4, state.RatingValue);
    }

    [Fact]
    public async Task FindShallowAsyncRejectsAnOptionalRootWithTheWrongPersistedParentKind() {
        await using var db = CreateContext();
        var bookId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        SeedEntity(db, bookId, EntityKind.Book, "Book");
        SeedEntity(db, imageId, EntityKind.Image, "Wrong parent", bookId);
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(
            db,
            TestUserContext.Admin(),
            EntityMappers.Kinds(db),
            EntityMappers.Capabilities(db, TestUserContext.Admin()));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.FindShallowAsync(imageId, CancellationToken.None));
    }

    [Fact]
    public async Task RatingMutationPreservesRelatedFilesAndCreditedPeopleAndReturnsCanonicalCard() {
        await using var db = CreateContext();
        var videoId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        SeedEntity(db, videoId, EntityKind.Video, "Video");
        SeedEntity(db, personId, EntityKind.Person, "Person");
        await db.SaveChangesAsync();
        db.Entities.Single(row => row.Id == personId).IsNsfw = true;
        db.EntityFiles.Add(new EntityFileRow {
            Id = fileId,
            EntityId = videoId,
            Role = EntityFileRole.Source,
            Path = "/media/video.mkv",
            MimeType = "video/x-matroska",
            SizeBytes = 7_654_321,
            Source = FileSourceKind.Custom.ToCode(),
            CreatedAt = IdentityCreatedAt,
            UpdatedAt = IdentityCreatedAt,
        });
        db.EntityRelationshipLinks.Add(new EntityRelationshipLinkRow {
            EntityId = videoId,
            RelationshipCode = RelationshipKind.Credits.ToCode(),
            Label = "Lead",
            TargetEntityId = personId,
            TargetKindCode = EntityKind.Person.ToCode(),
            MetadataJson = """{"role":"actor"}""",
            CreatedAt = IdentityCreatedAt,
        });
        db.PersonDetails.Add(new PersonDetailRow {
            EntityId = personId,
            Disambiguation = "Original performer",
            Country = "US",
        });
        AddExternalIdentity(db, personId, "tmdb", "person-42", "https://example.test/person/42", IdentityCreatedAt);
        await db.SaveChangesAsync();

        var user = TestUserContext.Admin();
        var repository = new EfEntityRepository(
            db,
            user,
            EntityMappers.Kinds(db),
            EntityMappers.Capabilities(db, user));
        var reads = new EfEntityReadService(
            db,
            user,
            repository,
            ThumbnailContributors.For(db),
            new EfEntityProgressTopologyResolver(db));
        var service = new EntityCapabilityService(
            repository,
            reads,
            new EfEntityProgressTopologyResolver(db));

        var response = await service.RateAsync(videoId, 4, CancellationToken.None);
        var canonical = await reads.GetAsync(videoId, hideNsfw: false, CancellationToken.None);

        Assert.NotNull(response);
        Assert.NotNull(canonical);
        Assert.Equivalent(canonical, response);

        db.ChangeTracker.Clear();
        var file = await db.EntityFiles.AsNoTracking().SingleAsync(row => row.Id == fileId);
        Assert.Equal("/media/video.mkv", file.Path);
        Assert.Equal("video/x-matroska", file.MimeType);
        Assert.Equal(7_654_321, file.SizeBytes);
        Assert.Equal(FileSourceKind.Custom.ToCode(), file.Source);
        Assert.Equal(IdentityCreatedAt, file.CreatedAt);

        var credit = await db.EntityRelationshipLinks.AsNoTracking().SingleAsync(row => row.EntityId == videoId);
        Assert.Equal(personId, credit.TargetEntityId);
        Assert.Equal(RelationshipKind.Credits.ToCode(), credit.RelationshipCode);
        var person = await db.Entities.AsNoTracking().SingleAsync(row => row.Id == personId);
        Assert.True(person.IsNsfw);
        var detail = await db.PersonDetails.AsNoTracking().SingleAsync(row => row.EntityId == personId);
        Assert.Equal("Original performer", detail.Disambiguation);
        Assert.Equal("US", detail.Country);
        var identity = await db.EntityExternalIds.AsNoTracking().SingleAsync(row => row.EntityId == personId);
        Assert.Equal("tmdb", identity.Provider);
        Assert.Equal("person-42", identity.Value);
        Assert.Equal("https://example.test/person/42", identity.Url);
        Assert.Equal(4, (await db.UserEntityStates.SingleAsync(row => row.EntityId == videoId)).RatingValue);
    }

    [Fact]
    public async Task UnrelatedEntitySavePreservesMissingManagedSubtitleForRecovery() {
        var subtitlePath = Path.GetTempFileName();
        try {
            await using var db = CreateContext();
            var videoId = Guid.NewGuid();
            var subtitleId = Guid.NewGuid();
            SeedEntity(db, videoId, EntityKind.Video, "Stable subtitles");
            db.EntitySubtitleStates.Add(new EntitySubtitleStateRow {
                EntityId = videoId,
                SubtitlesExtractedAt = DateTimeOffset.UtcNow
            });
            db.EntitySubtitles.Add(new EntitySubtitleRow {
                Id = subtitleId,
                EntityId = videoId,
                Language = "eng",
                Format = "vtt",
                Source = EntitySubtitleSource.Embedded,
                SourceKey = SubtitleSourceKeys.EmbeddedStream(7),
                StoragePath = subtitlePath,
                SourceFormat = "subrip",
                SourcePath = "7",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            var repository = new EfEntityRepository(
                db,
                TestUserContext.Admin(),
                EntityMappers.Kinds(db),
                EntityMappers.Capabilities(db, TestUserContext.Admin()));

            var video = await repository.RequireAsync<Video>(videoId, CancellationToken.None);
            File.Delete(subtitlePath);
            video.Rate(4);
            await repository.SaveMutableStateAsync(
                video,
                new EntityMutableStateChange(userOpinionChanged: true),
                CancellationToken.None);

            var row = Assert.Single(await db.EntitySubtitles.AsNoTracking()
                .Where(subtitle => subtitle.EntityId == videoId)
                .ToArrayAsync());
            Assert.Equal(subtitleId, row.Id);
            Assert.Equal(SubtitleSourceKeys.EmbeddedStream(7), row.SourceKey);
            var needs = await new Prismedia.Infrastructure.Media.Persistence.LibraryScanPersistenceService(db)
                .CheckDownstreamNeedsBatchAsync([videoId], CancellationToken.None);
            Assert.True(needs[videoId].NeedsSubtitleExtraction);
        } finally {
            File.Delete(subtitlePath);
        }
    }

    [Fact]
    public async Task StaleEntitySaveCannotOverwriteManagedSubtitlePipelineState() {
        var databaseName = $"subtitle-race-{Guid.NewGuid():N}";
        var oldPath = Path.GetTempFileName();
        var newPath = Path.GetTempFileName();
        var addedPath = Path.GetTempFileName();
        try {
            var videoId = Guid.NewGuid();
            var embeddedId = Guid.NewGuid();
            var sidecarId = Guid.NewGuid();
            var originalTimestamp = DateTimeOffset.UtcNow.AddHours(-2);
            await using (var setup = CreateContext(databaseName)) {
                SeedEntity(setup, videoId, EntityKind.Video, "Concurrent subtitles");
                setup.EntitySubtitleStates.Add(new EntitySubtitleStateRow {
                    EntityId = videoId,
                    SubtitlesExtractedAt = originalTimestamp,
                    SubtitleSidecarSignature = new string('a', 64)
                });
                setup.EntitySubtitles.Add(new EntitySubtitleRow {
                    Id = embeddedId,
                    EntityId = videoId,
                    Language = "en",
                    Format = SubtitleFormats.Vtt,
                    Source = EntitySubtitleSource.Embedded,
                    SourceKey = SubtitleSourceKeys.EmbeddedStream(3),
                    StoragePath = oldPath,
                    SourceFormat = SubtitleFormats.Srt,
                    SourcePath = "3",
                    CreatedAt = originalTimestamp
                });
                await setup.SaveChangesAsync();
            }

            await using var staleContext = CreateContext(databaseName);
            var staleRepository = new EfEntityRepository(
                staleContext,
                TestUserContext.Admin(),
                EntityMappers.Kinds(staleContext),
                EntityMappers.Capabilities(staleContext, TestUserContext.Admin()));
            var staleVideo = await staleRepository.RequireAsync<Video>(videoId, CancellationToken.None);

            await using (var pipeline = CreateContext(databaseName)) {
                var detail = await pipeline.EntitySubtitleStates.FindAsync([videoId]);
                detail!.SubtitlesExtractedAt = null;
                var embedded = await pipeline.EntitySubtitles.FindAsync([embeddedId]);
                embedded!.StoragePath = newPath;
                pipeline.EntitySubtitles.Add(new EntitySubtitleRow {
                    Id = sidecarId,
                    EntityId = videoId,
                    Language = "fr",
                    Format = SubtitleFormats.Vtt,
                    Source = EntitySubtitleSource.Sidecar,
                    SourceKey = new string('b', 64),
                    StoragePath = addedPath,
                    SourceFormat = SubtitleFormats.Srt,
                    CreatedAt = DateTimeOffset.UtcNow
                });
                await pipeline.SaveChangesAsync();
            }

            staleVideo.Rate(4);
            await staleRepository.SaveMutableStateAsync(
                staleVideo,
                new EntityMutableStateChange(userOpinionChanged: true),
                CancellationToken.None);

            await using var verification = CreateContext(databaseName);
            var currentDetail = await verification.EntitySubtitleStates.AsNoTracking()
                .SingleAsync(row => row.EntityId == videoId);
            Assert.Null(currentDetail.SubtitlesExtractedAt);
            Assert.Equal(new string('a', 64), currentDetail.SubtitleSidecarSignature);
            var currentRows = await verification.EntitySubtitles.AsNoTracking()
                .Where(row => row.EntityId == videoId)
                .OrderBy(row => row.Id)
                .ToArrayAsync();
            Assert.Equal(2, currentRows.Length);
            Assert.Equal(newPath, Assert.Single(currentRows, row => row.Id == embeddedId).StoragePath);
            Assert.Equal(addedPath, Assert.Single(currentRows, row => row.Id == sidecarId).StoragePath);
        } finally {
            File.Delete(oldPath);
            File.Delete(newPath);
            File.Delete(addedPath);
        }
    }

    [Fact]
    public async Task FindAsyncHydratesExternalIdentitiesThroughTheCanonicalStore() {
        await using var db = CreateContext();
        var id = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");
        SeedEntity(db, id, EntityKind.Video, "Canonical identity");
        await db.SaveChangesAsync();
        var identities = new RecordingExternalIdentityStore(
            listedIdentities: [new EntityExternalId(" TMDB ", " 603 ", "https://www.themoviedb.org/movie/603")]);
        var repository = new EfEntityRepository(
            db,
            TestUserContext.Admin(),
            EntityMappers.Kinds(db),
            EntityMappers.Capabilities(db, TestUserContext.Admin()),
            identities);

        var loaded = await repository.RequireAsync<Video>(id, CancellationToken.None);

        Assert.Equal([id], identities.ListedEntityIds);
        var externalId = Assert.Single(loaded.ExternalIds);
        Assert.Equal(new ExternalIdentity("tmdb", "603"), externalId.Identity);
        Assert.Equal("https://www.themoviedb.org/movie/603", externalId.Url);
    }

    [Fact]
    public async Task FindAsyncProjectsPersistedProviderIdentityAndPrefersExactPluginUrl() {
        await using var db = CreateContext();
        var id = Guid.NewGuid();
        SeedEntity(db, id, EntityKind.Video, "Persisted provider identity");
        AddExternalIdentity(
            db,
            id,
            "tmdb",
            "603",
            "https://www.themoviedb.org/movie/603",
            IdentityCreatedAt);
        await db.SaveChangesAsync();
        var externalIdentities = new EfEntityExternalIdentityStore(db, TimeProvider.System);
        var providerIdentities = new EfEntityProviderIdentityStore(db, TimeProvider.System);
        await providerIdentities.SetAsync(
            id,
            "cinema-metadata",
            new ExternalIdentity("tmdb", "603"),
            CancellationToken.None);
        await db.SaveChangesAsync();
        var identityUrls = new RecordingIdentityUrlResolver("https://cinema-metadata.example/movie/603");
        var repository = new EfEntityRepository(
            db,
            TestUserContext.Admin(),
            EntityMappers.Kinds(db),
            EntityMappers.Capabilities(db, TestUserContext.Admin()),
            externalIdentities,
            providerIdentities,
            new ConfiguredIdentityRouter(),
            identityUrls);

        var loaded = await repository.RequireAsync<Video>(id, CancellationToken.None);

        Assert.NotNull(loaded.ProviderIdentity);
        Assert.Equal("cinema-metadata", loaded.ProviderIdentity.PluginId);
        Assert.Equal(new ExternalIdentity("tmdb", "603"), loaded.ProviderIdentity.Identity);
        Assert.Equal("https://cinema-metadata.example/movie/603", loaded.ProviderIdentity.Url);
        var urlCall = Assert.Single(identityUrls.Calls);
        Assert.Equal(EntityKind.Video.ToCode(), urlCall.EntityKindCode);
        Assert.Equal(
            new PluginIdentityRoute("cinema-metadata", new ExternalIdentity("tmdb", "603")),
            urlCall.Route);
        var capability = Assert.Single(
            EntityCardProjector.ToCard(loaded, hasSourceBackedSubtree: false).Capabilities.OfType<ProviderIdentityCapability>());
        Assert.Equal("cinema-metadata", capability.PluginId);
        Assert.Equal("tmdb", capability.IdentityNamespace);
        Assert.Equal("603", capability.IdentityValue);
        Assert.Equal("https://cinema-metadata.example/movie/603", capability.Url);
    }

    [Fact]
    public async Task FindAsyncFallsBackToExternalIdentityUrlWhenPluginDeclaresNoUrlFormat() {
        await using var db = CreateContext();
        var id = Guid.NewGuid();
        SeedEntity(db, id, EntityKind.Video, "Provider identity URL fallback");
        AddExternalIdentity(
            db,
            id,
            "tmdb",
            "603",
            "https://www.themoviedb.org/movie/603",
            IdentityCreatedAt);
        await db.SaveChangesAsync();
        var providerIdentities = new EfEntityProviderIdentityStore(db, TimeProvider.System);
        await providerIdentities.SetAsync(
            id,
            "cinema-metadata",
            new ExternalIdentity("tmdb", "603"),
            CancellationToken.None);
        await db.SaveChangesAsync();
        var identityUrls = new RecordingIdentityUrlResolver(url: null);
        var repository = new EfEntityRepository(
            db,
            TestUserContext.Admin(),
            EntityMappers.Kinds(db),
            EntityMappers.Capabilities(db, TestUserContext.Admin()),
            new EfEntityExternalIdentityStore(db, TimeProvider.System),
            providerIdentities,
            new ConfiguredIdentityRouter(),
            identityUrls);

        var loaded = await repository.RequireAsync<Video>(id, CancellationToken.None);

        Assert.Equal("https://www.themoviedb.org/movie/603", loaded.ProviderIdentity?.Url);
        Assert.Single(identityUrls.Calls);
    }

    [Fact]
    public async Task FindAsyncInfersLegacyProviderIdentityWhenExactlyOneManifestRouteExists() {
        await using var db = CreateContext();
        var id = Guid.NewGuid();
        SeedEntity(db, id, EntityKind.Video, "Legacy provider identity");
        AddExternalIdentity(db, id, "tmdb", "603", url: null, IdentityCreatedAt);
        await db.SaveChangesAsync();
        var identity = new ExternalIdentity("tmdb", "603");
        var identityUrls = new RecordingIdentityUrlResolver("https://www.themoviedb.org/movie/603");
        var repository = new EfEntityRepository(
            db,
            TestUserContext.Admin(),
            EntityMappers.Kinds(db),
            EntityMappers.Capabilities(db, TestUserContext.Admin()),
            new EfEntityExternalIdentityStore(db, TimeProvider.System),
            new EfEntityProviderIdentityStore(db, TimeProvider.System),
            new ConfiguredIdentityRouter(new PluginIdentityRoute("tmdb", identity)),
            identityUrls);

        var loaded = await repository.RequireAsync<Video>(id, CancellationToken.None);

        Assert.Equal("tmdb", loaded.ProviderIdentity?.PluginId);
        Assert.Equal(identity, loaded.ProviderIdentity?.Identity);
        Assert.Equal("https://www.themoviedb.org/movie/603", loaded.ProviderIdentity?.Url);
        var call = Assert.Single(identityUrls.Calls);
        Assert.Equal(EntityKind.Video.ToCode(), call.EntityKindCode);
        Assert.Equal(new PluginIdentityRoute("tmdb", identity), call.Route);
        Assert.Single(EntityCardProjector.ToCard(loaded, hasSourceBackedSubtree: false).Capabilities.OfType<ProviderIdentityCapability>());
    }

    [Fact]
    public async Task FindAsyncDoesNotInferLegacyProviderIdentityWhenManifestRoutesAreAmbiguous() {
        await using var db = CreateContext();
        var id = Guid.NewGuid();
        SeedEntity(db, id, EntityKind.Video, "Ambiguous provider identity");
        AddExternalIdentity(db, id, "tmdb", "603", url: null, IdentityCreatedAt);
        await db.SaveChangesAsync();
        var identity = new ExternalIdentity("tmdb", "603");
        var identityUrls = new RecordingIdentityUrlResolver("https://www.themoviedb.org/movie/603");
        var repository = new EfEntityRepository(
            db,
            TestUserContext.Admin(),
            EntityMappers.Kinds(db),
            EntityMappers.Capabilities(db, TestUserContext.Admin()),
            new EfEntityExternalIdentityStore(db, TimeProvider.System),
            new EfEntityProviderIdentityStore(db, TimeProvider.System),
            new ConfiguredIdentityRouter(
                new PluginIdentityRoute("alpha-provider", identity),
                new PluginIdentityRoute("zeta-provider", identity)),
            identityUrls);

        var loaded = await repository.RequireAsync<Video>(id, CancellationToken.None);

        Assert.Null(loaded.ProviderIdentity);
        Assert.Empty(identityUrls.Calls);
        Assert.Empty(EntityCardProjector.ToCard(loaded, hasSourceBackedSubtree: false).Capabilities.OfType<ProviderIdentityCapability>());
    }

    [Fact]
    public async Task FindAsyncDoesNotReplaceStalePersistedRouteWithLegacyInference() {
        await using var db = CreateContext();
        var id = Guid.NewGuid();
        SeedEntity(db, id, EntityKind.Video, "Stale provider identity");
        var rawIdentity = AddExternalIdentity(db, id, "tmdb", "603", url: null, IdentityCreatedAt);
        await db.SaveChangesAsync();
        var providerIdentities = new EfEntityProviderIdentityStore(db, TimeProvider.System);
        await providerIdentities.SetAsync(
            id,
            "tmdb",
            new ExternalIdentity("tmdb", "603"),
            CancellationToken.None);
        await db.SaveChangesAsync();
        rawIdentity.Value = "604";
        await db.SaveChangesAsync();
        var router = new ConfiguredIdentityRouter(
            new PluginIdentityRoute("tmdb", new ExternalIdentity("tmdb", "604")));
        var repository = new EfEntityRepository(
            db,
            TestUserContext.Admin(),
            EntityMappers.Kinds(db),
            EntityMappers.Capabilities(db, TestUserContext.Admin()),
            new EfEntityExternalIdentityStore(db, TimeProvider.System),
            providerIdentities,
            router,
            new RecordingIdentityUrlResolver("https://www.themoviedb.org/movie/604"));

        var loaded = await repository.RequireAsync<Video>(id, CancellationToken.None);

        Assert.Null(loaded.ProviderIdentity);
        Assert.Equal(0, router.CallCount);
        Assert.Empty(EntityCardProjector.ToCard(loaded, hasSourceBackedSubtree: false).Capabilities.OfType<ProviderIdentityCapability>());
    }

    [Fact]
    public async Task FindAsyncDoesNotHydrateMissingSubtitleFiles() {
        await using var db = CreateContext();
        var id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        SeedEntity(db, id, EntityKind.Video, "Missing subtitle");
        db.EntitySubtitles.Add(new EntitySubtitleRow {
            Id = Guid.NewGuid(),
            EntityId = id,
            Language = "en",
            Label = "English",
            Format = "vtt",
            Source = EntitySubtitleSource.Embedded,
            StoragePath = "/tmp/prismedia/missing-subtitle.vtt",
            SourceFormat = "vtt",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var loaded = await repository.RequireAsync<Video>(id, CancellationToken.None);

        Assert.Empty(loaded.SubtitleCapability!.Items);
    }

    private static CapabilityTechnical MakeTechnical(
        int? width = null, int? height = null, string? codec = null) {
        var technical = new CapabilityTechnical();
        technical.Apply(width: width, height: height, codec: codec);
        return technical;
    }

    private static void Set(Entity entity, EntityCapability capability) {
        var remove = typeof(Entity).GetMethod(nameof(Entity.RemoveCapability))!
            .MakeGenericMethod(capability.GetType());
        remove.Invoke(entity, null);
        entity.AddCapability(capability);
    }

    [Fact]
    public async Task MissingOptionalAndRequiredLoadsUseDifferentPaths() {
        await using var db = CreateContext();
        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var id = Guid.Parse("99999999-9999-9999-9999-999999999999");

        Assert.Null(await repository.FindAsync<Video>(id, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.RequireAsync<Video>(id, CancellationToken.None));
    }

    private static PrismediaDbContext CreateContext(string? databaseName = null) =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .Options);

    private static void SeedEntity(
        PrismediaDbContext db,
        Guid id,
        EntityKind kind,
        string title,
        Guid? parentEntityId = null,
        int? sortOrder = null) {
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = EntityKindRegistry.ToCode(kind),
            Title = title,
            ParentEntityId = parentEntityId,
            SortOrder = sortOrder,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }

    private static EntityExternalIdRow AddExternalIdentity(
        PrismediaDbContext db,
        Guid entityId,
        string provider,
        string value,
        string? url,
        DateTimeOffset timestamp) {
        var row = new EntityExternalIdRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Provider = provider,
            Value = value,
            Url = url,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
        db.EntityExternalIds.Add(row);
        return row;
    }

    private sealed class RecordingExternalIdentityStore(
        IEntityExternalIdentityStore? inner = null,
        IReadOnlyList<EntityExternalId>? listedIdentities = null) : IEntityExternalIdentityStore {
        public List<Guid> ListedEntityIds { get; } = [];
        public List<Guid> WrittenEntityIds { get; } = [];
        public List<ExternalIdentityWriteMode> WriteModes { get; } = [];

        public async Task<IReadOnlyList<EntityExternalId>> ListAsync(
            Guid entityId,
            CancellationToken cancellationToken) {
            ListedEntityIds.Add(entityId);
            return inner is null
                ? listedIdentities ?? []
                : await inner.ListAsync(entityId, cancellationToken);
        }

        public Task<ExternalIdentityResolution> ResolveAsync(
            EntityKind kind,
            IReadOnlyCollection<ExternalIdentity> identities,
            Guid? parentEntityId,
            CancellationToken cancellationToken) =>
            inner?.ResolveAsync(kind, identities, parentEntityId, cancellationToken)
            ?? Task.FromResult(new ExternalIdentityResolution([]));

        public async Task WriteAsync(
            Guid entityId,
            IReadOnlyCollection<EntityExternalId> identities,
            ExternalIdentityWriteMode mode,
            CancellationToken cancellationToken) {
            WrittenEntityIds.Add(entityId);
            WriteModes.Add(mode);
            if (inner is not null) {
                await inner.WriteAsync(entityId, identities, mode, cancellationToken);
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class ConfiguredIdentityRouter(params PluginIdentityRoute[] routes) : IPluginIdentityRouter {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<PluginIdentityRoute>> ResolveAsync(
            string entityKindCode,
            IdentifyAction action,
            IReadOnlyList<ExternalIdentity> identities,
            CancellationToken cancellationToken) {
            CallCount++;
            var requested = identities.ToHashSet();
            return Task.FromResult<IReadOnlyList<PluginIdentityRoute>>(
                routes.Where(route => requested.Contains(route.Identity)).ToArray());
        }
    }

    private sealed class RecordingIdentityUrlResolver(string? url) : IPluginIdentityUrlResolver {
        public List<IdentityUrlResolveCall> Calls { get; } = [];

        public Task<string?> ResolveAsync(
            string entityKindCode,
            PluginIdentityRoute route,
            CancellationToken cancellationToken) {
            Calls.Add(new IdentityUrlResolveCall(entityKindCode, route));
            return Task.FromResult(url);
        }
    }

    private sealed record IdentityUrlResolveCall(
        string EntityKindCode,
        PluginIdentityRoute Route);
}
