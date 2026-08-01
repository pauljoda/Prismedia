using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Media;
using Prismedia.Contracts.Playback;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Entities.Mappers;
using Prismedia.Infrastructure.Entities.Thumbnails;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfEntityReadServiceTests {
    [Fact]
    public async Task GetAsyncProjectsRelationshipCodesAndCreditsCapability() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var personId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var studioId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var tagId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow {
                Id = videoId,
                KindCode = EntityKind.Video.ToCode(),
                Title = "Pilot",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = personId,
                KindCode = EntityKind.Person.ToCode(),
                Title = "Guest Actor",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = studioId,
                KindCode = EntityKind.Studio.ToCode(),
                Title = "HBO",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = tagId,
                KindCode = EntityKind.Tag.ToCode(),
                Title = "Mystery",
                CreatedAt = now,
                UpdatedAt = now
            });
        db.EntityRelationshipLinks.AddRange(
            new EntityRelationshipLinkRow {
                EntityId = videoId,
                RelationshipCode = "cast",
                Label = "Cast",
                TargetEntityId = personId,
                TargetKindCode = EntityKind.Person.ToCode(),
                SortOrder = 2,
                MetadataJson = """{"role":"guest","character":"Visitor"}""",
                CreatedAt = now
            },
            new EntityRelationshipLinkRow {
                EntityId = videoId,
                RelationshipCode = "studio",
                Label = "Studio",
                TargetEntityId = studioId,
                TargetKindCode = EntityKind.Studio.ToCode(),
                SortOrder = 0,
                CreatedAt = now
            },
            new EntityRelationshipLinkRow {
                EntityId = videoId,
                RelationshipCode = "tags",
                Label = "Tags",
                TargetEntityId = tagId,
                TargetKindCode = EntityKind.Tag.ToCode(),
                SortOrder = 1,
                CreatedAt = now
            });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var service = new EfEntityReadService(db, TestUserContext.Admin(), repository, ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db));

        var detail = Assert.IsType<EntityCard>(
            await service.GetAsync(videoId, hideNsfw: false, CancellationToken.None));

        var cast = Assert.Single(detail.Relationships, group => group.Code == RelationshipKind.Cast);
        Assert.Equal(EntityKind.Person, cast.Kind);
        Assert.Equal(personId, Assert.Single(cast.Entities).Id);
        Assert.Contains(detail.Relationships, group => group.Code == RelationshipKind.Studio && group.Kind == EntityKind.Studio);
        Assert.Contains(detail.Relationships, group => group.Code == RelationshipKind.Tags && group.Kind == EntityKind.Tag);
        var metadata = Assert.Single(Assert.Single(detail.Capabilities.OfType<CreditsCapability>()).Items);
        Assert.Equal(personId, metadata.PersonId);
        Assert.Equal("guest", metadata.Role);
        Assert.Equal("Visitor", metadata.Character);
    }

    [Fact]
    public async Task ListAsyncCanFilterToEntitiesThatReferenceATargetByRelationshipCode() {
        await using var db = CreateContext();
        var actorId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var matchedVideoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var otherVideoId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var relatedVideoId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow {
                Id = matchedVideoId,
                KindCode = EntityKind.Video.ToCode(),
                Title = "Linked Video",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = otherVideoId,
                KindCode = EntityKind.Video.ToCode(),
                Title = "Unlinked Video",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = relatedVideoId,
                KindCode = EntityKind.Video.ToCode(),
                Title = "Related Video",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = actorId,
                KindCode = EntityKind.Person.ToCode(),
                Title = "Guest Actor",
                CreatedAt = now,
                UpdatedAt = now
            });
        db.EntityRelationshipLinks.AddRange(
            new EntityRelationshipLinkRow {
                EntityId = matchedVideoId,
                RelationshipCode = "cast",
                Label = "Cast",
                TargetEntityId = actorId,
                TargetKindCode = EntityKind.Person.ToCode(),
                SortOrder = 0,
                CreatedAt = now
            },
            new EntityRelationshipLinkRow {
                EntityId = relatedVideoId,
                RelationshipCode = "related",
                Label = "Related",
                TargetEntityId = actorId,
                TargetKindCode = EntityKind.Person.ToCode(),
                SortOrder = 0,
                CreatedAt = now
            });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var service = new EfEntityReadService(db, TestUserContext.Admin(), repository, ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db));

        var result = await service.ListAsync(
            EntityKind.Video.ToCode(),
            query: null,
            cursor: null,
            hideNsfw: null,
            limit: null,
            CancellationToken.None,
            referencedBy: actorId,
            relationshipCode: "cast");

        var item = Assert.Single(result.Items);
        Assert.Equal(matchedVideoId, item.Id);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task ListAsyncBookBrowseIncludesBooksParentedToAuthors() {
        await using var db = CreateContext();
        var authorId = Guid.Parse("aaaaaaaa-1111-4444-8888-aaaaaaaaaaaa");
        var parentedBookId = Guid.Parse("bbbbbbbb-1111-4444-8888-bbbbbbbbbbbb");
        var wantedBookId = Guid.Parse("cccccccc-1111-4444-8888-cccccccccccc");
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow {
                Id = authorId,
                KindCode = EntityKind.BookAuthor.ToCode(),
                Title = "George R. R. Martin",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = parentedBookId,
                KindCode = EntityKind.Book.ToCode(),
                Title = "A Game of Thrones",
                ParentEntityId = authorId,
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = wantedBookId,
                KindCode = EntityKind.Book.ToCode(),
                Title = "The Anxious Generation",
                IsWanted = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.BookDetails.AddRange(
            new BookDetailRow { EntityId = parentedBookId, BookType = BookType.Book, Format = BookFormat.Epub },
            new BookDetailRow { EntityId = wantedBookId, BookType = BookType.Book, Format = BookFormat.Epub });
        await db.SaveChangesAsync();

        var result = await CreateService(db).ListAsync(EntityKind.Book.ToCode(), null, null, hideNsfw: true, null, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, item => item.Id == parentedBookId && item.ParentEntityId == authorId);
        Assert.Contains(result.Items, item => item.Id == wantedBookId && item.ParentEntityId is null);
    }

    [Fact]
    public async Task ListAsyncHidesWantedAudioTracksUnlessExplicitlyRequested() {
        await using var db = CreateContext();
        var playableTrackId = Guid.Parse("dddddddd-1111-4444-8888-dddddddddddd");
        var wantedTrackId = Guid.Parse("eeeeeeee-1111-4444-8888-eeeeeeeeeeee");
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow {
                Id = playableTrackId,
                KindCode = EntityKind.AudioTrack.ToCode(),
                Title = "Playable track",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = wantedTrackId,
                KindCode = EntityKind.AudioTrack.ToCode(),
                Title = "Wanted track",
                IsWanted = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.AudioTrackDetails.AddRange(
            new AudioTrackDetailRow { EntityId = playableTrackId },
            new AudioTrackDetailRow { EntityId = wantedTrackId });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var ordinary = await service.ListAsync(
            EntityKind.AudioTrack.ToCode(),
            null,
            null,
            hideNsfw: true,
            null,
            CancellationToken.None);
        var explicitlyWanted = await service.ListAsync(
            EntityKind.AudioTrack.ToCode(),
            null,
            null,
            hideNsfw: true,
            null,
            CancellationToken.None,
            wanted: true);

        Assert.Equal(playableTrackId, Assert.Single(ordinary.Items).Id);
        Assert.Equal(wantedTrackId, Assert.Single(explicitlyWanted.Items).Id);
    }

    [Fact]
    public async Task ListAsyncSearchReturnsDirectMovieAndStandaloneVideoEntities() {
        await using var db = CreateContext();
        var movieId = Guid.Parse("aaaaaaaa-aaaa-4444-8888-aaaaaaaaaaaa");
        var videoId = Guid.Parse("bbbbbbbb-bbbb-4444-8888-bbbbbbbbbbbb");
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow {
                Id = movieId,
                KindCode = EntityKind.Movie.ToCode(),
                Title = "Friendship",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = videoId,
                KindCode = EntityKind.Video.ToCode(),
                Title = "Friendship",
                CreatedAt = now,
                UpdatedAt = now
            });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var service = new EfEntityReadService(db, TestUserContext.Admin(), repository, ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db));

        var searchResult = await service.ListAsync(null, "Friendship", null, null, null, CancellationToken.None);
        Assert.Equal(2, searchResult.TotalCount);
        Assert.Equal([movieId, videoId], searchResult.Items.Select(item => item.Id).Order().ToArray());

        var videoBrowseResult = await service.ListAsync(EntityKind.Video.ToCode(), null, null, null, null, CancellationToken.None);
        var browsedItem = Assert.Single(videoBrowseResult.Items);
        Assert.Equal(videoId, browsedItem.Id);
        Assert.Null(browsedItem.ParentEntityId);
        Assert.Equal(1, videoBrowseResult.TotalCount);
    }

    [Fact]
    public async Task ListAsyncFiltersMultiplePlayableKindsWithoutHiddenLibraries() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var enabledRootId = Guid.Parse("11111111-aaaa-4444-8888-111111111111");
        var disabledRootId = Guid.Parse("22222222-aaaa-4444-8888-222222222222");
        var movieId = Guid.Parse("33333333-aaaa-4444-8888-333333333333");
        var videoId = Guid.Parse("55555555-aaaa-4444-8888-555555555555");
        var seriesId = Guid.Parse("66666666-aaaa-4444-8888-666666666666");
        var seasonId = Guid.Parse("77777777-aaaa-4444-8888-777777777777");
        var hiddenVideoId = Guid.Parse("88888888-aaaa-4444-8888-888888888888");
        var bookId = Guid.Parse("99999999-aaaa-4444-8888-999999999999");
        db.LibraryRoots.AddRange(
            Root(enabledRootId, enabled: true, now),
            Root(disabledRootId, enabled: false, now));
        db.Entities.AddRange(
            new EntityRow { Id = movieId, KindCode = EntityKind.Movie.ToCode(), Title = "Movie", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = videoId, KindCode = EntityKind.Video.ToCode(), Title = "Standalone Video", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = seriesId, KindCode = EntityKind.VideoSeries.ToCode(), Title = "Series", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = seasonId, KindCode = EntityKind.VideoSeason.ToCode(), Title = "Season", ParentEntityId = seriesId, CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = hiddenVideoId, KindCode = EntityKind.Video.ToCode(), Title = "Hidden", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = bookId, KindCode = EntityKind.Book.ToCode(), Title = "Book", CreatedAt = now, UpdatedAt = now });
        db.EntityLibraryRoots.AddRange(
            new EntityLibraryRootRow { EntityId = movieId, LibraryRootId = enabledRootId  },
            new EntityLibraryRootRow { EntityId = videoId, LibraryRootId = enabledRootId  },
            new EntityLibraryRootRow { EntityId = hiddenVideoId, LibraryRootId = disabledRootId  });
        db.UserEntityStates.AddRange(
            new UserEntityStateRow { UserId = TestUserContext.UserId, EntityId = movieId, ResumeSeconds = 10, UpdatedAt = now },
            new UserEntityStateRow { UserId = TestUserContext.UserId, EntityId = videoId, ResumeSeconds = 20, UpdatedAt = now },
            new UserEntityStateRow { UserId = TestUserContext.UserId, EntityId = seriesId, ResumeSeconds = 30, UpdatedAt = now },
            new UserEntityStateRow { UserId = TestUserContext.UserId, EntityId = seasonId, ResumeSeconds = 40, UpdatedAt = now },
            new UserEntityStateRow { UserId = TestUserContext.UserId, EntityId = hiddenVideoId, ResumeSeconds = 50, UpdatedAt = now },
            new UserEntityStateRow { UserId = TestUserContext.UserId, EntityId = bookId, ProgressIndex = 1, ProgressTotal = 10, UpdatedAt = now });
        await db.SaveChangesAsync();

        var result = await CreateService(db).ListAsync(
            string.Join(',', [
                EntityKind.Movie.ToCode(),
                EntityKind.Video.ToCode(),
                EntityKind.VideoSeries.ToCode(),
                EntityKind.VideoSeason.ToCode(),
            ]),
            null,
            null,
            null,
            null,
            CancellationToken.None,
            status: "in-progress");

        Assert.Equal(4, result.TotalCount);
        Assert.Equal(
            [movieId, videoId, seriesId, seasonId],
            result.Items.Select(item => item.Id).Order().ToArray());
        Assert.DoesNotContain(result.Items, item => item.Id == hiddenVideoId);
        Assert.DoesNotContain(result.Items, item => item.Id == bookId);
    }

    [Fact]
    public async Task ListAsyncRelatedGridReturnsTheDirectMovieEntity() {
        await using var db = CreateContext();
        var tagId = Guid.Parse("11111111-aaaa-4444-8888-111111111111");
        var movieId = Guid.Parse("22222222-aaaa-4444-8888-222222222222");
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow {
                Id = tagId,
                KindCode = EntityKind.Tag.ToCode(),
                Title = "Comedy",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = movieId,
                KindCode = EntityKind.Movie.ToCode(),
                Title = "Friendship",
                CreatedAt = now,
                UpdatedAt = now
            });
        db.EntityRelationshipLinks.Add(new EntityRelationshipLinkRow {
            EntityId = movieId,
            RelationshipCode = RelationshipKind.Tags.ToCode(),
            Label = "Tags",
            TargetEntityId = tagId,
            TargetKindCode = EntityKind.Tag.ToCode(),
            SortOrder = 0,
            CreatedAt = now
        });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var service = new EfEntityReadService(db, TestUserContext.Admin(), repository, ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db));

        var result = await service.ListAsync(
            kind: null,
            query: null,
            cursor: null,
            hideNsfw: null,
            limit: null,
            CancellationToken.None,
            referencedBy: tagId,
            relationshipCode: RelationshipKind.Tags.ToCode());

        var item = Assert.Single(result.Items);
        Assert.Equal(movieId, item.Id);
        Assert.Equal(EntityKind.Movie, item.Kind);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task ListAsyncProjectsVideoTrickplayPlaylistAsSpriteHover() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = videoId,
            KindCode = EntityKind.Video.ToCode(),
            Title = "Hoverable Video",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.EntityFiles.AddRange(
            new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = videoId,
                Role = EntityFileRole.Thumbnail,
                Path = "/assets/videos/444/thumb.jpg",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = videoId,
                Role = EntityFileRole.Trickplay,
                Path = VideoPlaybackProtocol.TrickplayPlaylistPath(videoId, 320),
                MimeType = "application/vnd.apple.mpegurl",
                CreatedAt = now,
                UpdatedAt = now
            });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var service = new EfEntityReadService(db, TestUserContext.Admin(), repository, ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db));

        var result = await service.ListAsync(EntityKind.Video.ToCode(), null, null, null, null, CancellationToken.None);
        var item = Assert.Single(result.Items);

        Assert.Equal(ThumbnailHoverKind.Sprite, item.HoverKind);
        Assert.Equal(VideoPlaybackProtocol.TrickplayPlaylistPath(videoId, 320), item.HoverUrl);
    }

    [Fact]
    public async Task ListAsyncPrefersCustomArtworkOverGeneratedThumbnails() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("15151515-1515-1515-1515-151515151515");
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = videoId,
            KindCode = EntityKind.Video.ToCode(),
            Title = "Custom Poster Video",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.EntityFiles.AddRange(
            new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = videoId,
                Role = EntityFileRole.Thumbnail,
                Path = "/assets/videos/15151515/thumb.jpg",
                Source = "scan",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = videoId,
                Role = EntityFileRole.Poster,
                Path = "/assets/custom/artwork/15151515/poster.webp",
                Source = "custom",
                CreatedAt = now.AddSeconds(1),
                UpdatedAt = now.AddSeconds(1)
            });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var service = new EfEntityReadService(db, TestUserContext.Admin(), repository, ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db));

        var result = await service.ListAsync(EntityKind.Video.ToCode(), null, null, null, null, CancellationToken.None);
        var item = Assert.Single(result.Items);

        Assert.Equal("/assets/custom/artwork/15151515/poster.webp", item.CoverUrl);
    }

    [Fact]
    public async Task ListAsyncSkipsMissingLocalArtworkAndUsesGeneratedThumbnail() {
        var cacheRoot = CreateCacheRoot();
        try {
            await using var db = CreateContext();
            var videoId = Guid.Parse("15151515-aaaa-1515-1515-151515151515");
            var thumbPath = $"/assets/videos/{videoId}/thumb.jpg";
            var posterPath = $"/assets/custom/artwork/{videoId}/poster.webp";
            var now = DateTimeOffset.UtcNow;
            db.Entities.Add(new EntityRow {
                Id = videoId,
                KindCode = EntityKind.Video.ToCode(),
                Title = "Stale Poster Video",
                CreatedAt = now,
                UpdatedAt = now
            });
            db.EntityFiles.AddRange(
                File(videoId, EntityFileRole.Thumbnail, thumbPath, now),
                File(videoId, EntityFileRole.Poster, posterPath, now.AddSeconds(1)));
            await db.SaveChangesAsync();
            WriteCacheFile(cacheRoot, thumbPath);

            var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
            var service = new EfEntityReadService(db, TestUserContext.Admin(), repository, ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db), Assets(cacheRoot));

            var result = await service.ListAsync(EntityKind.Video.ToCode(), null, null, null, null, CancellationToken.None);
            var item = Assert.Single(result.Items);

            Assert.Equal(thumbPath, item.CoverUrl);
        } finally {
            DeleteDirectory(cacheRoot);
        }
    }

    [Fact]
    public async Task GetAsyncSkipsMissingLocalArtworkAndUsesGeneratedThumbnail() {
        var cacheRoot = CreateCacheRoot();
        try {
            await using var db = CreateContext();
            var videoId = Guid.Parse("15151515-bbbb-1515-1515-151515151515");
            var thumbPath = $"/assets/videos/{videoId}/thumb.jpg";
            var posterPath = $"/assets/custom/artwork/{videoId}/poster.webp";
            var now = DateTimeOffset.UtcNow;
            db.Entities.Add(new EntityRow {
                Id = videoId,
                KindCode = EntityKind.Video.ToCode(),
                Title = "Stale Detail Poster Video",
                CreatedAt = now,
                UpdatedAt = now
            });
            db.EntityFiles.AddRange(
                File(videoId, EntityFileRole.Thumbnail, thumbPath, now),
                File(videoId, EntityFileRole.Poster, posterPath, now.AddSeconds(1)));
            await db.SaveChangesAsync();
            WriteCacheFile(cacheRoot, thumbPath);

            var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
            var service = new EfEntityReadService(db, TestUserContext.Admin(), repository, ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db), Assets(cacheRoot));

            var item = await service.GetAsync(videoId, hideNsfw: false, CancellationToken.None);
            var images = Assert.IsType<ImagesCapability>(Assert.Single(item!.Capabilities.OfType<ImagesCapability>()));

            Assert.Equal(thumbPath, images.CoverUrl);
            Assert.Equal(thumbPath, images.ThumbnailUrl);
            Assert.DoesNotContain(images.Items, image => image.Path == posterPath);
        } finally {
            DeleteDirectory(cacheRoot);
        }
    }

    [Fact]
    public async Task GetThumbnailsAsyncFallsBackToCoverWhenGridVariantsAreMissing() {
        await using var db = CreateContext();
        var movieId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000041");
        var poster = "/assets/plugins/artwork/movie/poster.jpg";
        var now = DateTimeOffset.UtcNow;

        db.Entities.Add(new EntityRow {
            Id = movieId,
            KindCode = EntityKind.Movie.ToCode(),
            Title = "Poster Only Movie",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.EntityFiles.Add(File(movieId, EntityFileRole.Poster, poster, now));
        await db.SaveChangesAsync();

        var response = await CreateService(db).GetThumbnailsAsync([movieId], hideNsfw: false, CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal(poster, item.CoverUrl);
        Assert.Equal(poster, item.CoverThumbUrl);
        Assert.Equal(poster, item.CoverThumb2xUrl);
    }

    [Fact]
    public async Task GetThumbnailsAsyncIgnoresMissingGridVariantFilesAndFallsBackToCover() {
        var cacheRoot = CreateCacheRoot();
        try {
            await using var db = CreateContext();
            var movieId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000042");
            var poster = "/assets/plugins/artwork/movie/poster.jpg";
            var grid = $"/assets/grid-thumbs/{movieId}.jpg";
            var grid2x = $"/assets/grid-thumbs/{movieId}@2x.jpg";
            var now = DateTimeOffset.UtcNow;

            db.Entities.Add(new EntityRow {
                Id = movieId,
                KindCode = EntityKind.Movie.ToCode(),
                Title = "Stale Grid Movie",
                CreatedAt = now,
                UpdatedAt = now
            });
            db.EntityFiles.AddRange(
                File(movieId, EntityFileRole.Poster, poster, now),
                File(movieId, EntityFileRole.GridThumbnail, grid, now.AddSeconds(1)),
                File(movieId, EntityFileRole.GridThumbnail2x, grid2x, now.AddSeconds(2)));
            await db.SaveChangesAsync();
            WriteCacheFile(cacheRoot, poster);

            var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
            var service = new EfEntityReadService(db, TestUserContext.Admin(), repository, ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db), Assets(cacheRoot));

            var response = await service.GetThumbnailsAsync([movieId], hideNsfw: false, CancellationToken.None);

            var item = Assert.Single(response.Items);
            Assert.Equal(poster, item.CoverUrl);
            Assert.Equal(poster, item.CoverThumbUrl);
            Assert.Equal(poster, item.CoverThumb2xUrl);
        } finally {
            DeleteDirectory(cacheRoot);
        }
    }

    [Fact]
    public async Task GetThumbnailsAsyncUsesEpisodeRepresentativeWhenSeriesPosterIsMissing() {
        var cacheRoot = CreateCacheRoot();
        try {
            await using var db = CreateContext();
            var seriesId = Guid.Parse("15151515-cccc-1515-1515-151515151515");
            var seasonId = Guid.Parse("15151515-dddd-1515-1515-151515151515");
            var episodeId = Guid.Parse("15151515-eeee-1515-1515-151515151515");
            var episodeThumb = $"/assets/videos/{episodeId}/thumb.jpg";
            var seriesPoster = $"/assets/plugins/artwork/{seriesId}/poster.jpg";
            var now = DateTimeOffset.UtcNow;
            db.Entities.AddRange(
                new EntityRow {
                    Id = seriesId,
                    KindCode = EntityKind.VideoSeries.ToCode(),
                    Title = "Series",
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new EntityRow {
                    Id = seasonId,
                    KindCode = EntityKind.VideoSeason.ToCode(),
                    Title = "Season",
                    ParentEntityId = seriesId,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new EntityRow {
                    Id = episodeId,
                    KindCode = EntityKind.VideoEpisode.ToCode(),
                    Title = "Episode",
                    ParentEntityId = seasonId,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            db.EntityFiles.AddRange(
                File(seriesId, EntityFileRole.Poster, seriesPoster, now),
                File(episodeId, EntityFileRole.Thumbnail, episodeThumb, now));
            await db.SaveChangesAsync();
            WriteCacheFile(cacheRoot, episodeThumb);

            var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
            var service = new EfEntityReadService(db, TestUserContext.Admin(), repository, ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db), Assets(cacheRoot));

            var result = await service.GetThumbnailsAsync([seriesId], hideNsfw: false, CancellationToken.None);
            var item = Assert.Single(result.Items);

            Assert.Equal(episodeThumb, item.CoverUrl);
        } finally {
            DeleteDirectory(cacheRoot);
        }
    }

    [Fact]
    public async Task ListAsyncPrefersLogoOverBackdropForThumbnailCover() {
        await using var db = CreateContext();
        var studioId = Guid.Parse("16161616-1616-1616-1616-161616161616");
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = studioId,
            KindCode = EntityKind.Studio.ToCode(),
            Title = "GameChops",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.EntityFiles.AddRange(
            new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = studioId,
                Role = EntityFileRole.Backdrop,
                Path = "/assets/plugins/artwork/gamechops/banner.webp",
                Source = "custom",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = studioId,
                Role = EntityFileRole.Logo,
                Path = "/assets/plugins/artwork/gamechops/logo.webp",
                Source = "custom",
                CreatedAt = now.AddSeconds(1),
                UpdatedAt = now.AddSeconds(1)
            });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var service = new EfEntityReadService(db, TestUserContext.Admin(), repository, ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db));

        var result = await service.ListAsync(EntityKind.Studio.ToCode(), null, null, null, null, CancellationToken.None);
        var item = Assert.Single(result.Items);

        Assert.Equal("/assets/plugins/artwork/gamechops/logo.webp", item.CoverUrl);
    }

    [Fact]
    public async Task ListAsyncProjectsVideoTechnicalMetadataAsThumbnailMeta() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = videoId,
            KindCode = EntityKind.Video.ToCode(),
            Title = "Probed Video",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.EntityTechnical.Add(new EntityTechnicalRow {
            EntityId = videoId,
            DurationSeconds = 596,
            Width = 3840,
            Height = 1920,
            BitRate = 8_000_000,
            Codec = "h264",
            Container = "matroska",
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var service = new EfEntityReadService(db, TestUserContext.Admin(), repository, ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db));

        var result = await service.ListAsync(EntityKind.Video.ToCode(), null, null, null, null, CancellationToken.None);
        var item = Assert.Single(result.Items);

        Assert.Equal(
            [
                new EntityThumbnailMeta("duration", "09:56"),
                new EntityThumbnailMeta("video", "4K"),
                new EntityThumbnailMeta("video", "H264"),
                new EntityThumbnailMeta("video", "MATROSKA")
            ],
            item.Meta);
    }

    [Fact]
    public async Task ListAsyncReturnsOnlyTopLevelGalleriesForGalleryBrowse() {
        await using var db = CreateContext();
        var galleryId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var subgalleryId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow {
                Id = galleryId,
                KindCode = EntityKind.Gallery.ToCode(),
                Title = "Gallery",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = subgalleryId,
                KindCode = EntityKind.Gallery.ToCode(),
                Title = "A secondGallery",
                ParentEntityId = galleryId,
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now
            });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var service = new EfEntityReadService(db, TestUserContext.Admin(), repository, ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db));

        var result = await service.ListAsync(EntityKind.Gallery.ToCode(), null, null, null, null, CancellationToken.None);
        var item = Assert.Single(result.Items);

        Assert.Equal(galleryId, item.Id);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task ListAsyncReturnsAlbumsIncludingThoseGroupedUnderAnArtist() {
        // Albums now nest under an artist grouping rather than under another album, so the audio
        // browse must surface every album — both loose albums and albums that have an artist.
        await using var db = CreateContext();
        var artistId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var looseAlbumId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var artistAlbumId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow {
                Id = artistId,
                KindCode = EntityKind.MusicArtist.ToCode(),
                Title = "Imagine Dragons",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = looseAlbumId,
                KindCode = EntityKind.AudioLibrary.ToCode(),
                Title = "Loose Album",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = artistAlbumId,
                KindCode = EntityKind.AudioLibrary.ToCode(),
                Title = "Evolve",
                ParentEntityId = artistId,
                SortOrder = 1,
                CreatedAt = now,
                UpdatedAt = now
            });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var service = new EfEntityReadService(db, TestUserContext.Admin(), repository, ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db));

        var result = await service.ListAsync(EntityKind.AudioLibrary.ToCode(), null, null, null, null, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, item => item.Id == looseAlbumId);
        Assert.Contains(result.Items, item => item.Id == artistAlbumId);
    }

    [Fact]
    public async Task ListAsyncKeepsAudiobookTracksInsideTheirBookInsteadOfMusicLists() {
        await using var db = CreateContext();
        var bookId = Guid.Parse("11111111-aaaa-4444-8888-111111111111");
        var audiobookTrackId = Guid.Parse("22222222-aaaa-4444-8888-222222222222");
        var albumId = Guid.Parse("33333333-aaaa-4444-8888-333333333333");
        var musicTrackId = Guid.Parse("44444444-aaaa-4444-8888-444444444444");
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow {
                Id = bookId,
                KindCode = EntityKind.Book.ToCode(),
                Title = "Spoken Story",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = audiobookTrackId,
                KindCode = EntityKind.AudioTrack.ToCode(),
                Title = "Book Chapter",
                ParentEntityId = bookId,
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = albumId,
                KindCode = EntityKind.AudioLibrary.ToCode(),
                Title = "Music Album",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = musicTrackId,
                KindCode = EntityKind.AudioTrack.ToCode(),
                Title = "Music Chapter",
                ParentEntityId = albumId,
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.BookDetails.Add(new BookDetailRow { EntityId = bookId, BookType = BookType.Book });
        db.EntityLibraryRoots.Add(new EntityLibraryRootRow { EntityId = albumId });
        db.AudioTrackDetails.AddRange(
            new AudioTrackDetailRow { EntityId = audiobookTrackId },
            new AudioTrackDetailRow { EntityId = musicTrackId });
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var tracks = await service.ListAsync(
            EntityKind.AudioTrack.ToCode(),
            query: null,
            cursor: null,
            hideNsfw: false,
            limit: null,
            CancellationToken.None);
        var search = await service.ListAsync(
            kind: null,
            query: "Chapter",
            cursor: null,
            hideNsfw: false,
            limit: null,
            CancellationToken.None);
        var book = Assert.IsType<EntityCard>(await service.GetAsync(
            bookId, hideNsfw: false,
            CancellationToken.None));

        Assert.Equal(musicTrackId, Assert.Single(tracks.Items).Id);
        Assert.Equal(1, tracks.TotalCount);
        Assert.Equal(musicTrackId, Assert.Single(search.Items).Id);
        Assert.Equal(1, search.TotalCount);
        var audiobookTracks = Assert.Single(book.ChildrenByKind, group => group.Kind == EntityKind.AudioTrack);
        Assert.Equal(audiobookTrackId, Assert.Single(audiobookTracks.Entities).Id);
    }

    [Fact]
    public async Task ListAsyncProjectsRepresentativeChildHoverImages() {
        await using var db = CreateContext();
        var bookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = bookId,
            KindCode = EntityKind.Book.ToCode(),
            Title = "Collected Manga",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = bookId,
            Role = EntityFileRole.Cover,
            Path = "/assets/books/custom-cover.jpg",
            CreatedAt = now,
            UpdatedAt = now
        });

        for (var index = 0; index < 6; index++) {
            var volumeId = Guid.Parse($"22222222-2222-2222-2222-22222222222{index}");
            var chapterId = Guid.Parse($"33333333-3333-3333-3333-33333333333{index}");
            var pageId = Guid.Parse($"44444444-4444-4444-4444-44444444444{index}");
            db.Entities.AddRange(
                new EntityRow {
                    Id = volumeId,
                    KindCode = EntityKind.BookVolume.ToCode(),
                    Title = $"Volume {index + 1:00}",
                    ParentEntityId = bookId,
                    SortOrder = index,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new EntityRow {
                    Id = chapterId,
                    KindCode = EntityKind.BookChapter.ToCode(),
                    Title = $"Chapter {index + 1:00}",
                    ParentEntityId = volumeId,
                    SortOrder = 0,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new EntityRow {
                    Id = pageId,
                    KindCode = EntityKind.BookPage.ToCode(),
                    Title = $"Page {index + 1:00}",
                    ParentEntityId = chapterId,
                    SortOrder = 0,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            db.EntityFiles.Add(new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = pageId,
                Role = EntityFileRole.Thumbnail,
                Path = $"/assets/book-pages/page-{index + 1}.jpg",
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var service = new EfEntityReadService(db, TestUserContext.Admin(), repository, ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db));

        var result = await service.ListAsync(EntityKind.Book.ToCode(), null, null, null, null, CancellationToken.None);
        var item = Assert.Single(result.Items);

        Assert.Equal("/assets/books/custom-cover.jpg", item.CoverUrl);
        Assert.Equal(5, item.HoverImages.Count);
        Assert.Equal("/assets/book-pages/page-1.jpg", item.HoverImages[0].Path);
        Assert.Equal("/assets/book-pages/page-6.jpg", item.HoverImages[^1].Path);
    }

    [Fact]
    public async Task ListAsyncUsesFirstRepresentativeHoverImageAsFallbackCover() {
        await using var db = CreateContext();
        var bookId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
        var chapterId = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");
        var pageId = Guid.Parse("cccccccc-3333-3333-3333-333333333333");
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow {
                Id = bookId,
                KindCode = EntityKind.Book.ToCode(),
                Title = "No Custom Cover",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = chapterId,
                KindCode = EntityKind.BookChapter.ToCode(),
                Title = "Chapter 1",
                ParentEntityId = bookId,
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = pageId,
                KindCode = EntityKind.BookPage.ToCode(),
                Title = "Page 1",
                ParentEntityId = chapterId,
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = pageId,
            Role = EntityFileRole.Thumbnail,
            Path = "/assets/book-pages/page-1.jpg",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var service = new EfEntityReadService(db, TestUserContext.Admin(), repository, ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db));

        var result = await service.ListAsync(EntityKind.Book.ToCode(), null, null, null, null, CancellationToken.None);
        var item = Assert.Single(result.Items);

        Assert.Equal("/assets/book-pages/page-1.jpg", item.CoverUrl);
        Assert.Equal("/assets/book-pages/page-1.jpg", Assert.Single(item.HoverImages).Path);
    }

    [Fact]
    public async Task ListAsyncUsesCollectionMemberArtworkAsFallbackCover() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var collectionId = Guid.Parse("11111111-aaaa-4444-8888-111111111111");
        var albumId = Guid.Parse("22222222-bbbb-4444-8888-222222222222");
        var trackId = Guid.Parse("33333333-cccc-4444-8888-333333333333");
        var secondTrackId = Guid.Parse("33333333-cccc-4444-8888-333333333334");
        var disabledRootId = Guid.Parse("44444444-dddd-4444-8888-444444444444");
        var hiddenVideoId = Guid.Parse("55555555-eeee-4444-8888-555555555555");

        db.LibraryRoots.Add(Root(disabledRootId, enabled: false, now));
        db.Entities.AddRange(
            new EntityRow {
                Id = collectionId,
                KindCode = EntityKind.Collection.ToCode(),
                Title = "Road Trip",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = albumId,
                KindCode = EntityKind.AudioLibrary.ToCode(),
                Title = "Album",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = trackId,
                KindCode = EntityKind.AudioTrack.ToCode(),
                Title = "Opening Track",
                ParentEntityId = albumId,
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = secondTrackId,
                KindCode = EntityKind.AudioTrack.ToCode(),
                Title = "Closing Track",
                ParentEntityId = albumId,
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = hiddenVideoId,
                KindCode = EntityKind.Video.ToCode(),
                Title = "Hidden Video",
                CreatedAt = now,
                UpdatedAt = now
            });
        db.CollectionDetails.Add(new CollectionDetailRow {
            EntityId = collectionId,
            OwnerUserId = TestUserContext.UserId,
            CoverMode = CollectionCoverMode.Mosaic
        });
        db.EntityLibraryRoots.Add(new EntityLibraryRootRow { EntityId = albumId });
        db.AudioTrackDetails.AddRange(
            new AudioTrackDetailRow { EntityId = trackId },
            new AudioTrackDetailRow { EntityId = secondTrackId });
        db.EntityLibraryRoots.Add(new EntityLibraryRootRow { EntityId = hiddenVideoId, LibraryRootId = disabledRootId  });
        db.CollectionItemDetails.AddRange(
            new CollectionItemDetailRow {
                Id = Guid.NewGuid(),
                CollectionEntityId = collectionId,
                ItemEntityId = hiddenVideoId,
                Source = CollectionItemSource.Manual,
                SortOrder = -1,
                AddedAt = now
            },
            new CollectionItemDetailRow {
                Id = Guid.NewGuid(),
                CollectionEntityId = collectionId,
                ItemEntityId = trackId,
                Source = CollectionItemSource.Manual,
                SortOrder = 0,
                AddedAt = now
            },
            new CollectionItemDetailRow {
                Id = Guid.NewGuid(),
                CollectionEntityId = collectionId,
                ItemEntityId = secondTrackId,
                Source = CollectionItemSource.Manual,
                SortOrder = 1,
                AddedAt = now
            });
        db.EntityFiles.AddRange(
            File(hiddenVideoId, EntityFileRole.Poster, "/assets/videos/hidden/poster.jpg", now),
            File(albumId, EntityFileRole.Cover, "/assets/audio-libraries/album/cover.jpg", now));
        await db.SaveChangesAsync();

        var result = await CreateService(db).ListAsync(
            EntityKind.Collection.ToCode(),
            query: null,
            cursor: null,
            hideNsfw: null,
            limit: null,
            CancellationToken.None);

        var collection = Assert.Single(result.Items);
        Assert.Equal(collectionId, collection.Id);
        Assert.Equal("/assets/audio-libraries/album/cover.jpg", collection.CoverUrl);
        var hover = Assert.Single(collection.HoverImages);
        Assert.Equal(trackId, hover.EntityId);
        Assert.Equal("/assets/audio-libraries/album/cover.jpg", hover.Path);
    }

    [Fact]
    public async Task CollectionArtworkFallsBackWhenConfiguredCoverIsHidden() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var collectionId = Guid.NewGuid();
        var configuredCoverId = Guid.NewGuid();
        var visibleMemberId = Guid.NewGuid();
        db.Entities.AddRange(
            new EntityRow {
                Id = collectionId,
                KindCode = EntityKind.Collection.ToCode(),
                Title = "Collection",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = configuredCoverId,
                KindCode = EntityKind.Video.ToCode(),
                Title = "Hidden configured cover",
                IsNsfw = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = visibleMemberId,
                KindCode = EntityKind.AudioLibrary.ToCode(),
                Title = "Visible fallback",
                CreatedAt = now,
                UpdatedAt = now
            });
        db.CollectionDetails.Add(new CollectionDetailRow {
            EntityId = collectionId,
            OwnerUserId = TestUserContext.UserId,
            CoverMode = CollectionCoverMode.Item,
            CoverItemEntityId = configuredCoverId
        });
        db.CollectionItemDetails.Add(new CollectionItemDetailRow {
            Id = Guid.NewGuid(),
            CollectionEntityId = collectionId,
            ItemEntityId = visibleMemberId,
            Source = CollectionItemSource.Manual,
            SortOrder = 0,
            AddedAt = now
        });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = visibleMemberId,
            Role = EntityFileRole.Cover,
            Path = "/assets/audio-libraries/fallback/cover.jpg",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var result = await CreateService(db).ListAsync(
            EntityKind.Collection.ToCode(),
            null,
            null,
            hideNsfw: true,
            null,
            CancellationToken.None);

        Assert.Equal("/assets/audio-libraries/fallback/cover.jpg", Assert.Single(result.Items).CoverUrl);
    }

    [Fact]
    public async Task ListAsyncShowsOnlyTopLevelBooksWhenBooksHaveChildBooks() {
        await using var db = CreateContext();
        var seriesId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
        var childBookId = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");
        var looseBookId = Guid.Parse("cccccccc-3333-3333-3333-333333333333");
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow {
                Id = seriesId,
                KindCode = EntityKind.Book.ToCode(),
                Title = "Game of Thrones",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = childBookId,
                KindCode = EntityKind.Book.ToCode(),
                Title = "A Game of Thrones",
                ParentEntityId = seriesId,
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = looseBookId,
                KindCode = EntityKind.Book.ToCode(),
                Title = "Standalone",
                CreatedAt = now,
                UpdatedAt = now
            });
        db.BookDetails.AddRange(
            new BookDetailRow { EntityId = seriesId },
            new BookDetailRow { EntityId = childBookId, Format = BookFormat.Pdf },
            new BookDetailRow { EntityId = looseBookId, Format = BookFormat.Epub });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var service = new EfEntityReadService(db, TestUserContext.Admin(), repository, ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db));

        var result = await service.ListAsync(EntityKind.Book.ToCode(), null, null, null, null, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal([seriesId, looseBookId], result.Items.Select(item => item.Id).Order().ToArray());
    }

    [Fact]
    public async Task DiscoveryKeepsNestedBooksAndGalleriesWhileTheirTypedBrowseHidesThem() {
        await using var db = CreateContext();
        var bookId = Guid.NewGuid();
        var nestedBookId = Guid.NewGuid();
        var galleryId = Guid.NewGuid();
        var nestedGalleryId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            Row(bookId, EntityKind.Book, "Book"),
            Row(nestedBookId, EntityKind.Book, "Nested book", bookId),
            Row(galleryId, EntityKind.Gallery, "Gallery"),
            Row(nestedGalleryId, EntityKind.Gallery, "Nested gallery", galleryId));
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var discovery = await service.ListAsync(null, "Nested", null, null, null, CancellationToken.None);
        var books = await service.ListAsync(EntityKind.Book.ToCode(), null, null, null, null, CancellationToken.None);
        var galleries = await service.ListAsync(EntityKind.Gallery.ToCode(), null, null, null, null, CancellationToken.None);

        Assert.Equal(
            new[] { nestedBookId, nestedGalleryId }.Order(),
            discovery.Items.Select(item => item.Id).Order());
        Assert.DoesNotContain(books.Items, item => item.Id == nestedBookId);
        Assert.DoesNotContain(galleries.Items, item => item.Id == nestedGalleryId);

        EntityRow Row(Guid id, EntityKind kind, string title, Guid? parentId = null) =>
            new() {
                Id = id,
                KindCode = kind.ToCode(),
                Title = title,
                ParentEntityId = parentId,
                CreatedAt = now,
                UpdatedAt = now
            };
    }

    [Fact]
    public async Task GetAsyncProjectsOnlyDirectBookChildren() {
        await using var db = CreateContext();
        var bookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var volumeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var chapterId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var pageId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow {
                Id = bookId,
                KindCode = EntityKind.Book.ToCode(),
                Title = "The Promised Neverland",
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
                Title = "Chapter 1",
                ParentEntityId = volumeId,
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = pageId,
                KindCode = EntityKind.BookPage.ToCode(),
                Title = "001",
                ParentEntityId = chapterId,
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.BookDetails.Add(new BookDetailRow { EntityId = bookId });
        db.BookChapterDetails.Add(new BookChapterDetailRow { EntityId = chapterId });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var service = new EfEntityReadService(db, TestUserContext.Admin(), repository, ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db));

        var detail = Assert.IsAssignableFrom<IEntityCard>(
            await service.GetAsync(bookId, hideNsfw: false, CancellationToken.None));

        var volumes = Assert.Single(detail.ChildrenByKind);
        Assert.Equal(EntityKind.BookVolume, volumes.Kind);
        Assert.Equal(volumeId, Assert.Single(volumes.Entities).Id);
        Assert.DoesNotContain(detail.ChildrenByKind, group => group.Kind == EntityKind.BookChapter);
        Assert.DoesNotContain(detail.ChildrenByKind, group => group.Kind == EntityKind.BookPage);
    }

    [Fact]
    public async Task GetAsyncProjectsOnlyDirectVolumeChildren() {
        await using var db = CreateContext();
        var bookId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var volumeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var chapterId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var pageId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow {
                Id = bookId,
                KindCode = EntityKind.Book.ToCode(),
                Title = "Book",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = volumeId,
                KindCode = EntityKind.BookVolume.ToCode(),
                Title = "Volume 01",
                ParentEntityId = bookId,
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = chapterId,
                KindCode = EntityKind.BookChapter.ToCode(),
                Title = "Chapter 1",
                ParentEntityId = volumeId,
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = pageId,
                KindCode = EntityKind.BookPage.ToCode(),
                Title = "001",
                ParentEntityId = chapterId,
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.BookChapterDetails.Add(new BookChapterDetailRow { EntityId = chapterId });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var service = new EfEntityReadService(db, TestUserContext.Admin(), repository, ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db));

        var card = Assert.IsType<EntityCard>(await service.GetAsync(volumeId, hideNsfw: false, CancellationToken.None));

        var chapters = Assert.Single(card.ChildrenByKind);
        Assert.Equal(EntityKind.BookChapter, chapters.Kind);
        Assert.Equal(chapterId, Assert.Single(chapters.Entities).Id);
        Assert.DoesNotContain(card.ChildrenByKind, group => group.Kind == EntityKind.BookPage);
    }

    [Fact]
    public async Task GetAsyncHidesNsfwEntityAndFiltersHiddenChildrenWhenRequested() {
        await using var db = CreateContext();
        var bookId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var safeChapterId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var nsfwChapterId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow {
                Id = bookId,
                KindCode = EntityKind.Book.ToCode(),
                Title = "Shared Book",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = safeChapterId,
                KindCode = EntityKind.BookChapter.ToCode(),
                Title = "Safe Chapter",
                ParentEntityId = bookId,
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = nsfwChapterId,
                KindCode = EntityKind.BookChapter.ToCode(),
                Title = "Hidden Chapter",
                ParentEntityId = bookId,
                SortOrder = 1,
                IsNsfw = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var service = new EfEntityReadService(db, TestUserContext.Admin(), repository, ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db));

        var hidden = await service.GetAsync(nsfwChapterId, hideNsfw: true, CancellationToken.None);
        var parent = Assert.IsType<EntityCard>(await service.GetAsync(bookId, hideNsfw: true, CancellationToken.None));

        Assert.Null(hidden);
        var children = Assert.Single(parent.ChildrenByKind);
        Assert.Equal(safeChapterId, Assert.Single(children.Entities).Id);
    }

    [Fact]
    public async Task GetAsyncOmitsBookProgressWhenCurrentChapterIsHidden() {
        await using var db = CreateContext();
        var bookId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var chapterId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow {
                Id = bookId,
                KindCode = EntityKind.Book.ToCode(),
                Title = "Visible Book",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = chapterId,
                KindCode = EntityKind.BookChapter.ToCode(),
                Title = "Hidden Chapter",
                ParentEntityId = bookId,
                SortOrder = 0,
                IsNsfw = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.UserEntityStates.Add(new UserEntityStateRow {
            UserId = TestUserContext.UserId,
            EntityId = bookId,
            ProgressCurrentEntityId = chapterId,
            ProgressUnit = "page",
            ProgressIndex = 5,
            ProgressTotal = 30,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var service = new EfEntityReadService(db, TestUserContext.Admin(), repository, ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db));

        var detail = Assert.IsAssignableFrom<IEntityCard>(
            await service.GetAsync(bookId, hideNsfw: true, CancellationToken.None));

        Assert.DoesNotContain(detail.Capabilities, capability => capability is ProgressCapability);
        Assert.Empty(detail.ChildrenByKind);
    }

    [Fact]
    public async Task GetAsyncFiltersHiddenRelationshipsWhenRequested() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var safePersonId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var nsfwPersonId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow {
                Id = videoId,
                KindCode = EntityKind.Video.ToCode(),
                Title = "Feature",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = safePersonId,
                KindCode = EntityKind.Person.ToCode(),
                Title = "Safe Person",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = nsfwPersonId,
                KindCode = EntityKind.Person.ToCode(),
                Title = "Hidden Person",
                IsNsfw = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.EntityRelationshipLinks.AddRange(
            new EntityRelationshipLinkRow {
                EntityId = videoId,
                RelationshipCode = "cast",
                Label = "Cast",
                TargetEntityId = safePersonId,
                TargetKindCode = EntityKind.Person.ToCode(),
                SortOrder = 0,
                CreatedAt = now
            },
            new EntityRelationshipLinkRow {
                EntityId = videoId,
                RelationshipCode = "cast",
                Label = "Cast",
                TargetEntityId = nsfwPersonId,
                TargetKindCode = EntityKind.Person.ToCode(),
                SortOrder = 1,
                CreatedAt = now
            });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var service = new EfEntityReadService(db, TestUserContext.Admin(), repository, ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db));

        var detail = Assert.IsType<EntityCard>(
            await service.GetAsync(videoId, hideNsfw: true, CancellationToken.None));

        var cast = Assert.Single(detail.Relationships, group => group.Code == RelationshipKind.Cast);
        Assert.Equal(safePersonId, Assert.Single(cast.Entities).Id);
        Assert.DoesNotContain(
            Assert.Single(detail.Capabilities.OfType<CreditsCapability>()).Items,
            credit => credit.PersonId == nsfwPersonId);
    }

    [Fact]
    public async Task DisabledLibraryRootHidesDirectRootMediaFromListsDetailsAndThumbnails() {
        await using var db = CreateContext();
        var enabledRootId = Guid.Parse("11111111-0000-0000-0000-000000000001");
        var disabledRootId = Guid.Parse("11111111-0000-0000-0000-000000000002");
        var visibleVideoId = Guid.Parse("11111111-0000-0000-0000-000000000003");
        var hiddenVideoId = Guid.Parse("11111111-0000-0000-0000-000000000004");
        var rootlessTagId = Guid.Parse("11111111-0000-0000-0000-000000000005");
        var now = DateTimeOffset.UtcNow;
        db.LibraryRoots.AddRange(
            Root(enabledRootId, enabled: true, now),
            Root(disabledRootId, enabled: false, now));
        db.Entities.AddRange(
            new EntityRow { Id = visibleVideoId, KindCode = EntityKind.Video.ToCode(), Title = "Shared Title", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = hiddenVideoId, KindCode = EntityKind.Video.ToCode(), Title = "Shared Title Hidden", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = rootlessTagId, KindCode = EntityKind.Tag.ToCode(), Title = "Rootless Tag", CreatedAt = now, UpdatedAt = now });
        db.EntityLibraryRoots.AddRange(
            new EntityLibraryRootRow { EntityId = visibleVideoId, LibraryRootId = enabledRootId  },
            new EntityLibraryRootRow { EntityId = hiddenVideoId, LibraryRootId = disabledRootId  });
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var videos = await service.ListAsync(EntityKind.Video.ToCode(), null, null, null, null, CancellationToken.None);
        var search = await service.ListAsync(null, "Shared Title", null, null, null, CancellationToken.None);
        var hiddenCard = await service.GetAsync(hiddenVideoId, hideNsfw: false, CancellationToken.None);
        var hiddenDetail = await service.GetAsync(hiddenVideoId, hideNsfw: false, CancellationToken.None);
        var thumbnails = await service.GetThumbnailsAsync([visibleVideoId, hiddenVideoId, rootlessTagId], hideNsfw: false, CancellationToken.None);

        Assert.Equal(1, videos.TotalCount);
        Assert.Equal(visibleVideoId, Assert.Single(videos.Items).Id);
        Assert.Equal(1, search.TotalCount);
        Assert.Equal(visibleVideoId, Assert.Single(search.Items).Id);
        Assert.Null(hiddenCard);
        Assert.Null(hiddenDetail);
        Assert.Equal([visibleVideoId, rootlessTagId], thumbnails.Items.Select(item => item.Id).ToArray());

        db.LibraryRoots.Single(root => root.Id == disabledRootId).Enabled = true;
        await db.SaveChangesAsync();

        // Library visibility is memoized per request scope, so a fresh service models
        // the next request after the root was re-enabled.
        var freshService = CreateService(db);
        var reenabled = await freshService.ListAsync(EntityKind.Video.ToCode(), null, null, null, null, CancellationToken.None);

        Assert.Equal(2, reenabled.TotalCount);
        Assert.Equal([visibleVideoId, hiddenVideoId], reenabled.Items.Select(item => item.Id).Order().ToArray());
    }

    [Fact]
    public async Task GetChildrenAsyncBatchesParentsWithStableOrderingAndVisibility() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var emptyParentId = Guid.Parse("11111111-1000-0000-0000-000000000001");
        var populatedParentId = Guid.Parse("11111111-1000-0000-0000-000000000002");
        var hiddenParentId = Guid.Parse("11111111-1000-0000-0000-000000000003");
        var imageId = Guid.Parse("11111111-1000-0000-0000-000000000004");
        var firstVideoId = Guid.Parse("11111111-1000-0000-0000-000000000005");
        var secondVideoId = Guid.Parse("11111111-1000-0000-0000-000000000006");
        var hiddenChildId = Guid.Parse("11111111-1000-0000-0000-000000000007");
        var childOfHiddenParentId = Guid.Parse("11111111-1000-0000-0000-000000000008");
        db.Entities.AddRange(
            new EntityRow { Id = emptyParentId, KindCode = EntityKind.VideoSeries.ToCode(), Title = "Empty", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = populatedParentId, KindCode = EntityKind.VideoSeries.ToCode(), Title = "Populated", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = hiddenParentId, KindCode = EntityKind.VideoSeries.ToCode(), Title = "Hidden", IsNsfw = true, CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = imageId, KindCode = EntityKind.Image.ToCode(), Title = "Image", ParentEntityId = populatedParentId, SortOrder = 9, CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = firstVideoId, KindCode = EntityKind.VideoEpisode.ToCode(), Title = "First", ParentEntityId = populatedParentId, SortOrder = 1, CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = secondVideoId, KindCode = EntityKind.VideoEpisode.ToCode(), Title = "Second", ParentEntityId = populatedParentId, SortOrder = 2, CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = hiddenChildId, KindCode = EntityKind.VideoEpisode.ToCode(), Title = "Hidden child", ParentEntityId = populatedParentId, SortOrder = 0, IsNsfw = true, CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = childOfHiddenParentId, KindCode = EntityKind.VideoEpisode.ToCode(), Title = "Invisible subtree", ParentEntityId = hiddenParentId, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();

        var response = await CreateService(db).GetChildrenAsync(
            [emptyParentId, populatedParentId, populatedParentId, hiddenParentId, Guid.NewGuid()],
            hideNsfw: true,
            CancellationToken.None);

        Assert.Equal([emptyParentId, populatedParentId], response.Groups.Select(group => group.ParentId));
        Assert.Empty(response.Groups[0].Items);
        Assert.Equal(
            [imageId, firstVideoId, secondVideoId],
            response.Groups[1].Items.Select(item => item.Id));
    }

    [Fact]
    public async Task DisabledLibraryRootHidesInheritedAudioTrackAndDirectChildren() {
        await using var db = CreateContext();
        var enabledRootId = Guid.Parse("22222222-0000-0000-0000-000000000001");
        var disabledRootId = Guid.Parse("22222222-0000-0000-0000-000000000002");
        var artistId = Guid.Parse("22222222-0000-0000-0000-000000000003");
        var visibleAlbumId = Guid.Parse("22222222-0000-0000-0000-000000000004");
        var hiddenAlbumId = Guid.Parse("22222222-0000-0000-0000-000000000005");
        var visibleTrackId = Guid.Parse("22222222-0000-0000-0000-000000000006");
        var hiddenTrackId = Guid.Parse("22222222-0000-0000-0000-000000000007");
        var now = DateTimeOffset.UtcNow;
        db.LibraryRoots.AddRange(
            Root(enabledRootId, enabled: true, now),
            Root(disabledRootId, enabled: false, now));
        db.Entities.AddRange(
            new EntityRow { Id = artistId, KindCode = EntityKind.MusicArtist.ToCode(), Title = "Artist", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = visibleAlbumId, KindCode = EntityKind.AudioLibrary.ToCode(), Title = "Visible Album", ParentEntityId = artistId, SortOrder = 0, CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = hiddenAlbumId, KindCode = EntityKind.AudioLibrary.ToCode(), Title = "Hidden Album", ParentEntityId = artistId, SortOrder = 1, CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = visibleTrackId, KindCode = EntityKind.AudioTrack.ToCode(), Title = "Visible Track", ParentEntityId = visibleAlbumId, SortOrder = 0, CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = hiddenTrackId, KindCode = EntityKind.AudioTrack.ToCode(), Title = "Hidden Track", ParentEntityId = hiddenAlbumId, SortOrder = 0, CreatedAt = now, UpdatedAt = now });
        db.EntityLibraryRoots.Add(new EntityLibraryRootRow { EntityId = artistId, LibraryRootId = enabledRootId });
        db.EntityLibraryRoots.AddRange(
            new EntityLibraryRootRow { EntityId = visibleAlbumId, LibraryRootId = enabledRootId },
            new EntityLibraryRootRow { EntityId = hiddenAlbumId, LibraryRootId = disabledRootId });
        db.AudioTrackDetails.AddRange(
            new AudioTrackDetailRow { EntityId = visibleTrackId },
            new AudioTrackDetailRow { EntityId = hiddenTrackId });
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var tracks = await service.ListAsync(EntityKind.AudioTrack.ToCode(), null, null, null, null, CancellationToken.None);
        var artist = Assert.IsType<EntityCard>(await service.GetAsync(artistId, hideNsfw: false, CancellationToken.None));

        Assert.Equal(1, tracks.TotalCount);
        Assert.Equal(visibleTrackId, Assert.Single(tracks.Items).Id);
        var albums = Assert.Single(artist.ChildrenByKind);
        Assert.Equal(EntityKind.AudioLibrary, albums.Kind);
        Assert.Equal(visibleAlbumId, Assert.Single(albums.Entities).Id);
    }

    [Fact]
    public async Task DisabledLibraryRootHidesRelationshipTargetsAndDirectMovies() {
        await using var db = CreateContext();
        var enabledRootId = Guid.Parse("33333333-0000-0000-0000-000000000001");
        var disabledRootId = Guid.Parse("33333333-0000-0000-0000-000000000002");
        var sourceVideoId = Guid.Parse("33333333-0000-0000-0000-000000000003");
        var relatedVisibleVideoId = Guid.Parse("33333333-0000-0000-0000-000000000004");
        var hiddenMovieId = Guid.Parse("33333333-0000-0000-0000-000000000005");
        var now = DateTimeOffset.UtcNow;
        db.LibraryRoots.AddRange(
            Root(enabledRootId, enabled: true, now),
            Root(disabledRootId, enabled: false, now));
        db.Entities.AddRange(
            new EntityRow { Id = sourceVideoId, KindCode = EntityKind.Video.ToCode(), Title = "Source", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = relatedVisibleVideoId, KindCode = EntityKind.Video.ToCode(), Title = "Visible Related", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = hiddenMovieId, KindCode = EntityKind.Movie.ToCode(), Title = "Hidden Movie", CreatedAt = now, UpdatedAt = now });
        db.EntityLibraryRoots.AddRange(
            new EntityLibraryRootRow { EntityId = sourceVideoId, LibraryRootId = enabledRootId  },
            new EntityLibraryRootRow { EntityId = relatedVisibleVideoId, LibraryRootId = enabledRootId  },
            new EntityLibraryRootRow { EntityId = hiddenMovieId, LibraryRootId = disabledRootId  });
        db.EntityRelationshipLinks.AddRange(
            Link(sourceVideoId, relatedVisibleVideoId, EntityKind.Video, now),
            Link(sourceVideoId, hiddenMovieId, EntityKind.Movie, now));
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var source = Assert.IsType<EntityCard>(
            await service.GetAsync(sourceVideoId, hideNsfw: false, CancellationToken.None));
        var movies = await service.ListAsync(EntityKind.Movie.ToCode(), null, null, null, null, CancellationToken.None);

        var related = Assert.Single(source.Relationships, group => group.Code == RelationshipKind.Related);
        Assert.Equal(relatedVisibleVideoId, Assert.Single(related.Entities).Id);
        Assert.Empty(movies.Items);
        Assert.Equal(0, movies.TotalCount);

        static EntityRelationshipLinkRow Link(Guid source, Guid target, EntityKind targetKind, DateTimeOffset at) =>
            new() {
                EntityId = source,
                RelationshipCode = RelationshipKind.Related.ToCode(),
                Label = "Related",
                TargetEntityId = target,
                TargetKindCode = targetKind.ToCode(),
                CreatedAt = at,
            };
    }

    [Fact]
    public async Task ListAsyncReturnsUnboundedTotalCountForFilteredEntities() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 5; index++) {
            db.Entities.Add(new EntityRow {
                Id = Guid.NewGuid(),
                KindCode = EntityKind.Video.ToCode(),
                Title = $"Video {index:00}",
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        // A non-matching entity must not be counted in the kind-scoped total.
        db.Entities.Add(new EntityRow {
            Id = Guid.NewGuid(),
            KindCode = EntityKind.Image.ToCode(),
            Title = "Stray image",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var service = new EfEntityReadService(db, TestUserContext.Admin(), repository, ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db));

        var firstPage = await service.ListAsync(
            EntityKind.Video.ToCode(), query: null, cursor: null, hideNsfw: null, limit: 2, CancellationToken.None);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.NotNull(firstPage.NextCursor);
        Assert.Equal(5, firstPage.TotalCount);

        var secondPage = await service.ListAsync(
            EntityKind.Video.ToCode(), query: null, cursor: firstPage.NextCursor, hideNsfw: null, limit: 2, CancellationToken.None);
        Assert.Equal(2, secondPage.Items.Count);
        // Total is independent of cursor position — it always reflects the full filter match.
        Assert.Equal(5, secondPage.TotalCount);
    }

    [Fact]
    public async Task ListAsyncSortsByDateAddedDescendingThenAscending() {
        await using var db = CreateContext();
        var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var oldest = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var middle = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var newest = Guid.Parse("33333333-3333-3333-3333-333333333333");
        db.Entities.AddRange(
            new EntityRow { Id = oldest, KindCode = EntityKind.Video.ToCode(), Title = "Zeta", CreatedAt = baseTime, UpdatedAt = baseTime },
            new EntityRow { Id = middle, KindCode = EntityKind.Video.ToCode(), Title = "Alpha", CreatedAt = baseTime.AddHours(1), UpdatedAt = baseTime },
            new EntityRow { Id = newest, KindCode = EntityKind.Video.ToCode(), Title = "Mu", CreatedAt = baseTime.AddHours(2), UpdatedAt = baseTime });
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var descending = await service.ListAsync(
            EntityKind.Video.ToCode(), null, null, null, null, CancellationToken.None, sort: "added", sortDir: "desc");
        Assert.Equal(new[] { newest, middle, oldest }, descending.Items.Select(item => item.Id).ToArray());

        var ascending = await service.ListAsync(
            EntityKind.Video.ToCode(), null, null, null, null, CancellationToken.None, sort: "added", sortDir: "asc");
        Assert.Equal(new[] { oldest, middle, newest }, ascending.Items.Select(item => item.Id).ToArray());
    }

    [Fact]
    public async Task ListAsyncSortsByRatingPushingUnratedLast() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var unrated = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var lowRating = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var highRating = Guid.Parse("33333333-3333-3333-3333-333333333333");
        db.Entities.AddRange(
            new EntityRow { Id = unrated, KindCode = EntityKind.Video.ToCode(), Title = "Unrated", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = lowRating, KindCode = EntityKind.Video.ToCode(), Title = "Low", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = highRating, KindCode = EntityKind.Video.ToCode(), Title = "High", CreatedAt = now, UpdatedAt = now });
        db.UserEntityStates.AddRange(
            new UserEntityStateRow { UserId = TestUserContext.UserId, EntityId = lowRating, RatingValue = 2, UpdatedAt = now },
            new UserEntityStateRow { UserId = TestUserContext.UserId, EntityId = highRating, RatingValue = 5, UpdatedAt = now });
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var descending = await service.ListAsync(
            EntityKind.Video.ToCode(), null, null, null, null, CancellationToken.None, sort: "rating", sortDir: "desc");
        Assert.Equal(new[] { highRating, lowRating, unrated }, descending.Items.Select(item => item.Id).ToArray());
    }

    [Fact]
    public async Task ListAsyncRandomSortIsStableAcrossPagesForTheSameSeed() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 12; index++) {
            db.Entities.Add(new EntityRow {
                Id = Guid.Parse($"00000000-0000-0000-0000-0000000000{index:D2}"),
                KindCode = EntityKind.Video.ToCode(),
                Title = $"Video {index:00}",
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var firstPage = await service.ListAsync(
            EntityKind.Video.ToCode(), null, null, null, limit: 5, CancellationToken.None, sort: "random", seed: 1234);
        var secondPage = await service.ListAsync(
            EntityKind.Video.ToCode(), null, firstPage.NextCursor, null, limit: 5, CancellationToken.None, sort: "random", seed: 1234);
        var firstPageAgain = await service.ListAsync(
            EntityKind.Video.ToCode(), null, null, null, limit: 5, CancellationToken.None, sort: "random", seed: 1234);

        // The same seed reproduces the same overall order, and paging never repeats a row.
        Assert.Equal(
            firstPage.Items.Select(item => item.Id),
            firstPageAgain.Items.Select(item => item.Id));
        Assert.Empty(firstPage.Items.Select(item => item.Id)
            .Intersect(secondPage.Items.Select(item => item.Id)));
        Assert.Equal(12, firstPage.TotalCount);

        // A different seed should generally produce a different ordering.
        var differentSeed = await service.ListAsync(
            EntityKind.Video.ToCode(), null, null, null, limit: 5, CancellationToken.None, sort: "random", seed: 9999);
        Assert.NotEqual(
            firstPage.Items.Select(item => item.Id).ToArray(),
            differentSeed.Items.Select(item => item.Id).ToArray());
    }

    [Fact]
    public async Task ListAsyncFiltersByFavoriteOrganizedAndRatingBounds() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var favorite = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var organized = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var ratedThree = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var unrated = Guid.Parse("44444444-4444-4444-4444-444444444444");
        db.Entities.AddRange(
            new EntityRow { Id = favorite, KindCode = EntityKind.Video.ToCode(), Title = "Fav", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = organized, KindCode = EntityKind.Video.ToCode(), Title = "Org", IsOrganized = true, CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = ratedThree, KindCode = EntityKind.Video.ToCode(), Title = "Three", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = unrated, KindCode = EntityKind.Video.ToCode(), Title = "None", CreatedAt = now, UpdatedAt = now });
        db.UserEntityStates.AddRange(
            new UserEntityStateRow { UserId = TestUserContext.UserId, EntityId = favorite, IsFavorite = true, RatingValue = 5, UpdatedAt = now },
            new UserEntityStateRow { UserId = TestUserContext.UserId, EntityId = organized, RatingValue = 1, UpdatedAt = now },
            new UserEntityStateRow { UserId = TestUserContext.UserId, EntityId = ratedThree, RatingValue = 3, UpdatedAt = now });
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var favorites = await service.ListAsync(
            EntityKind.Video.ToCode(), null, null, null, null, CancellationToken.None, favorite: true);
        Assert.Equal(favorite, Assert.Single(favorites.Items).Id);

        var organizedOnly = await service.ListAsync(
            EntityKind.Video.ToCode(), null, null, null, null, CancellationToken.None, organized: true);
        Assert.Equal(organized, Assert.Single(organizedOnly.Items).Id);

        var atLeastThree = await service.ListAsync(
            EntityKind.Video.ToCode(), null, null, null, null, CancellationToken.None, ratingMin: 3);
        Assert.Equal(
            new[] { favorite, ratedThree }.OrderBy(id => id),
            atLeastThree.Items.Select(item => item.Id).OrderBy(id => id));
        Assert.Equal(2, atLeastThree.TotalCount);

        var unratedOnly = await service.ListAsync(
            EntityKind.Video.ToCode(), null, null, null, null, CancellationToken.None, unrated: true);
        Assert.Equal(unrated, Assert.Single(unratedOnly.Items).Id);
    }

    [Fact]
    public async Task ListAsyncFiltersByAdaptiveStatusForPlaybackAndProgress() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var watchedVideo = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var inProgressVideo = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var unwatchedVideo = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var readBook = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var inProgressBook = Guid.Parse("55555555-5555-5555-5555-555555555555");
        db.Entities.AddRange(
            new EntityRow { Id = watchedVideo, KindCode = EntityKind.Video.ToCode(), Title = "Watched", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = inProgressVideo, KindCode = EntityKind.Video.ToCode(), Title = "Watching", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = unwatchedVideo, KindCode = EntityKind.Video.ToCode(), Title = "Fresh", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = readBook, KindCode = EntityKind.Book.ToCode(), Title = "Read", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = inProgressBook, KindCode = EntityKind.Book.ToCode(), Title = "Reading", CreatedAt = now, UpdatedAt = now });
        db.UserEntityStates.AddRange(
            new UserEntityStateRow { UserId = TestUserContext.UserId, EntityId = watchedVideo, PlayCount = 1, CompletedAt = now, UpdatedAt = now },
            new UserEntityStateRow { UserId = TestUserContext.UserId, EntityId = inProgressVideo, PlayCount = 0, ResumeSeconds = 42, UpdatedAt = now });
        db.UserEntityStates.AddRange(
            new UserEntityStateRow { UserId = TestUserContext.UserId, EntityId = readBook, ProgressUnit = "page", ProgressIndex = 30, ProgressTotal = 30, ProgressCompletedAt = now, UpdatedAt = now },
            new UserEntityStateRow { UserId = TestUserContext.UserId, EntityId = inProgressBook, ProgressUnit = "page", ProgressIndex = 5, ProgressTotal = 30, UpdatedAt = now });
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var watched = await service.ListAsync(
            EntityKind.Video.ToCode(), null, null, null, null, CancellationToken.None, status: "watched");
        Assert.Equal(watchedVideo, Assert.Single(watched.Items).Id);

        var unwatched = await service.ListAsync(
            EntityKind.Video.ToCode(), null, null, null, null, CancellationToken.None, status: "unwatched");
        Assert.Equal(unwatchedVideo, Assert.Single(unwatched.Items).Id);

        var watchingVideo = await service.ListAsync(
            EntityKind.Video.ToCode(), null, null, null, null, CancellationToken.None, status: "in-progress");
        Assert.Equal(inProgressVideo, Assert.Single(watchingVideo.Items).Id);

        var read = await service.ListAsync(
            EntityKind.Book.ToCode(), null, null, null, null, CancellationToken.None, status: "read");
        Assert.Equal(readBook, Assert.Single(read.Items).Id);

        var reading = await service.ListAsync(
            EntityKind.Book.ToCode(), null, null, null, null, CancellationToken.None, status: "in-progress");
        Assert.Equal(inProgressBook, Assert.Single(reading.Items).Id);
    }

    [Fact]
    public async Task ListAsyncIncludesPartialFirstEpisodeAsSeriesProgress() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var seriesId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
        var seasonId = Guid.Parse("aaaaaaaa-2222-2222-2222-222222222222");
        var episodeId = Guid.Parse("aaaaaaaa-3333-3333-3333-333333333333");
        db.Entities.AddRange(
            new EntityRow { Id = seriesId, KindCode = EntityKind.VideoSeries.ToCode(), Title = "Series", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = seasonId, KindCode = EntityKind.VideoSeason.ToCode(), Title = "Season", ParentEntityId = seriesId, CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = episodeId, KindCode = EntityKind.VideoEpisode.ToCode(), Title = "Episode", ParentEntityId = seasonId, CreatedAt = now, UpdatedAt = now });
        db.EntityTechnical.Add(new EntityTechnicalRow {
            EntityId = episodeId,
            DurationSeconds = 100,
            UpdatedAt = now
        });
        db.UserEntityStates.AddRange(
            new UserEntityStateRow {
                UserId = TestUserContext.UserId,
                EntityId = seriesId,
                ProgressCurrentEntityId = episodeId,
                ProgressUnit = ProgressUnit.Item.ToCode(),
                ProgressIndex = 0,
                ProgressTotal = 4,
                UpdatedAt = now
            },
            new UserEntityStateRow {
                UserId = TestUserContext.UserId,
                EntityId = episodeId,
                ResumeSeconds = 50,
                UpdatedAt = now
            });
        await db.SaveChangesAsync();

        var result = await CreateService(db).ListAsync(
            EntityKind.VideoSeries.ToCode(),
            null,
            null,
            null,
            null,
            CancellationToken.None,
            status: "in-progress");

        var thumbnail = Assert.Single(result.Items);
        Assert.Equal(seriesId, thumbnail.Id);
        Assert.Equal(0.125, thumbnail.Progress);
    }

    [Fact]
    public async Task ListAsyncUsesDirectMoviePlaybackForFiltersAndProgress() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var watchedMovie = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var unwatchedMovie = Guid.Parse("33333333-3333-3333-3333-333333333333");
        db.Entities.AddRange(
            new EntityRow { Id = watchedMovie, KindCode = EntityKind.Movie.ToCode(), Title = "Watched Movie", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = unwatchedMovie, KindCode = EntityKind.Movie.ToCode(), Title = "Fresh Movie", CreatedAt = now, UpdatedAt = now });
        db.EntityTechnical.Add(new EntityTechnicalRow {
            EntityId = watchedMovie,
            DurationSeconds = 200,
            UpdatedAt = now
        });
        db.UserEntityStates.Add(new UserEntityStateRow {
            UserId = TestUserContext.UserId,
            EntityId = watchedMovie,
            ResumeSeconds = 50,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var played = await service.ListAsync(
            EntityKind.Movie.ToCode(), null, null, null, null, CancellationToken.None, played: true);
        var unplayed = await service.ListAsync(
            EntityKind.Movie.ToCode(), null, null, null, null, CancellationToken.None, played: false);
        var watchedThumbnail = Assert.Single(played.Items);

        Assert.Equal(watchedMovie, watchedThumbnail.Id);
        Assert.Equal(50, watchedThumbnail.ResumeSeconds);
        Assert.Equal(0.25, watchedThumbnail.Progress);
        Assert.Equal(unwatchedMovie, Assert.Single(unplayed.Items).Id);
    }

    [Fact]
    public async Task ListAsyncSortsByMostRecentEngagementForLastPlayed() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var playedToday = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var playedLastWeek = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var neverPlayed = Guid.Parse("33333333-3333-3333-3333-333333333333");
        db.Entities.AddRange(
            new EntityRow { Id = playedToday, KindCode = EntityKind.Video.ToCode(), Title = "Today", CreatedAt = now.AddYears(-1), UpdatedAt = now },
            new EntityRow { Id = playedLastWeek, KindCode = EntityKind.Video.ToCode(), Title = "Last Week", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = neverPlayed, KindCode = EntityKind.Video.ToCode(), Title = "Never", CreatedAt = now, UpdatedAt = now });
        db.UserEntityStates.AddRange(
            new UserEntityStateRow { UserId = TestUserContext.UserId, EntityId = playedToday, PlayCount = 1, LastPlayedAt = now, UpdatedAt = now },
            new UserEntityStateRow { UserId = TestUserContext.UserId, EntityId = playedLastWeek, PlayCount = 1, LastPlayedAt = now.AddDays(-7), UpdatedAt = now.AddDays(-7) });
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var result = await service.ListAsync(
            EntityKind.Video.ToCode(), null, null, null, null, CancellationToken.None,
            sort: "last-played", sortDir: "desc");

        // Most recently played first, then older, with the never-played entity sorted last.
        Assert.Equal([playedToday, playedLastWeek, neverPlayed], result.Items.Select(item => item.Id).ToArray());
    }

    [Fact]
    public async Task ListAsyncFiltersBooksByTypeAndFormat() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var comicCbz = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var comicPdf = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var novelEpub = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var mangaCbz = Guid.Parse("44444444-4444-4444-4444-444444444444");
        db.Entities.AddRange(
            new EntityRow { Id = comicCbz, KindCode = EntityKind.Book.ToCode(), Title = "Comic Archive", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = comicPdf, KindCode = EntityKind.Book.ToCode(), Title = "Comic PDF", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = novelEpub, KindCode = EntityKind.Book.ToCode(), Title = "Novel EPUB", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = mangaCbz, KindCode = EntityKind.Book.ToCode(), Title = "Manga Archive", CreatedAt = now, UpdatedAt = now });
        db.BookDetails.AddRange(
            new BookDetailRow { EntityId = comicCbz, BookType = BookType.Comic, Format = BookFormat.ImageArchive },
            new BookDetailRow { EntityId = comicPdf, BookType = BookType.Comic, Format = BookFormat.Pdf },
            new BookDetailRow { EntityId = novelEpub, BookType = BookType.Novel, Format = BookFormat.Epub },
            new BookDetailRow { EntityId = mangaCbz, BookType = BookType.Manga, Format = BookFormat.ImageArchive });
        await db.SaveChangesAsync();

        var service = CreateService(db);

        // Single type: only comics, regardless of format.
        var comics = await service.ListAsync(
            EntityKind.Book.ToCode(), null, null, null, null, CancellationToken.None, bookType: "comic");
        Assert.Equal([comicCbz, comicPdf], comics.Items.Select(item => item.Id).Order().ToArray());
        Assert.Equal(2, comics.TotalCount);

        // Multiple types are OR-ed within the family.
        var comicsAndManga = await service.ListAsync(
            EntityKind.Book.ToCode(), null, null, null, null, CancellationToken.None, bookType: "comic,manga");
        Assert.Equal([comicCbz, comicPdf, mangaCbz], comicsAndManga.Items.Select(item => item.Id).Order().ToArray());

        // Single format: only PDFs.
        var pdfs = await service.ListAsync(
            EntityKind.Book.ToCode(), null, null, null, null, CancellationToken.None, bookFormat: "pdf");
        Assert.Equal(comicPdf, Assert.Single(pdfs.Items).Id);

        // Type and format combine with AND across families.
        var comicArchives = await service.ListAsync(
            EntityKind.Book.ToCode(), null, null, null, null, CancellationToken.None,
            bookType: "comic", bookFormat: "image-archive");
        Assert.Equal(comicCbz, Assert.Single(comicArchives.Items).Id);

        // Unknown codes are ignored, leaving the result unfiltered by that family.
        var unknown = await service.ListAsync(
            EntityKind.Book.ToCode(), null, null, null, null, CancellationToken.None, bookType: "nonsense");
        Assert.Equal(4, unknown.TotalCount);
    }

    [Fact]
    public async Task ListAsyncProjectsBookTypeMetaForBookThumbnails() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var comicId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        db.Entities.Add(new EntityRow {
            Id = comicId,
            KindCode = EntityKind.Book.ToCode(),
            Title = "Comic Archive",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.BookDetails.Add(new BookDetailRow {
            EntityId = comicId,
            BookType = BookType.Comic,
            Format = BookFormat.ImageArchive
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var result = await service.ListAsync(
            EntityKind.Book.ToCode(), null, null, null, null, CancellationToken.None, bookType: "comic");
        var item = Assert.Single(result.Items);

        Assert.Contains(new EntityThumbnailMeta("book", "Comic"), item.Meta);
    }

    [Fact]
    public async Task ListAsyncFiltersByNsfwHasFileAndPlayedAcrossTheWholeSet() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;

        var sfwUnplayedNoFile = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var nsfwPlayedWithFile = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
        var sfwReadingNoFile = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

        db.Entities.AddRange(
            new EntityRow { Id = sfwUnplayedNoFile, KindCode = EntityKind.Image.ToCode(), Title = "Alpha", IsNsfw = false, CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = nsfwPlayedWithFile, KindCode = EntityKind.Image.ToCode(), Title = "Bravo", IsNsfw = true, CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = sfwReadingNoFile, KindCode = EntityKind.Image.ToCode(), Title = "Charlie", IsNsfw = false, CreatedAt = now, UpdatedAt = now });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(), EntityId = nsfwPlayedWithFile, Role = EntityFileRole.Source, Path = "/m/b.jpg", CreatedAt = now, UpdatedAt = now,
        });
        db.UserEntityStates.Add(new UserEntityStateRow {
            UserId = TestUserContext.UserId, EntityId = nsfwPlayedWithFile, PlayCount = 3, LastPlayedAt = now, UpdatedAt = now });
        db.UserEntityStates.Add(new UserEntityStateRow {
            UserId = TestUserContext.UserId, EntityId = sfwReadingNoFile, ProgressIndex = 4, ProgressTotal = 10, UpdatedAt = now });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var kind = EntityKind.Image.ToCode();

        var onlyNsfw = await service.ListAsync(kind, null, null, null, null, CancellationToken.None, nsfw: true);
        Assert.Equal(1, onlyNsfw.TotalCount);
        Assert.Equal(nsfwPlayedWithFile, Assert.Single(onlyNsfw.Items).Id);

        var notNsfw = await service.ListAsync(kind, null, null, null, null, CancellationToken.None, nsfw: false);
        Assert.Equal(2, notNsfw.TotalCount);
        Assert.DoesNotContain(notNsfw.Items, item => item.Id == nsfwPlayedWithFile);

        var withFile = await service.ListAsync(kind, null, null, null, null, CancellationToken.None, hasFile: true);
        Assert.Equal(nsfwPlayedWithFile, Assert.Single(withFile.Items).Id);

        var noFile = await service.ListAsync(kind, null, null, null, null, CancellationToken.None, hasFile: false);
        Assert.Equal(2, noFile.TotalCount);

        // Played includes both playback engagement (Bravo) and started reading progress (Charlie).
        var played = await service.ListAsync(kind, null, null, null, null, CancellationToken.None, played: true);
        Assert.Equal(2, played.TotalCount);
        Assert.DoesNotContain(played.Items, item => item.Id == sfwUnplayedNoFile);

        var unplayed = await service.ListAsync(kind, null, null, null, null, CancellationToken.None, played: false);
        Assert.Equal(sfwUnplayedNoFile, Assert.Single(unplayed.Items).Id);
    }

    [Fact]
    public async Task ListAsyncProjectsSourceMediaAndFiltersByLatestAcquisitionStatus() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var downloaded = Guid.Parse("ac100000-0000-0000-0000-000000000001");
        var failed = Guid.Parse("ac100000-0000-0000-0000-000000000002");

        db.Entities.AddRange(
            new EntityRow { Id = downloaded, KindCode = EntityKind.Book.ToCode(), Title = "Downloaded", IsWanted = true, CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = failed, KindCode = EntityKind.Book.ToCode(), Title = "Failed", IsWanted = false, CreatedAt = now, UpdatedAt = now });
        db.EntityFiles.AddRange(
            new EntityFileRow { Id = Guid.NewGuid(), EntityId = downloaded, Role = EntityFileRole.Cover, Path = "/covers/downloaded.jpg", CreatedAt = now, UpdatedAt = now },
            new EntityFileRow { Id = Guid.NewGuid(), EntityId = failed, Role = EntityFileRole.Source, Path = "/books/failed.epub", CreatedAt = now, UpdatedAt = now });
        db.Acquisitions.AddRange(
            new AcquisitionRow { Id = Guid.Parse("ac200000-0000-0000-0000-000000000001"), EntityId = downloaded, Status = AcquisitionStatus.Failed, Title = "Downloaded", ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = now, UpdatedAt = now },
            new AcquisitionRow { Id = Guid.Parse("ac200000-0000-0000-0000-000000000002"), EntityId = downloaded, Status = AcquisitionStatus.Downloaded, Title = "Downloaded", ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = now, UpdatedAt = now },
            new AcquisitionRow { Id = Guid.NewGuid(), EntityId = failed, Status = AcquisitionStatus.Failed, Title = "Failed", ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await ((IEntityReadService)service).ListAsync(new EntityListQuery {
            Kind = EntityKind.Book.ToCode(),
            AcquisitionStatus = AcquisitionStatus.Downloaded,
        }, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(downloaded, item.Id);
        Assert.False(item.HasSourceMedia);
        Assert.Equal(AcquisitionStatus.Downloaded, item.LatestAcquisitionStatus);
        Assert.Equal(AcquisitionStatus.Downloaded, item.WantedStatus);

        var all = await service.ListAsync(EntityKind.Book.ToCode(), null, null, null, null, CancellationToken.None);
        var stored = Assert.Single(all.Items, candidate => candidate.Id == failed);
        Assert.True(stored.HasSourceMedia);
        Assert.Equal(AcquisitionStatus.Failed, stored.LatestAcquisitionStatus);
        Assert.Null(stored.WantedStatus);
    }

    [Fact]
    public async Task AcquisitionAvailabilityIncludesStructuralChildrenAndUpgradeWorkAcrossMediaKinds() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var seriesId = Guid.Parse("a1100000-0000-0000-0000-000000000001");
        var seasonId = Guid.Parse("a1100000-0000-0000-0000-000000000002");
        var episodeId = Guid.Parse("a1100000-0000-0000-0000-000000000003");
        var unrelatedSeriesId = Guid.Parse("a1100000-0000-0000-0000-000000000004");
        var artistId = Guid.Parse("a1100000-0000-0000-0000-000000000005");
        var albumId = Guid.Parse("a1100000-0000-0000-0000-000000000006");
        var unrelatedArtistId = Guid.Parse("a1100000-0000-0000-0000-000000000007");
        db.Entities.AddRange(
            Entity(seriesId, EntityKind.VideoSeries, "Series", parentId: null),
            Entity(seasonId, EntityKind.VideoSeason, "Season", seriesId),
            Entity(episodeId, EntityKind.VideoEpisode, "Episode", seasonId),
            Entity(unrelatedSeriesId, EntityKind.VideoSeries, "Unrelated series", parentId: null),
            Entity(artistId, EntityKind.MusicArtist, "Artist", parentId: null),
            Entity(albumId, EntityKind.AudioLibrary, "Album", artistId),
            Entity(unrelatedArtistId, EntityKind.MusicArtist, "Unrelated artist", parentId: null));

        var seriesAcquisitionId = Guid.Parse("a1200000-0000-0000-0000-000000000001");
        var oldSeasonAcquisitionId = Guid.Parse("a1200000-0000-0000-0000-000000000002");
        var seasonAcquisitionId = Guid.Parse("a1200000-0000-0000-0000-000000000003");
        var episodeAcquisitionId = Guid.Parse("a1200000-0000-0000-0000-000000000004");
        db.Acquisitions.AddRange(
            Acquisition(seriesAcquisitionId, EntityKind.VideoSeries, AcquisitionStatus.Imported, seriesId, now.AddMinutes(-8)),
            Acquisition(oldSeasonAcquisitionId, EntityKind.VideoSeason, AcquisitionStatus.Pending, seasonId, now.AddMinutes(-7)),
            Acquisition(seasonAcquisitionId, EntityKind.VideoSeason, AcquisitionStatus.Failed, seasonId, now.AddMinutes(-6)),
            Acquisition(episodeAcquisitionId, EntityKind.VideoEpisode, AcquisitionStatus.Imported, episodeId, now.AddMinutes(-5)),
            Acquisition(Guid.Parse("a1200000-0000-0000-0000-000000000005"), EntityKind.VideoEpisode, AcquisitionStatus.Downloading, entityId: null, now.AddMinutes(-4), episodeAcquisitionId),
            Acquisition(Guid.Parse("a1200000-0000-0000-0000-000000000006"), EntityKind.VideoEpisode, AcquisitionStatus.Cancelled, entityId: null, now.AddMinutes(-3), episodeAcquisitionId),
            Acquisition(Guid.Parse("a1200000-0000-0000-0000-000000000007"), EntityKind.AudioLibrary, AcquisitionStatus.Downloaded, albumId, now.AddMinutes(-2)));
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var thumbnails = await service.GetThumbnailsAsync(
            [seriesId, artistId],
            hideNsfw: false,
            CancellationToken.None);
        var series = Assert.Single(thumbnails.Items, item => item.Id == seriesId);
        var artist = Assert.Single(thumbnails.Items, item => item.Id == artistId);

        Assert.Equal(AcquisitionStatus.Imported, series.LatestAcquisitionStatus);
        Assert.Equal(
            [AcquisitionStatus.Downloading, AcquisitionStatus.Imported, AcquisitionStatus.Failed, AcquisitionStatus.Cancelled],
            series.AcquisitionStatuses.OrderBy(status => status));
        Assert.Null(artist.LatestAcquisitionStatus);
        Assert.Equal([AcquisitionStatus.Downloaded], artist.AcquisitionStatuses);

        var downloadingSeries = await service.ListAsync(
            EntityKind.VideoSeries.ToCode(),
            null,
            null,
            false,
            limit: 1,
            CancellationToken.None,
            acquisitionStatus: AcquisitionStatus.Downloading);
        Assert.Equal(1, downloadingSeries.TotalCount);
        Assert.Equal(seriesId, Assert.Single(downloadingSeries.Items).Id);

        var failedSeries = await service.ListAsync(
            EntityKind.VideoSeries.ToCode(),
            null,
            null,
            false,
            limit: 1,
            CancellationToken.None,
            acquisitionStatus: AcquisitionStatus.Failed);
        Assert.Equal(seriesId, Assert.Single(failedSeries.Items).Id);

        var obsoleteSeasonState = await service.ListAsync(
            EntityKind.VideoSeries.ToCode(),
            null,
            null,
            false,
            limit: 1,
            CancellationToken.None,
            acquisitionStatus: AcquisitionStatus.Pending);
        Assert.Empty(obsoleteSeasonState.Items);

        var downloadedArtist = await service.ListAsync(
            EntityKind.MusicArtist.ToCode(),
            null,
            null,
            false,
            limit: 1,
            CancellationToken.None,
            acquisitionStatus: AcquisitionStatus.Downloaded);
        Assert.Equal(1, downloadedArtist.TotalCount);
        Assert.Equal(artistId, Assert.Single(downloadedArtist.Items).Id);

        EntityRow Entity(Guid id, EntityKind kind, string title, Guid? parentId) => new() {
            Id = id,
            KindCode = kind.ToCode(),
            Title = title,
            ParentEntityId = parentId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        static AcquisitionRow Acquisition(
            Guid id,
            EntityKind kind,
            AcquisitionStatus status,
            Guid? entityId,
            DateTimeOffset createdAt,
            Guid? upgradeOfAcquisitionId = null) => new() {
                Id = id,
                Kind = kind,
                Status = status,
                EntityId = entityId,
                UpgradeOfAcquisitionId = upgradeOfAcquisitionId,
                Title = kind.ToCode(),
                ExternalIdsJson = "{}",
                SourceUrlsJson = "[]",
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
            };
    }

    [Fact]
    public async Task ListAsyncFiltersOrphanedTaxonomyAcrossTheWholeSet() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var referencedTag = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
        var orphanTag = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
        var videoId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000003");

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

        var service = CreateService(db);
        var kind = EntityKind.Tag.ToCode();

        var orphans = await service.ListAsync(kind, null, null, null, null, CancellationToken.None, orphaned: true);
        Assert.Equal(orphanTag, Assert.Single(orphans.Items).Id);

        var referenced = await service.ListAsync(kind, null, null, null, null, CancellationToken.None, orphaned: false);
        Assert.Equal(referencedTag, Assert.Single(referenced.Items).Id);
    }

    [Fact]
    public async Task ListAsyncSortsTaxonomyByReferenceCount() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var heavy = Guid.Parse("dddddddd-0000-0000-0000-000000000001"); // 2 references
        var light = Guid.Parse("dddddddd-0000-0000-0000-000000000002"); // 1 reference
        var none = Guid.Parse("dddddddd-0000-0000-0000-000000000003");  // 0 references
        var v1 = Guid.Parse("dddddddd-0000-0000-0000-000000000011");
        var v2 = Guid.Parse("dddddddd-0000-0000-0000-000000000012");

        db.Entities.AddRange(
            new EntityRow { Id = heavy, KindCode = EntityKind.Tag.ToCode(), Title = "Heavy", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = light, KindCode = EntityKind.Tag.ToCode(), Title = "Light", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = none, KindCode = EntityKind.Tag.ToCode(), Title = "None", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = v1, KindCode = EntityKind.Video.ToCode(), Title = "V1", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = v2, KindCode = EntityKind.Video.ToCode(), Title = "V2", CreatedAt = now, UpdatedAt = now });
        db.EntityRelationshipLinks.AddRange(Link(v1, heavy), Link(v2, heavy), Link(v1, light));
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var kind = EntityKind.Tag.ToCode();

        var desc = await service.ListAsync(kind, null, null, null, null, CancellationToken.None, sort: "references", sortDir: "desc");
        Assert.Equal(new[] { heavy, light, none }, desc.Items.Select(item => item.Id).ToArray());

        var asc = await service.ListAsync(kind, null, null, null, null, CancellationToken.None, sort: "references", sortDir: "asc");
        Assert.Equal(new[] { none, light, heavy }, asc.Items.Select(item => item.Id).ToArray());

        static EntityRelationshipLinkRow Link(Guid source, Guid target) =>
            new() {
                EntityId = source,
                RelationshipCode = "tags",
                Label = "Tags",
                TargetEntityId = target,
                TargetKindCode = EntityKind.Tag.ToCode(),
                CreatedAt = DateTimeOffset.UtcNow,
            };
    }

    [Fact]
    public async Task GetThumbnailsAsyncCountsVisibleVideoHierarchyWithoutWantedOrHiddenDescendants() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var enabledRootId = Guid.NewGuid();
        var disabledRootId = Guid.NewGuid();
        var seriesId = Guid.NewGuid();
        var visibleSeasonId = Guid.NewGuid();
        var nsfwSeasonId = Guid.NewGuid();
        var wantedSeasonId = Guid.NewGuid();
        var visibleEpisodeId = Guid.NewGuid();
        var directEpisodeId = Guid.NewGuid();
        var nsfwEpisodeId = Guid.NewGuid();
        var wantedEpisodeId = Guid.NewGuid();
        var hiddenLibraryEpisodeId = Guid.NewGuid();
        var episodeBelowHiddenSeasonId = Guid.NewGuid();

        db.LibraryRoots.AddRange(
            Root(enabledRootId, enabled: true, now),
            Root(disabledRootId, enabled: false, now));
        db.Entities.AddRange(
            Row(seriesId, EntityKind.VideoSeries.ToCode(), "Series", now),
            Row(visibleSeasonId, EntityKind.VideoSeason.ToCode(), "Season 1", now, seriesId),
            Row(nsfwSeasonId, EntityKind.VideoSeason.ToCode(), "Season 2", now, seriesId, isNsfw: true),
            Row(wantedSeasonId, EntityKind.VideoSeason.ToCode(), "Season 3", now, seriesId, isWanted: true),
            Row(visibleEpisodeId, EntityKind.VideoEpisode.ToCode(), "Episode 1", now, visibleSeasonId),
            Row(directEpisodeId, EntityKind.VideoEpisode.ToCode(), "Special", now, seriesId),
            Row(nsfwEpisodeId, EntityKind.VideoEpisode.ToCode(), "Hidden Episode", now, visibleSeasonId, isNsfw: true),
            Row(wantedEpisodeId, EntityKind.VideoEpisode.ToCode(), "Wanted Episode", now, visibleSeasonId, isWanted: true),
            Row(hiddenLibraryEpisodeId, EntityKind.VideoEpisode.ToCode(), "Disabled Library Episode", now, visibleSeasonId),
            Row(episodeBelowHiddenSeasonId, EntityKind.VideoEpisode.ToCode(), "Hidden Season Episode", now, nsfwSeasonId));
        db.EntityLibraryRoots.AddRange(
            new EntityLibraryRootRow { EntityId = visibleEpisodeId, LibraryRootId = enabledRootId  },
            new EntityLibraryRootRow { EntityId = directEpisodeId, LibraryRootId = enabledRootId  },
            new EntityLibraryRootRow { EntityId = nsfwEpisodeId, LibraryRootId = enabledRootId  },
            new EntityLibraryRootRow { EntityId = wantedEpisodeId, LibraryRootId = enabledRootId  },
            new EntityLibraryRootRow { EntityId = hiddenLibraryEpisodeId, LibraryRootId = disabledRootId  },
            new EntityLibraryRootRow { EntityId = episodeBelowHiddenSeasonId, LibraryRootId = enabledRootId  });
        await db.SaveChangesAsync();

        var response = await CreateService(db).GetThumbnailsAsync(
            [seriesId, visibleSeasonId],
            hideNsfw: true,
            CancellationToken.None);

        var series = response.Items.Single(item => item.Id == seriesId);
        Assert.Equal(
            [
                new EntityThumbnailMeta(EntityThumbnailMetaIcons.Season, "1"),
                new EntityThumbnailMeta(EntityThumbnailMetaIcons.Episode, "2")
            ],
            series.Meta);
        var season = response.Items.Single(item => item.Id == visibleSeasonId);
        Assert.Equal([new EntityThumbnailMeta(EntityThumbnailMetaIcons.Episode, "1")], season.Meta);

        static EntityRow Row(
            Guid id,
            string kind,
            string title,
            DateTimeOffset at,
            Guid? parentId = null,
            bool isNsfw = false,
            bool isWanted = false) =>
            new() {
                Id = id,
                KindCode = kind,
                Title = title,
                ParentEntityId = parentId,
                IsNsfw = isNsfw,
                IsWanted = isWanted,
                CreatedAt = at,
                UpdatedAt = at
            };
    }

    [Fact]
    public async Task GetThumbnailsAsyncCountsBookHierarchyAcrossDirectAndNestedPages() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var bookId = Guid.NewGuid();
        var volumeId = Guid.NewGuid();
        var nestedChapterId = Guid.NewGuid();
        var directChapterId = Guid.NewGuid();
        var directPageId = Guid.NewGuid();
        var nestedPageId = Guid.NewGuid();
        var chapterPageId = Guid.NewGuid();
        var hiddenVolumeId = Guid.NewGuid();
        var hiddenChapterId = Guid.NewGuid();
        var hiddenPageId = Guid.NewGuid();
        var wantedChapterId = Guid.NewGuid();
        var wantedPageId = Guid.NewGuid();

        db.Entities.AddRange(
            Row(bookId, EntityKind.Book.ToCode(), "Book", now),
            Row(volumeId, EntityKind.BookVolume.ToCode(), "Volume 1", now, bookId),
            Row(nestedChapterId, EntityKind.BookChapter.ToCode(), "Nested Chapter", now, volumeId),
            Row(directChapterId, EntityKind.BookChapter.ToCode(), "Direct Chapter", now, bookId),
            Row(directPageId, EntityKind.BookPage.ToCode(), "Loose Page", now, bookId),
            Row(nestedPageId, EntityKind.BookPage.ToCode(), "Nested Page", now, nestedChapterId),
            Row(chapterPageId, EntityKind.BookPage.ToCode(), "Direct Chapter Page", now, directChapterId),
            Row(hiddenVolumeId, EntityKind.BookVolume.ToCode(), "Hidden Volume", now, bookId, isNsfw: true),
            Row(hiddenChapterId, EntityKind.BookChapter.ToCode(), "Hidden Chapter", now, hiddenVolumeId),
            Row(hiddenPageId, EntityKind.BookPage.ToCode(), "Hidden Page", now, hiddenChapterId),
            Row(wantedChapterId, EntityKind.BookChapter.ToCode(), "Wanted Chapter", now, bookId, isWanted: true),
            Row(wantedPageId, EntityKind.BookPage.ToCode(), "Wanted Page", now, wantedChapterId));
        db.BookDetails.Add(new BookDetailRow { EntityId = bookId, BookType = BookType.Book });
        db.EntityTechnical.Add(new EntityTechnicalRow { EntityId = bookId, DurationSeconds = 3600, UpdatedAt = now });
        await db.SaveChangesAsync();

        var response = await CreateService(db).GetThumbnailsAsync(
            [bookId, volumeId, nestedChapterId],
            hideNsfw: true,
            CancellationToken.None);

        Assert.Equal(
            [
                new EntityThumbnailMeta(EntityThumbnailMetaIcons.Volume, "1"),
                new EntityThumbnailMeta(EntityThumbnailMetaIcons.Chapter, "2"),
                new EntityThumbnailMeta(EntityThumbnailMetaIcons.Page, "3"),
                new EntityThumbnailMeta(EntityThumbnailMetaIcons.Book, "Book"),
                new EntityThumbnailMeta(EntityThumbnailMetaIcons.Duration, "01:00")
            ],
            response.Items.Single(item => item.Id == bookId).Meta);
        Assert.Equal(
            [
                new EntityThumbnailMeta(EntityThumbnailMetaIcons.Chapter, "1"),
                new EntityThumbnailMeta(EntityThumbnailMetaIcons.Page, "1")
            ],
            response.Items.Single(item => item.Id == volumeId).Meta);
        Assert.Equal(
            [new EntityThumbnailMeta(EntityThumbnailMetaIcons.Page, "1")],
            response.Items.Single(item => item.Id == nestedChapterId).Meta);

        static EntityRow Row(
            Guid id,
            string kind,
            string title,
            DateTimeOffset at,
            Guid? parentId = null,
            bool isNsfw = false,
            bool isWanted = false) =>
            new() {
                Id = id,
                KindCode = kind,
                Title = title,
                ParentEntityId = parentId,
                IsNsfw = isNsfw,
                IsWanted = isWanted,
                CreatedAt = at,
                UpdatedAt = at
            };
    }

    [Fact]
    public async Task GetThumbnailsAsyncCountsRemainingStructuralAndMaterializedCollectionMembership() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var authorId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        var wantedBookId = Guid.NewGuid();
        var artistId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var wantedAlbumId = Guid.NewGuid();
        var trackOneId = Guid.NewGuid();
        var trackTwoId = Guid.NewGuid();
        var wantedTrackId = Guid.NewGuid();
        var galleryId = Guid.NewGuid();
        var nestedGalleryId = Guid.NewGuid();
        var imageOneId = Guid.NewGuid();
        var imageTwoId = Guid.NewGuid();
        var nsfwImageId = Guid.NewGuid();
        var collectionId = Guid.NewGuid();

        db.Entities.AddRange(
            Row(authorId, EntityKind.BookAuthor.ToCode(), "Author", now),
            Row(bookId, EntityKind.Book.ToCode(), "Published Book", now, authorId),
            Row(wantedBookId, EntityKind.Book.ToCode(), "Wanted Book", now, authorId, isWanted: true),
            Row(artistId, EntityKind.MusicArtist.ToCode(), "Artist", now),
            Row(albumId, EntityKind.AudioLibrary.ToCode(), "Album", now, artistId),
            Row(wantedAlbumId, EntityKind.AudioLibrary.ToCode(), "Wanted Album", now, artistId, isWanted: true),
            Row(trackOneId, EntityKind.AudioTrack.ToCode(), "Track 1", now, albumId),
            Row(trackTwoId, EntityKind.AudioTrack.ToCode(), "Track 2", now, albumId),
            Row(wantedTrackId, EntityKind.AudioTrack.ToCode(), "Wanted Track", now, albumId, isWanted: true),
            Row(galleryId, EntityKind.Gallery.ToCode(), "Gallery", now),
            Row(nestedGalleryId, EntityKind.Gallery.ToCode(), "Nested Gallery", now, galleryId),
            Row(imageOneId, EntityKind.Image.ToCode(), "Image 1", now, galleryId),
            Row(imageTwoId, EntityKind.Image.ToCode(), "Image 2", now, nestedGalleryId),
            Row(nsfwImageId, EntityKind.Image.ToCode(), "Hidden Image", now, galleryId, isNsfw: true),
            Row(collectionId, EntityKind.Collection.ToCode(), "Dynamic Collection", now));
        db.CollectionDetails.Add(new CollectionDetailRow {
            EntityId = collectionId,
            OwnerUserId = TestUserContext.UserId,
            Mode = CollectionMode.Dynamic
        });
        db.CollectionItemDetails.AddRange(
            Item(collectionId, bookId, CollectionItemSource.Dynamic, now),
            Item(collectionId, trackOneId, CollectionItemSource.Dynamic, now),
            Item(collectionId, nsfwImageId, CollectionItemSource.Dynamic, now),
            Item(collectionId, wantedTrackId, CollectionItemSource.Dynamic, now));
        await db.SaveChangesAsync();

        var response = await CreateService(db).GetThumbnailsAsync(
            [authorId, artistId, albumId, galleryId, collectionId, imageOneId],
            hideNsfw: true,
            CancellationToken.None);

        Assert.Equal(
            [new EntityThumbnailMeta(EntityThumbnailMetaIcons.Book, "1")],
            response.Items.Single(item => item.Id == authorId).Meta);
        Assert.Equal(
            [
                new EntityThumbnailMeta(EntityThumbnailMetaIcons.Album, "1"),
                new EntityThumbnailMeta(EntityThumbnailMetaIcons.Track, "2")
            ],
            response.Items.Single(item => item.Id == artistId).Meta);
        Assert.Equal(
            [new EntityThumbnailMeta(EntityThumbnailMetaIcons.Track, "2")],
            response.Items.Single(item => item.Id == albumId).Meta);
        Assert.Equal(
            [new EntityThumbnailMeta(EntityThumbnailMetaIcons.Image, "1")],
            response.Items.Single(item => item.Id == galleryId).Meta);
        Assert.Equal(
            [new EntityThumbnailMeta(EntityThumbnailMetaIcons.Collection, "2")],
            response.Items.Single(item => item.Id == collectionId).Meta);
        Assert.Empty(response.Items.Single(item => item.Id == imageOneId).Meta);

        static EntityRow Row(
            Guid id,
            string kind,
            string title,
            DateTimeOffset at,
            Guid? parentId = null,
            bool isNsfw = false,
            bool isWanted = false) =>
            new() {
                Id = id,
                KindCode = kind,
                Title = title,
                ParentEntityId = parentId,
                IsNsfw = isNsfw,
                IsWanted = isWanted,
                CreatedAt = at,
                UpdatedAt = at
            };

        static CollectionItemDetailRow Item(
            Guid collectionId,
            Guid itemId,
            CollectionItemSource source,
            DateTimeOffset at) =>
            new() {
                Id = Guid.NewGuid(),
                CollectionEntityId = collectionId,
                ItemEntityId = itemId,
                Source = source,
                AddedAt = at
            };
    }

    [Fact]
    public async Task GetThumbnailsAsyncUsesDirectMovieTechnicalMetadata() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var movieId = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = movieId,
            KindCode = EntityKind.Movie.ToCode(),
            Title = "Movie",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.EntityTechnical.Add(new EntityTechnicalRow {
            EntityId = movieId,
            DurationSeconds = 596,
            Width = 3840,
            Height = 1920,
            Codec = "h264",
            Container = "matroska",
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var response = await CreateService(db).GetThumbnailsAsync(
            [movieId],
            hideNsfw: false,
            CancellationToken.None);

        Assert.Equal(
            [
                new EntityThumbnailMeta(EntityThumbnailMetaIcons.Duration, "09:56"),
                new EntityThumbnailMeta(EntityThumbnailMetaIcons.Video, "4K"),
                new EntityThumbnailMeta(EntityThumbnailMetaIcons.Video, "H264"),
                new EntityThumbnailMeta(EntityThumbnailMetaIcons.Video, "MATROSKA")
            ],
            Assert.Single(response.Items).Meta);
    }

    [Fact]
    public async Task GetThumbnailsAsyncSurfacesReferenceCountsAndChipsForTaxonomy() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var tagId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
        var videoA = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
        var videoB = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
        var galleryC = Guid.Parse("cccccccc-0000-0000-0000-000000000004");
        var hiddenVideo = Guid.Parse("cccccccc-0000-0000-0000-000000000005");
        var wantedGallery = Guid.Parse("cccccccc-0000-0000-0000-000000000006");

        db.Entities.AddRange(
            new EntityRow { Id = tagId, KindCode = EntityKind.Tag.ToCode(), Title = "Noir", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = videoA, KindCode = EntityKind.Video.ToCode(), Title = "A", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = videoB, KindCode = EntityKind.Video.ToCode(), Title = "B", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = galleryC, KindCode = EntityKind.Gallery.ToCode(), Title = "C", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = hiddenVideo, KindCode = EntityKind.Video.ToCode(), Title = "Hidden", IsNsfw = true, CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = wantedGallery, KindCode = EntityKind.Gallery.ToCode(), Title = "Wanted", IsWanted = true, CreatedAt = now, UpdatedAt = now });
        db.EntityRelationshipLinks.AddRange(
            Link(videoA, tagId, "tags", now),
            // Same source under a second relationship code must still count once.
            Link(videoA, tagId, "featured", now),
            Link(videoB, tagId, "tags", now),
            Link(galleryC, tagId, "tags", now),
            Link(hiddenVideo, tagId, "tags", now),
            Link(wantedGallery, tagId, "tags", now));
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var response = await service.GetThumbnailsAsync([tagId, videoA], hideNsfw: true, CancellationToken.None);
        var tag = response.Items.Single(item => item.Id == tagId);
        var video = response.Items.Single(item => item.Id == videoA);

        // Distinct sources per kind, ordered by count descending.
        Assert.NotNull(tag.ReferenceCounts);
        Assert.Equal(
            new[] { (EntityKind.Video, 2), (EntityKind.Gallery, 1) },
            tag.ReferenceCounts!.Select(count => (count.Kind, count.Count)).ToArray());
        // Count chips derive from the same data using kind-mapped icons.
        Assert.Contains(new EntityThumbnailMeta(EntityThumbnailMetaIcons.Video, "2"), tag.Meta);
        Assert.Contains(new EntityThumbnailMeta(EntityThumbnailMetaIcons.Gallery, "1"), tag.Meta);
        // Media kinds carry no inbound-reference concept.
        Assert.Null(video.ReferenceCounts);

        static EntityRelationshipLinkRow Link(Guid source, Guid target, string code, DateTimeOffset at) =>
            new() {
                EntityId = source,
                RelationshipCode = code,
                Label = code,
                TargetEntityId = target,
                TargetKindCode = EntityKind.Tag.ToCode(),
                CreatedAt = at,
            };
    }

    [Fact]
    public async Task GetThumbnailsAsyncUsesAlbumCoverForAudioTrackWithoutOwnCover() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var albumId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var trackId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

        db.Entities.AddRange(
            new EntityRow {
                Id = albumId,
                KindCode = EntityKind.AudioLibrary.ToCode(),
                Title = "Album",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = trackId,
                KindCode = EntityKind.AudioTrack.ToCode(),
                Title = "Track",
                ParentEntityId = albumId,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.EntityLibraryRoots.Add(new EntityLibraryRootRow { EntityId = albumId });
        db.AudioTrackDetails.Add(new AudioTrackDetailRow { EntityId = trackId });
        db.EntityFiles.Add(File(albumId, EntityFileRole.Cover, "/assets/audio-libraries/album/cover.jpg", now));
        await db.SaveChangesAsync();

        var response = await CreateService(db).GetThumbnailsAsync([trackId], hideNsfw: false, CancellationToken.None);

        var track = Assert.Single(response.Items);
        Assert.Equal("/assets/audio-libraries/album/cover.jpg", track.CoverUrl);
    }

    [Fact]
    public async Task GetAsyncUsesAlbumCoverForAudioTrackWithoutOwnCover() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var albumId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000031");
        var trackId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000032");

        db.Entities.AddRange(
            new EntityRow {
                Id = albumId,
                KindCode = EntityKind.AudioLibrary.ToCode(),
                Title = "Album",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = trackId,
                KindCode = EntityKind.AudioTrack.ToCode(),
                Title = "Track",
                ParentEntityId = albumId,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.EntityLibraryRoots.Add(new EntityLibraryRootRow { EntityId = albumId });
        db.AudioTrackDetails.Add(new AudioTrackDetailRow { EntityId = trackId });
        db.EntityFiles.Add(File(albumId, EntityFileRole.Cover, "/assets/audio-libraries/album/cover.jpg", now));
        await db.SaveChangesAsync();

        var detail = Assert.IsType<EntityCard>(
            await CreateService(db).GetAsync(trackId, hideNsfw: false, CancellationToken.None));

        var images = Assert.IsType<ImagesCapability>(Assert.Single(detail.Capabilities.OfType<ImagesCapability>()));
        Assert.Equal("/assets/audio-libraries/album/cover.jpg", images.CoverUrl);
        Assert.Equal("/assets/audio-libraries/album/cover.jpg", images.ThumbnailUrl);
        Assert.Empty(images.Items);
    }

    [Fact]
    public async Task GetAsyncUsesDefinitionApprovedParentCoverForVideoEpisode() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var seriesId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000041");
        var episodeId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000042");

        db.Entities.AddRange(
            new EntityRow {
                Id = seriesId,
                KindCode = EntityKind.VideoSeries.ToCode(),
                Title = "Series",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = episodeId,
                KindCode = EntityKind.VideoEpisode.ToCode(),
                Title = "Episode",
                ParentEntityId = seriesId,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.EntityLibraryRoots.Add(new EntityLibraryRootRow { EntityId = seriesId });
        db.EntityFiles.Add(File(seriesId, EntityFileRole.Cover, "/assets/series/show/cover.jpg", now));
        await db.SaveChangesAsync();

        var detail = Assert.IsType<EntityCard>(
            await CreateService(db).GetAsync(episodeId, hideNsfw: false, CancellationToken.None));

        var images = Assert.IsType<ImagesCapability>(Assert.Single(detail.Capabilities.OfType<ImagesCapability>()));
        Assert.Equal("/assets/series/show/cover.jpg", images.CoverUrl);
        Assert.Equal("/assets/series/show/cover.jpg", images.ThumbnailUrl);
        Assert.Empty(images.Items);
    }

    [Fact]
    public async Task GetThumbnailsAsyncKeepsAudioTrackOwnCoverBeforeAlbumCover() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var albumId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000011");
        var trackId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000012");

        db.Entities.AddRange(
            new EntityRow {
                Id = albumId,
                KindCode = EntityKind.AudioLibrary.ToCode(),
                Title = "Album",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = trackId,
                KindCode = EntityKind.AudioTrack.ToCode(),
                Title = "Track",
                ParentEntityId = albumId,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.EntityLibraryRoots.Add(new EntityLibraryRootRow { EntityId = albumId });
        db.AudioTrackDetails.Add(new AudioTrackDetailRow { EntityId = trackId });
        db.EntityFiles.AddRange(
            File(albumId, EntityFileRole.Cover, "/assets/audio-libraries/album/cover.jpg", now),
            File(trackId, EntityFileRole.Cover, "/assets/audio-tracks/track/cover.jpg", now));
        await db.SaveChangesAsync();

        var response = await CreateService(db).GetThumbnailsAsync([trackId], hideNsfw: false, CancellationToken.None);

        var track = Assert.Single(response.Items);
        Assert.Equal("/assets/audio-tracks/track/cover.jpg", track.CoverUrl);
    }

    [Fact]
    public async Task GetThumbnailsAsyncLeavesAudioTrackWithoutAlbumCoverUncovered() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var albumId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000021");
        var trackId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000022");

        db.Entities.AddRange(
            new EntityRow {
                Id = albumId,
                KindCode = EntityKind.AudioLibrary.ToCode(),
                Title = "Album",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = trackId,
                KindCode = EntityKind.AudioTrack.ToCode(),
                Title = "Track",
                ParentEntityId = albumId,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.EntityLibraryRoots.Add(new EntityLibraryRootRow { EntityId = albumId });
        db.AudioTrackDetails.Add(new AudioTrackDetailRow { EntityId = trackId });
        await db.SaveChangesAsync();

        var response = await CreateService(db).GetThumbnailsAsync([trackId], hideNsfw: false, CancellationToken.None);

        var track = Assert.Single(response.Items);
        Assert.Null(track.CoverUrl);
    }

    [Fact]
    public async Task GetThumbnailsAsyncUsesSeasonArtworkVariantsForVideoEpisodeWithoutOwnCover() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var seasonId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000031");
        var episodeId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000032");
        var seasonCover = "/assets/video-seasons/season/cover.jpg";
        var seasonGrid = "/assets/grid-thumbs/season.jpg";
        var seasonGrid2x = "/assets/grid-thumbs/season@2x.jpg";

        db.Entities.AddRange(
            new EntityRow {
                Id = seasonId,
                KindCode = EntityKind.VideoSeason.ToCode(),
                Title = "Season One",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = episodeId,
                KindCode = EntityKind.VideoEpisode.ToCode(),
                Title = "Pilot",
                ParentEntityId = seasonId,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.EntityFiles.AddRange(
            File(seasonId, EntityFileRole.Cover, seasonCover, now),
            File(seasonId, EntityFileRole.GridThumbnail, seasonGrid, now.AddSeconds(1)),
            File(seasonId, EntityFileRole.GridThumbnail2x, seasonGrid2x, now.AddSeconds(2)));
        await db.SaveChangesAsync();

        var response = await CreateService(db).GetThumbnailsAsync([episodeId], hideNsfw: false, CancellationToken.None);

        var episode = Assert.Single(response.Items);
        Assert.Equal("Pilot", episode.Title);
        Assert.Equal("Season One", episode.Subtitle);
        Assert.Equal(EntityKind.VideoSeason, episode.ParentKind);
        Assert.Equal(seasonCover, episode.CoverUrl);
        Assert.Equal(seasonGrid, episode.CoverThumbUrl);
        Assert.Equal(seasonGrid2x, episode.CoverThumb2xUrl);
    }

    [Fact]
    public async Task GetThumbnailsAsyncKeepsVideoEpisodeArtworkBeforeSeasonArtwork() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var seasonId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000041");
        var episodeId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000042");
        var episodeCover = "/assets/video-episodes/pilot/cover.jpg";
        var episodeGrid = "/assets/grid-thumbs/pilot.jpg";
        var episodeGrid2x = "/assets/grid-thumbs/pilot@2x.jpg";

        db.Entities.AddRange(
            new EntityRow {
                Id = seasonId,
                KindCode = EntityKind.VideoSeason.ToCode(),
                Title = "Season One",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = episodeId,
                KindCode = EntityKind.VideoEpisode.ToCode(),
                Title = "Pilot",
                ParentEntityId = seasonId,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.EntityFiles.AddRange(
            File(seasonId, EntityFileRole.Cover, "/assets/video-seasons/season/cover.jpg", now),
            File(seasonId, EntityFileRole.GridThumbnail, "/assets/grid-thumbs/season.jpg", now.AddSeconds(1)),
            File(seasonId, EntityFileRole.GridThumbnail2x, "/assets/grid-thumbs/season@2x.jpg", now.AddSeconds(2)),
            File(episodeId, EntityFileRole.Cover, episodeCover, now.AddSeconds(3)),
            File(episodeId, EntityFileRole.GridThumbnail, episodeGrid, now.AddSeconds(4)),
            File(episodeId, EntityFileRole.GridThumbnail2x, episodeGrid2x, now.AddSeconds(5)));
        await db.SaveChangesAsync();

        var response = await CreateService(db).GetThumbnailsAsync([episodeId], hideNsfw: false, CancellationToken.None);

        var episode = Assert.Single(response.Items);
        Assert.Equal("Season One", episode.Subtitle);
        Assert.Equal(episodeCover, episode.CoverUrl);
        Assert.Equal(episodeGrid, episode.CoverThumbUrl);
        Assert.Equal(episodeGrid2x, episode.CoverThumb2xUrl);
    }

    [Fact]
    public async Task GetThumbnailsAsyncProjectsImmediateParentTitlesForSeriesHierarchy() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var seriesId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000051");
        var seasonId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000052");
        var episodeId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000053");

        db.Entities.AddRange(
            new EntityRow {
                Id = seriesId,
                KindCode = EntityKind.VideoSeries.ToCode(),
                Title = "Series Title",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = seasonId,
                KindCode = EntityKind.VideoSeason.ToCode(),
                Title = "Season One",
                ParentEntityId = seriesId,
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = episodeId,
                KindCode = EntityKind.VideoEpisode.ToCode(),
                Title = "Pilot",
                ParentEntityId = seasonId,
                CreatedAt = now,
                UpdatedAt = now
            });
        await db.SaveChangesAsync();

        var response = await CreateService(db).GetThumbnailsAsync(
            [seasonId, episodeId],
            hideNsfw: false,
            CancellationToken.None);

        Assert.Collection(
            response.Items,
            season => {
                Assert.Equal("Season One", season.Title);
                Assert.Equal("Series Title", season.Subtitle);
                Assert.Equal(EntityKind.VideoSeries, season.ParentKind);
            },
            episode => {
                Assert.Equal("Pilot", episode.Title);
                Assert.Equal("Season One", episode.Subtitle);
                Assert.Equal(EntityKind.VideoSeason, episode.ParentKind);
            });
    }

    [Fact]
    public async Task GetThumbnailsAsyncDoesNotExposeHiddenSeasonParentTitleOrArtwork() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var seasonId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000061");
        var episodeId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000062");

        db.Entities.AddRange(
            new EntityRow {
                Id = seasonId,
                KindCode = EntityKind.VideoSeason.ToCode(),
                Title = "Hidden Season",
                IsNsfw = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = episodeId,
                KindCode = EntityKind.VideoEpisode.ToCode(),
                Title = "Safe Episode",
                ParentEntityId = seasonId,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.EntityFiles.Add(File(seasonId, EntityFileRole.Cover, "/assets/video-seasons/hidden/cover.jpg", now));
        await db.SaveChangesAsync();

        var response = await CreateService(db).GetThumbnailsAsync([episodeId], hideNsfw: true, CancellationToken.None);

        var episode = Assert.Single(response.Items);
        Assert.Null(episode.Subtitle);
        Assert.Null(episode.ParentKind);
        Assert.Null(episode.CoverUrl);
        Assert.Null(episode.CoverThumbUrl);
        Assert.Null(episode.CoverThumb2xUrl);
    }

    [Fact]
    public async Task GetThumbnailsAsyncDoesNotExposeSeasonArtworkForEpisodeInDisabledLibrary() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var rootId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000071");
        var seasonId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000072");
        var episodeId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000073");

        db.LibraryRoots.Add(Root(rootId, enabled: false, now));
        db.Entities.AddRange(
            new EntityRow {
                Id = seasonId,
                KindCode = EntityKind.VideoSeason.ToCode(),
                Title = "Disabled Library Season",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = episodeId,
                KindCode = EntityKind.VideoEpisode.ToCode(),
                Title = "Hidden Episode",
                ParentEntityId = seasonId,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.EntityLibraryRoots.Add(new EntityLibraryRootRow { EntityId = episodeId, LibraryRootId = rootId  });
        db.EntityFiles.Add(File(seasonId, EntityFileRole.Cover, "/assets/video-seasons/disabled/cover.jpg", now));
        await db.SaveChangesAsync();

        var response = await CreateService(db).GetThumbnailsAsync([episodeId], hideNsfw: false, CancellationToken.None);

        Assert.Empty(response.Items);
    }

    private static string CreateCacheRoot() {
        var path = Path.Combine(Path.GetTempPath(), $"prismedia-read-cache-{Guid.NewGuid():N}");
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
        System.IO.File.WriteAllBytes(path, [0xff, 0xd8, 0xff, 0xd9]);
    }

    private static void DeleteDirectory(string path) {
        if (Directory.Exists(path)) {
            Directory.Delete(path, recursive: true);
        }
    }

    private static EntityFileRow File(Guid entityId, EntityFileRole role, string path, DateTimeOffset at) =>
        new() {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Role = role,
            Path = path,
            CreatedAt = at,
            UpdatedAt = at
        };

    private static LibraryRootRow Root(Guid id, bool enabled, DateTimeOffset at) =>
        new() {
            Id = id,
            Path = $"/media/{id}",
            Label = id.ToString(),
            Enabled = enabled,
            CreatedAt = at,
            UpdatedAt = at
        };

    private static EfEntityReadService CreateService(PrismediaDbContext db) {
        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        return new EfEntityReadService(db, TestUserContext.Admin(), repository, ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db));
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
