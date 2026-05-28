using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Videos;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Entities.Mappers;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfEntityReadServiceTests {
    [Fact]
    public async Task GetDetailAsyncProjectsRelationshipCodesAndCastMetadata() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var personId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var studioId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var tagId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow {
                Id = videoId,
                KindCode = EntityKindRegistry.Video.Code,
                Title = "Pilot",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = personId,
                KindCode = EntityKindRegistry.Person.Code,
                Title = "Guest Actor",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = studioId,
                KindCode = EntityKindRegistry.Studio.Code,
                Title = "HBO",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = tagId,
                KindCode = EntityKindRegistry.Tag.Code,
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
                TargetKindCode = EntityKindRegistry.Person.Code,
                SortOrder = 2,
                MetadataJson = """{"role":"guest","character":"Visitor"}""",
                CreatedAt = now
            },
            new EntityRelationshipLinkRow {
                EntityId = videoId,
                RelationshipCode = "studio",
                Label = "Studio",
                TargetEntityId = studioId,
                TargetKindCode = EntityKindRegistry.Studio.Code,
                SortOrder = 0,
                CreatedAt = now
            },
            new EntityRelationshipLinkRow {
                EntityId = videoId,
                RelationshipCode = "tags",
                Label = "Tags",
                TargetEntityId = tagId,
                TargetKindCode = EntityKindRegistry.Tag.Code,
                SortOrder = 1,
                CreatedAt = now
            });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, EntityMappers.Kinds(db), EntityMappers.Capabilities(db));
        var service = new EfEntityReadService(db, repository, EntityMappers.Kinds(db));

        var detail = Assert.IsType<VideoDetail>(
            await service.GetDetailAsync(videoId, EntityKindRegistry.Video.Code, hideNsfw: false, CancellationToken.None));

        var cast = Assert.Single(detail.Relationships, group => group.Code == "cast");
        Assert.Equal(EntityKindRegistry.Person.Code, cast.Kind);
        Assert.Equal(personId, Assert.Single(cast.Entities).Id);
        Assert.Contains(detail.Relationships, group => group.Code == "studio" && group.Kind == EntityKindRegistry.Studio.Code);
        Assert.Contains(detail.Relationships, group => group.Code == "tags" && group.Kind == EntityKindRegistry.Tag.Code);
        var metadata = Assert.Single(detail.CreditMetadata);
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
                KindCode = EntityKindRegistry.Video.Code,
                Title = "Linked Video",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = otherVideoId,
                KindCode = EntityKindRegistry.Video.Code,
                Title = "Unlinked Video",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = relatedVideoId,
                KindCode = EntityKindRegistry.Video.Code,
                Title = "Related Video",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = actorId,
                KindCode = EntityKindRegistry.Person.Code,
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
                TargetKindCode = EntityKindRegistry.Person.Code,
                SortOrder = 0,
                CreatedAt = now
            },
            new EntityRelationshipLinkRow {
                EntityId = relatedVideoId,
                RelationshipCode = "related",
                Label = "Related",
                TargetEntityId = actorId,
                TargetKindCode = EntityKindRegistry.Person.Code,
                SortOrder = 0,
                CreatedAt = now
            });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, EntityMappers.Kinds(db), EntityMappers.Capabilities(db));
        var service = new EfEntityReadService(db, repository, EntityMappers.Kinds(db));

        var result = await service.ListAsync(
            EntityKindRegistry.Video.Code,
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
    public async Task ListAsyncProjectsVideoTrickplayPlaylistAsSpriteHover() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = videoId,
            KindCode = EntityKindRegistry.Video.Code,
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
                Path = "/Videos/44444444-4444-4444-4444-444444444444/Trickplay/320/tiles.m3u8",
                MimeType = "application/vnd.apple.mpegurl",
                CreatedAt = now,
                UpdatedAt = now
            });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, EntityMappers.Kinds(db), EntityMappers.Capabilities(db));
        var service = new EfEntityReadService(db, repository, EntityMappers.Kinds(db));

        var result = await service.ListAsync(EntityKindRegistry.Video.Code, null, null, null, null, CancellationToken.None);
        var item = Assert.Single(result.Items);

        Assert.Equal("sprite", item.HoverKind);
        Assert.Equal("/Videos/44444444-4444-4444-4444-444444444444/Trickplay/320/tiles.m3u8", item.HoverUrl);
    }

    [Fact]
    public async Task ListAsyncPrefersCustomArtworkOverGeneratedThumbnails() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("15151515-1515-1515-1515-151515151515");
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = videoId,
            KindCode = EntityKindRegistry.Video.Code,
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

        var repository = new EfEntityRepository(db, EntityMappers.Kinds(db), EntityMappers.Capabilities(db));
        var service = new EfEntityReadService(db, repository, EntityMappers.Kinds(db));

        var result = await service.ListAsync(EntityKindRegistry.Video.Code, null, null, null, null, CancellationToken.None);
        var item = Assert.Single(result.Items);

        Assert.Equal("/assets/custom/artwork/15151515/poster.webp", item.CoverUrl);
    }

    [Fact]
    public async Task ListAsyncPrefersLogoOverBackdropForThumbnailCover() {
        await using var db = CreateContext();
        var studioId = Guid.Parse("16161616-1616-1616-1616-161616161616");
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = studioId,
            KindCode = EntityKindRegistry.Studio.Code,
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

        var repository = new EfEntityRepository(db, EntityMappers.Kinds(db), EntityMappers.Capabilities(db));
        var service = new EfEntityReadService(db, repository, EntityMappers.Kinds(db));

        var result = await service.ListAsync(EntityKindRegistry.Studio.Code, null, null, null, null, CancellationToken.None);
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
            KindCode = EntityKindRegistry.Video.Code,
            Title = "Probed Video",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.EntityTechnical.Add(new EntityTechnicalRow {
            EntityId = videoId,
            DurationSeconds = 596,
            Width = 1920,
            Height = 1080,
            BitRate = 8_000_000,
            Codec = "h264",
            Container = "matroska",
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, EntityMappers.Kinds(db), EntityMappers.Capabilities(db));
        var service = new EfEntityReadService(db, repository, EntityMappers.Kinds(db));

        var result = await service.ListAsync(EntityKindRegistry.Video.Code, null, null, null, null, CancellationToken.None);
        var item = Assert.Single(result.Items);

        Assert.Equal(
            [
                new EntityThumbnailMeta("duration", "09:56"),
                new EntityThumbnailMeta("video", "1080p"),
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
                KindCode = EntityKindRegistry.Gallery.Code,
                Title = "Gallery",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = subgalleryId,
                KindCode = EntityKindRegistry.Gallery.Code,
                Title = "A secondGallery",
                ParentEntityId = galleryId,
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now
            });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, EntityMappers.Kinds(db), EntityMappers.Capabilities(db));
        var service = new EfEntityReadService(db, repository, EntityMappers.Kinds(db));

        var result = await service.ListAsync(EntityKindRegistry.Gallery.Code, null, null, null, null, CancellationToken.None);
        var item = Assert.Single(result.Items);

        Assert.Equal(galleryId, item.Id);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task ListAsyncReturnsOnlyTopLevelAudioLibrariesForAudioBrowse() {
        await using var db = CreateContext();
        var libraryId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var nestedLibraryId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow {
                Id = libraryId,
                KindCode = EntityKindRegistry.AudioLibrary.Code,
                Title = "Album",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = nestedLibraryId,
                KindCode = EntityKindRegistry.AudioLibrary.Code,
                Title = "Disc 2",
                ParentEntityId = libraryId,
                SortOrder = 1,
                CreatedAt = now,
                UpdatedAt = now
            });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, EntityMappers.Kinds(db), EntityMappers.Capabilities(db));
        var service = new EfEntityReadService(db, repository, EntityMappers.Kinds(db));

        var result = await service.ListAsync(EntityKindRegistry.AudioLibrary.Code, null, null, null, null, CancellationToken.None);
        var item = Assert.Single(result.Items);

        Assert.Equal(libraryId, item.Id);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task ListAsyncProjectsRepresentativeChildHoverImages() {
        await using var db = CreateContext();
        var bookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = bookId,
            KindCode = EntityKindRegistry.Book.Code,
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
                    KindCode = EntityKindRegistry.BookVolume.Code,
                    Title = $"Volume {index + 1:00}",
                    ParentEntityId = bookId,
                    SortOrder = index,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new EntityRow {
                    Id = chapterId,
                    KindCode = EntityKindRegistry.BookChapter.Code,
                    Title = $"Chapter {index + 1:00}",
                    ParentEntityId = volumeId,
                    SortOrder = 0,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new EntityRow {
                    Id = pageId,
                    KindCode = EntityKindRegistry.BookPage.Code,
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

        var repository = new EfEntityRepository(db, EntityMappers.Kinds(db), EntityMappers.Capabilities(db));
        var service = new EfEntityReadService(db, repository, EntityMappers.Kinds(db));

        var result = await service.ListAsync(EntityKindRegistry.Book.Code, null, null, null, null, CancellationToken.None);
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
                KindCode = EntityKindRegistry.Book.Code,
                Title = "No Custom Cover",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = chapterId,
                KindCode = EntityKindRegistry.BookChapter.Code,
                Title = "Chapter 1",
                ParentEntityId = bookId,
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = pageId,
                KindCode = EntityKindRegistry.BookPage.Code,
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

        var repository = new EfEntityRepository(db, EntityMappers.Kinds(db), EntityMappers.Capabilities(db));
        var service = new EfEntityReadService(db, repository, EntityMappers.Kinds(db));

        var result = await service.ListAsync(EntityKindRegistry.Book.Code, null, null, null, null, CancellationToken.None);
        var item = Assert.Single(result.Items);

        Assert.Equal("/assets/book-pages/page-1.jpg", item.CoverUrl);
        Assert.Equal("/assets/book-pages/page-1.jpg", Assert.Single(item.HoverImages).Path);
    }

    [Fact]
    public async Task GetDetailAsyncProjectsOnlyDirectChildren() {
        await using var db = CreateContext();
        var bookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var volumeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var chapterId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var pageId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow {
                Id = bookId,
                KindCode = EntityKindRegistry.Book.Code,
                Title = "The Promised Neverland",
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
                Title = "Chapter 1",
                ParentEntityId = volumeId,
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = pageId,
                KindCode = EntityKindRegistry.BookPage.Code,
                Title = "001",
                ParentEntityId = chapterId,
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.BookDetails.Add(new BookDetailRow { EntityId = bookId });
        db.BookChapterDetails.Add(new BookChapterDetailRow { EntityId = chapterId });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, EntityMappers.Kinds(db), EntityMappers.Capabilities(db));
        var service = new EfEntityReadService(db, repository, EntityMappers.Kinds(db));

        var detail = Assert.IsAssignableFrom<IEntityCard>(
            await service.GetDetailAsync(bookId, EntityKindRegistry.Book.Code, hideNsfw: false, CancellationToken.None));

        var volumes = Assert.Single(detail.ChildrenByKind);
        Assert.Equal(EntityKindRegistry.BookVolume.Code, volumes.Kind);
        Assert.Equal(volumeId, Assert.Single(volumes.Entities).Id);
        Assert.DoesNotContain(detail.ChildrenByKind, group => group.Kind == EntityKindRegistry.BookChapter.Code);
        Assert.DoesNotContain(detail.ChildrenByKind, group => group.Kind == EntityKindRegistry.BookPage.Code);
    }

    [Fact]
    public async Task GetAsyncProjectsOnlyDirectChildren() {
        await using var db = CreateContext();
        var volumeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var chapterId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var pageId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow {
                Id = volumeId,
                KindCode = EntityKindRegistry.BookVolume.Code,
                Title = "Volume 01",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = chapterId,
                KindCode = EntityKindRegistry.BookChapter.Code,
                Title = "Chapter 1",
                ParentEntityId = volumeId,
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = pageId,
                KindCode = EntityKindRegistry.BookPage.Code,
                Title = "001",
                ParentEntityId = chapterId,
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.BookChapterDetails.Add(new BookChapterDetailRow { EntityId = chapterId });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, EntityMappers.Kinds(db), EntityMappers.Capabilities(db));
        var service = new EfEntityReadService(db, repository, EntityMappers.Kinds(db));

        var card = Assert.IsType<EntityCard>(await service.GetAsync(volumeId, hideNsfw: false, CancellationToken.None));

        var chapters = Assert.Single(card.ChildrenByKind);
        Assert.Equal(EntityKindRegistry.BookChapter.Code, chapters.Kind);
        Assert.Equal(chapterId, Assert.Single(chapters.Entities).Id);
        Assert.DoesNotContain(card.ChildrenByKind, group => group.Kind == EntityKindRegistry.BookPage.Code);
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
                KindCode = EntityKindRegistry.Book.Code,
                Title = "Shared Book",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = safeChapterId,
                KindCode = EntityKindRegistry.BookChapter.Code,
                Title = "Safe Chapter",
                ParentEntityId = bookId,
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = nsfwChapterId,
                KindCode = EntityKindRegistry.BookChapter.Code,
                Title = "Hidden Chapter",
                ParentEntityId = bookId,
                SortOrder = 1,
                IsNsfw = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, EntityMappers.Kinds(db), EntityMappers.Capabilities(db));
        var service = new EfEntityReadService(db, repository, EntityMappers.Kinds(db));

        var hidden = await service.GetAsync(nsfwChapterId, hideNsfw: true, CancellationToken.None);
        var parent = Assert.IsType<EntityCard>(await service.GetAsync(bookId, hideNsfw: true, CancellationToken.None));

        Assert.Null(hidden);
        var children = Assert.Single(parent.ChildrenByKind);
        Assert.Equal(safeChapterId, Assert.Single(children.Entities).Id);
    }

    [Fact]
    public async Task GetDetailAsyncOmitsBookProgressWhenCurrentChapterIsHidden() {
        await using var db = CreateContext();
        var bookId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var chapterId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow {
                Id = bookId,
                KindCode = EntityKindRegistry.Book.Code,
                Title = "Visible Book",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = chapterId,
                KindCode = EntityKindRegistry.BookChapter.Code,
                Title = "Hidden Chapter",
                ParentEntityId = bookId,
                SortOrder = 0,
                IsNsfw = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.EntityProgress.Add(new EntityProgressRow {
            EntityId = bookId,
            CurrentEntityId = chapterId,
            Unit = "page",
            Index = 5,
            Total = 30,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, EntityMappers.Kinds(db), EntityMappers.Capabilities(db));
        var service = new EfEntityReadService(db, repository, EntityMappers.Kinds(db));

        var detail = Assert.IsAssignableFrom<IEntityCard>(
            await service.GetDetailAsync(bookId, EntityKindRegistry.Book.Code, hideNsfw: true, CancellationToken.None));

        Assert.DoesNotContain(detail.Capabilities, capability => capability is ProgressCapability);
        Assert.Empty(detail.ChildrenByKind);
    }

    [Fact]
    public async Task GetDetailAsyncFiltersHiddenRelationshipsWhenRequested() {
        await using var db = CreateContext();
        var videoId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var safePersonId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var nsfwPersonId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow {
                Id = videoId,
                KindCode = EntityKindRegistry.Video.Code,
                Title = "Feature",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = safePersonId,
                KindCode = EntityKindRegistry.Person.Code,
                Title = "Safe Person",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = nsfwPersonId,
                KindCode = EntityKindRegistry.Person.Code,
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
                TargetKindCode = EntityKindRegistry.Person.Code,
                SortOrder = 0,
                CreatedAt = now
            },
            new EntityRelationshipLinkRow {
                EntityId = videoId,
                RelationshipCode = "cast",
                Label = "Cast",
                TargetEntityId = nsfwPersonId,
                TargetKindCode = EntityKindRegistry.Person.Code,
                SortOrder = 1,
                CreatedAt = now
            });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, EntityMappers.Kinds(db), EntityMappers.Capabilities(db));
        var service = new EfEntityReadService(db, repository, EntityMappers.Kinds(db));

        var detail = Assert.IsType<VideoDetail>(
            await service.GetDetailAsync(videoId, EntityKindRegistry.Video.Code, hideNsfw: true, CancellationToken.None));

        var cast = Assert.Single(detail.Relationships, group => group.Code == "cast");
        Assert.Equal(safePersonId, Assert.Single(cast.Entities).Id);
        Assert.DoesNotContain(detail.CreditMetadata, credit => credit.PersonId == nsfwPersonId);
    }

    [Fact]
    public async Task ListAsyncReturnsUnboundedTotalCountForFilteredEntities() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 5; index++) {
            db.Entities.Add(new EntityRow {
                Id = Guid.NewGuid(),
                KindCode = EntityKindRegistry.Video.Code,
                Title = $"Video {index:00}",
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        // A non-matching entity must not be counted in the kind-scoped total.
        db.Entities.Add(new EntityRow {
            Id = Guid.NewGuid(),
            KindCode = EntityKindRegistry.Image.Code,
            Title = "Stray image",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, EntityMappers.Kinds(db), EntityMappers.Capabilities(db));
        var service = new EfEntityReadService(db, repository, EntityMappers.Kinds(db));

        var firstPage = await service.ListAsync(
            EntityKindRegistry.Video.Code, query: null, cursor: null, hideNsfw: null, limit: 2, CancellationToken.None);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.NotNull(firstPage.NextCursor);
        Assert.Equal(5, firstPage.TotalCount);

        var secondPage = await service.ListAsync(
            EntityKindRegistry.Video.Code, query: null, cursor: firstPage.NextCursor, hideNsfw: null, limit: 2, CancellationToken.None);
        Assert.Equal(2, secondPage.Items.Count);
        // Total is independent of cursor position — it always reflects the full filter match.
        Assert.Equal(5, secondPage.TotalCount);
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
