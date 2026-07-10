using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;
using DomainEntityExternalId = Prismedia.Domain.Entities.EntityExternalId;

namespace Prismedia.Infrastructure.Tests;

public sealed class EntityMetadataApplyServiceTests {
    [Fact]
    public async Task ApplyPatchUpdatesEditableEntityMetadataAndCanClearNullableFields() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("18181818-1818-1818-1818-181818181818");
        SeedEntity(db, entityId, "video", "Old Title");
        db.EntityDescriptions.Add(new EntityDescriptionRow {
            EntityId = entityId,
            Value = "Old description",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.EntityUrls.Add(new EntityUrlRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Url = "https://old.example.test",
            SortOrder = 0,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        db.Users.Add(new UserRow {
            Id = TestUserContext.UserId,
            Username = "admin",
            NormalizedUsername = "admin",
            DisplayName = "Admin",
            Role = UserRole.Admin,
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(Path.GetTempPath()));
        var applied = await service.ApplyPatchAsync(
            entityId,
            new EntityMetadataUpdateRequest(
                Fields: ["title", "description", "urls", "flags"],
                Patch: EmptyPatch() with {
                    Title = "New Title",
                    Description = null,
                    Urls = ["https://new.example.test"],
                    Flags = new EntityMetadataFlagsPatch(IsFavorite: true, IsNsfw: false, IsOrganized: true)
                }),
            CancellationToken.None);

        Assert.True(applied);
        Assert.Equal("New Title", (await db.Entities.FindAsync([entityId]))?.Title);
        Assert.Null(await db.EntityDescriptions.FindAsync([entityId]));
        Assert.Equal("https://new.example.test", await db.EntityUrls.Where(row => row.EntityId == entityId).Select(row => row.Url).SingleAsync());
        var entityRow = await db.Entities.FindAsync([entityId]);
        Assert.False(entityRow?.IsNsfw);
        Assert.True(entityRow?.IsOrganized);
        // Imported favorites are applied to admin accounts' own state rows.
        var adminState = await db.UserEntityStates.SingleAsync(state => state.EntityId == entityId);
        Assert.Equal(TestUserContext.UserId, adminState.UserId);
        Assert.True(adminState.IsFavorite);
        Assert.Null(adminState.RatingValue);
    }

    [Fact]
    public async Task ApplyPatchIgnoresRatingFieldAndPreservesUserRating() {
        await using var db = CreateContext();
        var ratedId = Guid.Parse("41414141-4141-4141-4141-414141414141");
        var unratedId = Guid.Parse("42424242-4242-4242-4242-424242424242");
        SeedEntity(db, ratedId, "video", "Rated");
        SeedEntity(db, unratedId, "video", "Unrated");
        db.UserEntityStates.Add(new UserEntityStateRow {
            UserId = TestUserContext.UserId, EntityId = ratedId, RatingValue = 3, UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var service = new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(Path.GetTempPath()));

        Assert.True(await service.ApplyPatchAsync(
            ratedId,
            new EntityMetadataUpdateRequest(
                Fields: ["rating"],
                Patch: EmptyPatch() with { Rating = 99 }),
            CancellationToken.None));
        Assert.True(await service.ApplyPatchAsync(
            unratedId,
            new EntityMetadataUpdateRequest(
                Fields: ["rating"],
                Patch: EmptyPatch() with { Rating = 4 }),
            CancellationToken.None));

        Assert.Equal(3, db.UserEntityStates.Single(state => state.EntityId == ratedId).RatingValue);
        Assert.False(db.UserEntityStates.Any(state => state.EntityId == unratedId));
    }

    [Fact]
    public async Task ApplyProposalIgnoresSelectedRatingFieldAndPreservesUserRating() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("43434343-4343-4343-4343-434343434343");
        SeedEntity(db, entityId, "movie", "Movie");
        db.UserEntityStates.Add(new UserEntityStateRow {
            UserId = TestUserContext.UserId, EntityId = entityId, RatingValue = 2, UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var service = new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(Path.GetTempPath()));
        var applied = await service.ApplyAsync(
            entityId,
            new EntityMetadataProposal(
                ProposalId: "proposal",
                Provider: "test",
                TargetKind: ProposalKind.Movie,
                Confidence: 1,
                MatchReason: "exact",
                Patch: EmptyPatch() with { Title = "Provider Title", Rating = 5 },
                Images: [],
                Children: [],
                Candidates: [],
                Relationships: []),
            selectedFields: ["title", "rating"],
            selectedImages: null,
            CancellationToken.None);

        var entity = await db.Entities.FindAsync([entityId]);
        Assert.True(applied);
        Assert.Equal("Provider Title", entity?.Title);
        Assert.Equal(2, db.UserEntityStates.Single(state => state.EntityId == entityId).RatingValue);
    }

    [Fact]
    public async Task ApplyPatchReplacesIncludedMapsAndLeavesOmittedFieldsUnchanged() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("19191919-1919-1919-1919-191919191919");
        SeedEntity(db, entityId, "video", "Keep Title");
        db.EntityDates.Add(new EntityDateRow {
            EntityId = entityId,
            Code = "released",
            Value = "2020-01-01",
            SortableValue = new DateOnly(2020, 1, 1),
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.EntityStats.Add(new EntityStatRow {
            EntityId = entityId,
            Code = "runtime",
            Value = 90,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.EntityPositions.Add(new EntityPositionRow {
            EntityId = entityId,
            Code = "episode",
            Value = 1,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.EntityClassifications.Add(new EntityClassificationRow {
            EntityId = entityId,
            Value = "old",
            System = null,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(Path.GetTempPath()));
        var applied = await service.ApplyPatchAsync(
            entityId,
            new EntityMetadataUpdateRequest(
                Fields: ["dates", "stats", "positions", "classification"],
                Patch: EmptyPatch() with {
                    Dates = new Dictionary<string, string> { ["aired"] = "2026-05-21" },
                    Stats = new Dictionary<string, int> { ["votes"] = 12 },
                    Positions = new Dictionary<string, int> { ["season"] = 2 },
                    Classification = "episode"
                }),
            CancellationToken.None);

        Assert.True(applied);
        Assert.Equal("Keep Title", (await db.Entities.FindAsync([entityId]))?.Title);
        Assert.Null(await db.EntityDates.FindAsync([entityId, "released"]));
        Assert.Equal("2026-05-21", (await db.EntityDates.FindAsync([entityId, "aired"]))?.Value);
        Assert.Null(await db.EntityStats.FindAsync([entityId, "runtime"]));
        Assert.Equal(12, (await db.EntityStats.FindAsync([entityId, "votes"]))?.Value);
        Assert.Null(await db.EntityPositions.FindAsync([entityId, "episode"]));
        Assert.Equal(2, (await db.EntityPositions.FindAsync([entityId, "season"]))?.Value);
        Assert.Equal("episode", (await db.EntityClassifications.FindAsync([entityId]))?.Value);
    }

    [Fact]
    public async Task ApplyPatchUpdatesResentDateStatAndPositionCodesInsteadOfDeletingThem() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("21212121-2121-2121-2121-212121212121");
        SeedEntity(db, entityId, "video", "Video");
        db.EntityDates.Add(new EntityDateRow {
            EntityId = entityId,
            Code = "released",
            Value = "2020-01-01",
            SortableValue = new DateOnly(2020, 1, 1),
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.EntityStats.Add(new EntityStatRow {
            EntityId = entityId,
            Code = "runtimeMinutes",
            Value = 90,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.EntityPositions.Add(new EntityPositionRow {
            EntityId = entityId,
            Code = "episode",
            Value = 1,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(Path.GetTempPath()));
        var applied = await service.ApplyPatchAsync(
            entityId,
            new EntityMetadataUpdateRequest(
                Fields: ["dates", "stats", "positions"],
                Patch: EmptyPatch() with {
                    Dates = new Dictionary<string, string> { ["released"] = "2021-02-03" },
                    Stats = new Dictionary<string, int> { ["runtimeMinutes"] = 100 },
                    Positions = new Dictionary<string, int> { ["episode"] = 2 }
                }),
            CancellationToken.None);

        Assert.True(applied);
        Assert.Equal("2021-02-03", (await db.EntityDates.FindAsync([entityId, "released"]))?.Value);
        Assert.Equal(100, (await db.EntityStats.FindAsync([entityId, "runtimeMinutes"]))?.Value);
        Assert.Equal(2, (await db.EntityPositions.FindAsync([entityId, "episode"]))?.Value);
    }

    [Fact]
    public async Task ApplyStoresPartialAndTimestampDatesWithSortableValueAndPrecision() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        SeedEntity(db, entityId, "video", "Video");
        await db.SaveChangesAsync();

        var proposal = new EntityMetadataProposal(
            ProposalId: "youtube:video:abc",
            Provider: "youtube",
            TargetKind: ProposalKind.Video,
            Confidence: 1,
            MatchReason: "external-id",
            Patch: EmptyPatch() with {
                Dates = new Dictionary<string, string> {
                    ["released"] = "2025",
                    ["aired"] = "2024-07",
                    ["published"] = "2021-05-29T13:00:12-07:00"
                }
            },
            Images: [],
            Children: [],
            Candidates: []);

        var service = new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(Path.GetTempPath()));
        await service.ApplyAsync(entityId, proposal, ["dates"], selectedImages: null, CancellationToken.None);

        var released = await db.EntityDates.FindAsync([entityId, "released"]);
        Assert.Equal("2025", released?.Value);
        Assert.Equal(new DateOnly(2025, 1, 1), released?.SortableValue);
        Assert.Equal("year", released?.Precision);

        var aired = await db.EntityDates.FindAsync([entityId, "aired"]);
        Assert.Equal("2024-07", aired?.Value);
        Assert.Equal(new DateOnly(2024, 7, 1), aired?.SortableValue);
        Assert.Equal("month", aired?.Precision);

        var published = await db.EntityDates.FindAsync([entityId, "published"]);
        Assert.Equal("2021-05-29", published?.Value);
        Assert.Equal(new DateOnly(2021, 5, 29), published?.SortableValue);
        Assert.Equal("day", published?.Precision);
    }

    [Fact]
    public void PatchValidatorAcceptsPartialAndTimestampDates() {
        EntityMetadataPatchValidator.Validate(
            EntityMetadataPatchValidator.NormalizeFieldSet(["dates"]),
            EmptyPatch() with {
                Dates = new Dictionary<string, string> {
                    ["released"] = "2025",
                    ["aired"] = "2024-07",
                    ["published"] = "2021-05-29T13:00:12-07:00"
                }
            });
    }

    [Fact]
    public async Task ApplyPatchRejectsInvalidTitleRatingAndUrls() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("20202020-2020-2020-2020-202020202020");
        SeedEntity(db, entityId, "video", "Video");
        await db.SaveChangesAsync();

        var service = new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(Path.GetTempPath()));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.ApplyPatchAsync(
            entityId,
            new EntityMetadataUpdateRequest(
                Fields: ["title", "rating", "urls"],
                Patch: EmptyPatch() with {
                    Title = " ",
                    Rating = 6,
                    Urls = ["not-a-url"]
                }),
            CancellationToken.None));

        Assert.Contains("title", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Video", (await db.Entities.FindAsync([entityId]))?.Title);
    }

    [Fact]
    public void PatchValidatorNormalizesFieldsAndRejectsInvalidDates() {
        var fields = EntityMetadataPatchValidator.NormalizeFieldSet([" dates ", "", "DATES"]);

        var exception = Assert.Throws<ArgumentException>(() =>
            EntityMetadataPatchValidator.Validate(
                fields,
                EmptyPatch() with {
                    Dates = new Dictionary<string, string> { ["released"] = "not-a-date" }
                }));

        Assert.Contains("released", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyPatchReturnsFalseForMissingEntity() {
        await using var db = CreateContext();
        var service = new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(Path.GetTempPath()));

        var applied = await service.ApplyPatchAsync(
            Guid.Parse("21212121-2121-2121-2121-212121212121"),
            new EntityMetadataUpdateRequest(Fields: ["title"], Patch: EmptyPatch() with { Title = "Missing" }),
            CancellationToken.None);

        Assert.False(applied);
    }

    [Fact]
    public async Task ApplyPatchRejectsKindMismatchWithoutMutatingEntity() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("23232323-2323-2323-2323-232323232323");
        SeedEntity(db, entityId, "video", "Original Title");
        await db.SaveChangesAsync();

        var service = new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(Path.GetTempPath()));

        var result = await service.ApplyPatchAsync(
            entityId,
            new EntityMetadataUpdateRequest(Fields: ["title"], Patch: EmptyPatch() with { Title = "Wrong Kind" }),
            expectedKind: "video-series",
            CancellationToken.None);

        Assert.Equal(EntityMetadataPatchResult.KindMismatch, result);
        Assert.Equal("Original Title", (await db.Entities.FindAsync([entityId]))?.Title);
    }

    [Fact]
    public async Task ApplyMaterializesProviderContainerAndAdoptsFlatChildren() {
        await using var db = CreateContext();
        var bookId = Guid.Parse("23232323-2323-2323-2323-232323232321");
        var chapterOneId = Guid.Parse("23232323-2323-2323-2323-232323232322");
        var chapterTwoId = Guid.Parse("23232323-2323-2323-2323-232323232323");
        SeedEntity(db, bookId, "book", "Flat Book");
        SeedEntity(db, chapterOneId, "book-chapter", "Flat Book Ch.1", parentEntityId: bookId, sortOrder: 0);
        SeedEntity(db, chapterTwoId, "book-chapter", "Flat Book Ch.2", parentEntityId: bookId, sortOrder: 1);
        await db.SaveChangesAsync();

        EntityMetadataProposal Chapter(Guid target, int number) => new(
            ProposalId: $"mangadex:m1:chapter:{number}",
            Provider: "mangadex",
            TargetKind: ProposalKind.BookChapter,
            Confidence: 0.8m,
            MatchReason: "chapter-feed",
            Patch: EmptyPatch() with {
                Title = $"Chapter {number}",
                ExternalIds = new Dictionary<string, string> { ["mangadexChapter"] = $"ch-{number}" },
                Positions = new Dictionary<string, int> { ["sortOrder"] = number - 1 }
            },
            Images: [],
            Children: [],
            Candidates: [],
            TargetEntityId: target);

        var volume = new EntityMetadataProposal(
            ProposalId: "mangadex:m1:volume:1",
            Provider: "mangadex",
            TargetKind: ProposalKind.BookVolume,
            Confidence: 0.8m,
            MatchReason: "volume-map",
            Patch: EmptyPatch() with {
                Title = "Volume 1",
                ExternalIds = new Dictionary<string, string> { ["mangadex"] = "m1", ["volume"] = "1" },
                Positions = new Dictionary<string, int> { ["volumeNumber"] = 1 }
            },
            Images: [],
            Children: [Chapter(chapterOneId, 1), Chapter(chapterTwoId, 2)],
            Candidates: []);

        var proposal = new EntityMetadataProposal(
            ProposalId: "mangadex:m1",
            Provider: "mangadex",
            TargetKind: ProposalKind.Book,
            Confidence: 0.9m,
            MatchReason: "external-id",
            Patch: EmptyPatch() with { Title = "Flat Book" },
            Images: [],
            Children: [volume],
            Candidates: [],
            TargetEntityId: bookId);

        var service = new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(Path.GetTempPath()));
        await service.ApplyAsync(bookId, proposal, ["title"], selectedImages: null, CancellationToken.None);

        var volumeRow = await db.Entities.SingleAsync(row => row.KindCode == "book-volume");
        Assert.Equal("Volume 1", volumeRow.Title);
        Assert.Equal(bookId, volumeRow.ParentEntityId);
        Assert.Equal("1", (await db.EntityExternalIds.SingleAsync(row => row.EntityId == volumeRow.Id && row.Provider == "volume")).Value);
        Assert.Equal(volumeRow.Id, (await db.Entities.SingleAsync(row => row.Id == chapterOneId)).ParentEntityId);
        Assert.Equal(volumeRow.Id, (await db.Entities.SingleAsync(row => row.Id == chapterTwoId)).ParentEntityId);
        var chapterIdentity = await db.EntityExternalIds.SingleAsync(row => row.EntityId == chapterOneId);
        Assert.Equal(
            new ExternalIdentity("mangadexChapter", "ch-1"),
            new ExternalIdentity(chapterIdentity.Provider, chapterIdentity.Value));
    }

    [Fact]
    public async Task ApplyDoesNotMaterializeContainersWithoutBoundDescendants() {
        await using var db = CreateContext();
        var bookId = Guid.Parse("24242424-2424-2424-2424-242424242421");
        SeedEntity(db, bookId, "book", "Sparse Book");
        await db.SaveChangesAsync();

        var emptyVolume = new EntityMetadataProposal(
            ProposalId: "mangadex:m2:volume:9",
            Provider: "mangadex",
            TargetKind: ProposalKind.BookVolume,
            Confidence: 0.8m,
            MatchReason: "volume-map",
            Patch: EmptyPatch() with { Title = "Volume 9" },
            Images: [],
            Children: [new EntityMetadataProposal(
                "mangadex:m2:chapter:90", "mangadex", ProposalKind.BookChapter, 0.7m, "chapter-feed",
                EmptyPatch() with { Title = "Chapter 90" }, [], [], [])],
            Candidates: []);

        var proposal = new EntityMetadataProposal(
            ProposalId: "mangadex:m2",
            Provider: "mangadex",
            TargetKind: ProposalKind.Book,
            Confidence: 0.9m,
            MatchReason: "external-id",
            Patch: EmptyPatch() with { Title = "Sparse Book" },
            Images: [],
            Children: [emptyVolume],
            Candidates: [],
            TargetEntityId: bookId);

        var service = new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(Path.GetTempPath()));
        await service.ApplyAsync(bookId, proposal, ["title"], selectedImages: null, CancellationToken.None);

        Assert.False(await db.Entities.AnyAsync(row => row.KindCode == "book-volume"));
        Assert.False(await db.Entities.AnyAsync(row => row.KindCode == "book-chapter"));
    }

    [Fact]
    public async Task ApplyNeverReparentsAcrossTrees() {
        await using var db = CreateContext();
        var bookId = Guid.Parse("25252525-2525-2525-2525-252525252521");
        var otherBookId = Guid.Parse("25252525-2525-2525-2525-252525252522");
        var foreignChapterId = Guid.Parse("25252525-2525-2525-2525-252525252523");
        var localChapterId = Guid.Parse("25252525-2525-2525-2525-252525252524");
        SeedEntity(db, bookId, "book", "Main Book");
        SeedEntity(db, otherBookId, "book", "Other Book");
        SeedEntity(db, foreignChapterId, "book-chapter", "Foreign Chapter", parentEntityId: otherBookId);
        SeedEntity(db, localChapterId, "book-chapter", "Local Chapter", parentEntityId: bookId);
        await db.SaveChangesAsync();

        EntityMetadataProposal Chapter(Guid target, string title) => new(
            $"mangadex:m3:chapter:{target:N}", "mangadex", ProposalKind.BookChapter, 0.8m, "chapter-feed",
            EmptyPatch() with { Title = title }, [], [], [], TargetEntityId: target);

        var volume = new EntityMetadataProposal(
            "mangadex:m3:volume:1", "mangadex", ProposalKind.BookVolume, 0.8m, "volume-map",
            EmptyPatch() with { Title = "Volume 1" }, [],
            [Chapter(foreignChapterId, "Foreign Chapter"), Chapter(localChapterId, "Local Chapter")],
            []);

        var proposal = new EntityMetadataProposal(
            "mangadex:m3", "mangadex", ProposalKind.Book, 0.9m, "external-id",
            EmptyPatch() with { Title = "Main Book" }, [], [volume], [], TargetEntityId: bookId);

        var service = new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(Path.GetTempPath()));
        await service.ApplyAsync(bookId, proposal, ["title"], selectedImages: null, CancellationToken.None);

        var volumeRow = await db.Entities.SingleAsync(row => row.KindCode == "book-volume");
        // The in-tree chapter moves under the new volume; the foreign book's chapter stays put.
        Assert.Equal(volumeRow.Id, (await db.Entities.SingleAsync(row => row.Id == localChapterId)).ParentEntityId);
        Assert.Equal(otherBookId, (await db.Entities.SingleAsync(row => row.Id == foreignChapterId)).ParentEntityId);
    }

    [Fact]
    public async Task ApplySelectedFieldsPersistsProviderIdentityAndCapabilityRows() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        SeedEntity(db, entityId, "video", "Old Title");
        await db.SaveChangesAsync();

        var proposal = new EntityMetadataProposal(
            ProposalId: "tmdb:movie:123",
            Provider: "tmdb",
            TargetKind: ProposalKind.Video,
            Confidence: 1,
            MatchReason: "external-id",
            Patch: new EntityMetadataPatch(
                Title: "New Movie",
                Description: "A better description.",
                ExternalIds: new Dictionary<string, string> { ["tmdb"] = "123" },
                Urls: ["https://www.themoviedb.org/movie/123"],
                Tags: ["Drama", "Mystery"],
                Studio: "Prismedia Pictures",
                Credits: [new CreditPatch("Ada Actor", "person", "Lead", 0)],
                Dates: new Dictionary<string, string> { ["released"] = "2026-05-16" },
                Stats: new Dictionary<string, int> { ["runtime-minutes"] = 90, ["votes"] = 42 },
                Positions: new Dictionary<string, int>(),
                Classification: "movie"),
            Images: [],
            Children: [],
            Candidates: []);

        var service = new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(Path.GetTempPath()));
        await service.ApplyAsync(
            entityId,
            proposal,
            ["title", "description", "externalIds", "urls", "tags", "studio", "credits", "dates", "counters", "stats", "classification"],
            selectedImages: null,
            CancellationToken.None);

        var entity = await db.Entities.SingleAsync(row => row.Id == entityId);
        Assert.Equal("New Movie", entity.Title);
        Assert.Equal("A better description.", (await db.EntityDescriptions.FindAsync([entityId]))?.Value);
        var externalId = await db.EntityExternalIds.SingleAsync();
        Assert.Equal("tmdb", externalId.Provider);
        Assert.Equal("123", externalId.Value);
        Assert.Equal("https://www.themoviedb.org/movie/123", externalId.Url);
        Assert.Equal(["Drama", "Mystery"], await db.Entities
            .Where(row => row.KindCode == "tag")
            .OrderBy(row => row.Title)
            .Select(row => row.Title)
            .ToArrayAsync());
        Assert.Equal("Prismedia Pictures", await db.Entities
            .Where(row => row.KindCode == "studio")
            .Select(row => row.Title)
            .SingleAsync());
        Assert.Equal("Ada Actor", await db.Entities
            .Where(row => row.KindCode == "person")
            .Select(row => row.Title)
            .SingleAsync());
        Assert.Equal("2026-05-16", (await db.EntityDates.FindAsync([entityId, "released"]))?.Value);
        Assert.Equal(90, (await db.EntityStats.FindAsync([entityId, "runtime-minutes"]))?.Value);
        Assert.Equal(42, (await db.EntityStats.FindAsync([entityId, "votes"]))?.Value);
        Assert.Equal("movie", (await db.EntityClassifications.FindAsync([entityId]))?.Value);
    }

    [Fact]
    public async Task ReApplyingRelationshipsReconcilesWithoutDuplicateKeyTrackingError() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("31313131-3131-3131-3131-313131313131");
        SeedEntity(db, entityId, "video", "Movie");
        await db.SaveChangesAsync();

        var proposal = new EntityMetadataProposal(
            ProposalId: "tmdb:movie:7",
            Provider: "tmdb",
            TargetKind: ProposalKind.Video,
            Confidence: 1,
            MatchReason: "external-id",
            Patch: EmptyPatch() with {
                Tags = ["Drama", "Mystery"],
                Studio = "Prismedia Pictures",
                Credits = [new CreditPatch("Ada Actor", "person", "Lead", 0)]
            },
            Images: [],
            Children: [],
            Candidates: []);

        var service = new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(Path.GetTempPath()));
        string[] fields = ["tags", "studio", "credits"];

        await service.ApplyAsync(entityId, proposal, fields, selectedImages: null, CancellationToken.None);
        // Re-applying re-removes and re-adds the same links; previously this threw a duplicate-key
        // change-tracker exception because the removed row and the re-added row share a composite key.
        await service.ApplyAsync(entityId, proposal, fields, selectedImages: null, CancellationToken.None);

        Assert.Equal(1, await db.EntityRelationshipLinks
            .CountAsync(row => row.EntityId == entityId && row.RelationshipCode == RelationshipKind.Cast.ToCode()));
        Assert.Equal(2, await db.EntityRelationshipLinks
            .CountAsync(row => row.EntityId == entityId && row.RelationshipCode == RelationshipKind.Tags.ToCode()));
        Assert.Equal(1, await db.EntityRelationshipLinks
            .CountAsync(row => row.EntityId == entityId && row.RelationshipCode == RelationshipKind.Studio.ToCode()));
    }

    [Fact]
    public async Task ApplyCreditsToMusicArtistPersistsBandMembersOnTheArtist() {
        // A music artist is its own relationship owner: identifying a band (e.g. Imagine Dragons)
        // saves the members as Cast links on the artist so the artist detail surfaces them.
        await using var db = CreateContext();
        var artistId = Guid.Parse("a5715a5e-1d29-4c3f-9b1f-1a2b3c4d5e6f");
        SeedEntity(db, artistId, "music-artist", "Imagine Dragons");
        await db.SaveChangesAsync();

        var proposal = new EntityMetadataProposal(
            ProposalId: "musicbrainz:artist:1",
            Provider: "musicbrainz",
            TargetKind: ProposalKind.MusicArtist,
            Confidence: 1,
            MatchReason: "external-id",
            Patch: EmptyPatch() with {
                Credits =
                [
                    new CreditPatch("Dan Reynolds", "Vocals", null, 0),
                    new CreditPatch("Wayne Sermon", "Guitar", null, 1)
                ]
            },
            Images: [],
            Children: [],
            Candidates: []);

        var service = new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(Path.GetTempPath()));
        await service.ApplyAsync(artistId, proposal, ["credits"], selectedImages: null, CancellationToken.None);

        var memberLinks = await db.EntityRelationshipLinks
            .Where(row => row.EntityId == artistId && row.RelationshipCode == RelationshipKind.Cast.ToCode())
            .OrderBy(row => row.SortOrder)
            .ToArrayAsync();
        Assert.Equal(2, memberLinks.Length);
        Assert.All(memberLinks, link => Assert.Equal("person", link.TargetKindCode));
        Assert.Equal(["Dan Reynolds", "Wayne Sermon"], await db.Entities
            .Where(row => row.KindCode == "person")
            .OrderBy(row => row.Title)
            .Select(row => row.Title)
            .ToArrayAsync());
    }

    [Fact]
    public async Task ApplySeriesCascadePersistsEpisodeMetadataAndCredits() {
        await using var db = CreateContext();
        var seriesId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var seasonId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var episodeId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        SeedEntity(db, seriesId, "video-series", "Old Series");
        SeedEntity(db, seasonId, "video-season", "Old Season", parentEntityId: seriesId, sortOrder: 1);
        SeedEntity(db, episodeId, "video", "Old Episode", parentEntityId: seasonId, sortOrder: 1);
        db.EntityPositions.Add(new EntityPositionRow {
            EntityId = episodeId,
            Code = "episodeNumber",
            Value = 1,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var episodePatch = new EntityMetadataPatch(
            Title: "Pilot",
            Description: "The story starts.",
            ExternalIds: new Dictionary<string, string> { ["tmdb"] = "9001" },
            Urls: ["https://www.themoviedb.org/tv/12/season/1/episode/1"],
            Tags: ["Guest Heavy"],
            Studio: null,
            Credits: [new CreditPatch("Guest Actor", "guest", "Visitor", 3)],
            Dates: new Dictionary<string, string> { ["air"] = "2026-05-16" },
            Stats: new Dictionary<string, int> { ["runtimeMinutes"] = 33, ["voteAverage"] = 8 },
            Positions: new Dictionary<string, int> { ["seasonNumber"] = 1, ["episodeNumber"] = 1 },
            Classification: "episode");
        var proposal = new EntityMetadataProposal(
            ProposalId: "tmdb:tv:12",
            Provider: "tmdb",
            TargetKind: ProposalKind.VideoSeries,
            Confidence: 1,
            MatchReason: "external-id",
            Patch: EmptyPatch(),
            Images: [],
            Children:
            [
                new EntityMetadataProposal(
                    ProposalId: "tmdb:tv:12:season:1",
                    Provider: "tmdb",
                    TargetKind: ProposalKind.VideoSeason,
                    TargetEntityId: seasonId,
                    Confidence: 0.9m,
                    MatchReason: "cascade",
                    Patch: EmptyPatch() with
                    {
                        Title = "Season 1",
                        Positions = new Dictionary<string, int> { ["seasonNumber"] = 1 }
                    },
                    Images: [],
                    Children:
                    [
                        new EntityMetadataProposal(
                            ProposalId: "tmdb:tv:12:s1:e1",
                            Provider: "tmdb",
                            TargetKind: ProposalKind.VideoEpisode,
                            TargetEntityId: episodeId,
                            Confidence: 0.9m,
                            MatchReason: "cascade",
                            Patch: episodePatch,
                            Images: [],
                            Children: [],
                            Candidates: [])
                    ],
                    Candidates: [])
            ],
            Candidates: []);

        var service = new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(Path.GetTempPath()));
        await service.ApplyAsync(
            seriesId,
            proposal,
            selectedFields: ["externalIds"],
            selectedImages: null,
            CancellationToken.None);

        Assert.Equal("Season 1", (await db.Entities.FindAsync([seasonId]))?.Title);
        Assert.Equal("Pilot", (await db.Entities.FindAsync([episodeId]))?.Title);
        Assert.Equal("The story starts.", (await db.EntityDescriptions.FindAsync([episodeId]))?.Value);
        Assert.Equal("9001", (await db.EntityExternalIds.SingleAsync(row => row.EntityId == episodeId)).Value);
        Assert.Equal("https://www.themoviedb.org/tv/12/season/1/episode/1", await db.EntityUrls
            .Where(row => row.EntityId == episodeId)
            .Select(row => row.Url)
            .SingleAsync());
        Assert.Equal("Guest Actor", await db.Entities
            .Where(row => row.KindCode == "person")
            .Select(row => row.Title)
            .SingleAsync());
        Assert.Contains("Visitor", (await db.EntityRelationshipLinks
            .Where(row => row.EntityId == episodeId && row.RelationshipCode == "cast")
            .Select(row => row.MetadataJson)
            .SingleAsync()) ?? string.Empty);
        Assert.Equal("2026-05-16", (await db.EntityDates.FindAsync([episodeId, "air"]))?.Value);
        Assert.Equal(33, (await db.EntityStats.FindAsync([episodeId, "runtimeMinutes"]))?.Value);
        Assert.Equal(8, (await db.EntityStats.FindAsync([episodeId, "voteAverage"]))?.Value);
        Assert.Equal("episode", (await db.EntityClassifications.FindAsync([episodeId]))?.Value);
    }

    [Fact]
    public async Task ApplySeriesCascadeSavesSeasonPosterArtwork() {
        await using var db = CreateContext();
        var seriesId = Guid.Parse("37373737-3737-3737-3737-373737373737");
        var seasonId = Guid.Parse("38383838-3838-3838-3838-383838383838");
        SeedEntity(db, seriesId, "video-series", "Old Series");
        SeedEntity(db, seasonId, "video-season", "Old Season", parentEntityId: seriesId, sortOrder: 1);
        await db.SaveChangesAsync();

        var proposal = new EntityMetadataProposal(
            ProposalId: "tmdb:tv:12",
            Provider: "tmdb",
            TargetKind: ProposalKind.VideoSeries,
            TargetEntityId: seriesId,
            Confidence: 1,
            MatchReason: "external-id",
            Patch: EmptyPatch(),
            Images: [],
            Children:
            [
                new EntityMetadataProposal(
                    ProposalId: "tmdb:tv:12:season:1",
                    Provider: "tmdb",
                    TargetKind: ProposalKind.VideoSeason,
                    TargetEntityId: seasonId,
                    Confidence: 0.9m,
                    MatchReason: "cascade",
                    Patch: EmptyPatch() with { Title = "Season 1" },
                    Images: [new ImageCandidate("poster", "https://example.test/season.jpg", "tmdb", null, null, null, null)],
                    Children: [],
                    Candidates: [])
            ],
            Candidates: []);

        var service = new EntityMetadataApplyService(
            db,
            new PluginArtworkServiceOptions(Path.GetTempPath()),
            new HttpClient(new FixedImageHandler()));
        await service.ApplyAsync(seriesId, proposal, selectedFields: ["externalIds"], selectedImages: null, CancellationToken.None);

        var file = await db.EntityFiles.SingleAsync(row => row.EntityId == seasonId && row.Role == EntityFileRole.Poster);
        Assert.StartsWith($"/assets/plugins/artwork/{seasonId}/poster-", file.Path);
        Assert.EndsWith(".jpg", file.Path);
    }

    [Fact]
    public async Task ApplySelectedImagesSendsUserAgentForProviderArtworkDownloads() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("43434343-4343-4343-4343-434343434343");
        SeedEntity(db, entityId, "book-volume", "Volume 1");
        await db.SaveChangesAsync();

        var imageUrl = "https://uploads.mangadex.org/covers/manga-id/cover.jpg.512.jpg";
        var proposal = new EntityMetadataProposal(
            ProposalId: "mangadex:manga-id:volume:1",
            Provider: "mangadex",
            TargetKind: ProposalKind.BookVolume,
            TargetEntityId: entityId,
            Confidence: 1,
            MatchReason: "volume-map",
            Patch: EmptyPatch(),
            Images: [new ImageCandidate("cover", imageUrl, "MangaDex volume 1", null, null, null, null)],
            Children: [],
            Candidates: []);

        var service = new EntityMetadataApplyService(
            db,
            new PluginArtworkServiceOptions(Path.GetTempPath()),
            new HttpClient(new RequiresUserAgentImageHandler()));
        await service.ApplyAsync(
            entityId,
            proposal,
            selectedFields: ["images"],
            selectedImages: new Dictionary<string, string?> { ["cover"] = imageUrl },
            CancellationToken.None);

        var file = await db.EntityFiles.SingleAsync(row => row.EntityId == entityId && row.Role == EntityFileRole.Cover);
        Assert.StartsWith($"/assets/plugins/artwork/{entityId}/cover-", file.Path);
        Assert.EndsWith(".jpg", file.Path);
    }

    [Fact]
    public async Task ApplyStagesRemoteArtworkBeforeEnteringEntityLifecycleLease() {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        SeedEntity(db, entityId, "book", "Staged artwork");
        await db.SaveChangesAsync();
        var lease = new ObservingLifecycleLease();
        var imageHandler = new LeaseObservingImageHandler(() => lease.InsideLease);
        var cacheRoot = Path.Combine(Path.GetTempPath(), $"prismedia-staged-artwork-{Guid.NewGuid():N}");
        var imageUrl = "https://example.test/staged-cover.jpg";
        var proposal = new EntityMetadataProposal(
            "provider:book:staged",
            "provider",
            ProposalKind.Book,
            1,
            "external-id",
            EmptyPatch(),
            [new ImageCandidate("cover", imageUrl, "provider", null, null, null, null)],
            [],
            [],
            TargetEntityId: entityId);

        try {
            var service = new EntityMetadataApplyService(
                db,
                new PluginArtworkServiceOptions(cacheRoot),
                new HttpClient(imageHandler),
                lifecycle: lease);

            Assert.True(await service.ApplyAsync(
                entityId,
                proposal,
                ["images"],
                new Dictionary<string, string?> { ["cover"] = imageUrl },
                CancellationToken.None));

            Assert.True(imageHandler.WasCalled);
            Assert.False(imageHandler.ObservedInsideLease);
            Assert.False(lease.InsideLease);
        } finally {
            if (Directory.Exists(cacheRoot)) {
                Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ApplySeriesCascadeReplacesExistingSeasonPosterArtwork() {
        await using var db = CreateContext();
        var seriesId = Guid.Parse("39393939-3939-3939-3939-393939393939");
        var seasonId = Guid.Parse("40404040-4040-4040-4040-404040404040");
        SeedEntity(db, seriesId, "video-series", "Old Series");
        SeedEntity(db, seasonId, "video-season", "Old Season", parentEntityId: seriesId, sortOrder: 1);
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = seasonId,
            Role = EntityFileRole.Poster,
            Path = "/assets/plugins/artwork/old-season-poster.jpg",
            MimeType = "image/jpeg",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var proposal = new EntityMetadataProposal(
            ProposalId: "tmdb:tv:12",
            Provider: "tmdb",
            TargetKind: ProposalKind.VideoSeries,
            TargetEntityId: seriesId,
            Confidence: 1,
            MatchReason: "external-id",
            Patch: EmptyPatch(),
            Images: [],
            Children:
            [
                new EntityMetadataProposal(
                    ProposalId: "tmdb:tv:12:season:1",
                    Provider: "tmdb",
                    TargetKind: ProposalKind.VideoSeason,
                    TargetEntityId: seasonId,
                    Confidence: 0.9m,
                    MatchReason: "cascade",
                    Patch: EmptyPatch() with { Title = "Season 1" },
                    Images: [new ImageCandidate("poster", "https://example.test/season-new.jpg", "tmdb", null, null, null, null)],
                    Children: [],
                    Candidates: [])
            ],
            Candidates: []);

        var service = new EntityMetadataApplyService(
            db,
            new PluginArtworkServiceOptions(Path.GetTempPath()),
            new HttpClient(new FixedImageHandler()));
        await service.ApplyAsync(seriesId, proposal, selectedFields: ["externalIds"], selectedImages: null, CancellationToken.None);

        var file = await db.EntityFiles.SingleAsync(row => row.EntityId == seasonId && row.Role == EntityFileRole.Poster);
        Assert.StartsWith($"/assets/plugins/artwork/{seasonId}/poster-", file.Path);
        Assert.EndsWith(".jpg", file.Path);
    }

    [Fact]
    public async Task ApplyProposalChildrenWithTargetEntityIdsRecursesThroughGenericStructure() {
        await using var db = CreateContext();
        var parentId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var childId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var grandchildId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        SeedEntity(db, parentId, "video-series", "Old Series");
        SeedEntity(db, childId, "video-season", "Old Season", parentEntityId: parentId, sortOrder: 1);
        SeedEntity(db, grandchildId, "video", "Old Episode", parentEntityId: childId, sortOrder: 1);
        await db.SaveChangesAsync();

        var proposal = new EntityMetadataProposal(
            ProposalId: "provider:series:1",
            Provider: "provider",
            TargetKind: ProposalKind.VideoSeries,
            TargetEntityId: parentId,
            Confidence: 1,
            MatchReason: "external-id",
            Patch: EmptyPatch() with { Title = "New Series" },
            Images: [],
            Children:
            [
                new EntityMetadataProposal(
                    ProposalId: "provider:season:1",
                    Provider: "provider",
                    TargetKind: ProposalKind.VideoSeason,
                    TargetEntityId: childId,
                    Confidence: 1,
                    MatchReason: "structural-child",
                    Patch: EmptyPatch() with
                    {
                        Title = "New Season",
                        Dates = new Dictionary<string, string> { ["air"] = "2026-01-01" }
                    },
                    Images: [],
                    Children:
                    [
                        new EntityMetadataProposal(
                            ProposalId: "provider:episode:1",
                            Provider: "provider",
                            TargetKind: ProposalKind.Video,
                            TargetEntityId: grandchildId,
                            Confidence: 1,
                            MatchReason: "structural-child",
                            Patch: EmptyPatch() with
                            {
                                Title = "New Episode",
                                Description = "Episode metadata from its own proposal.",
                                Positions = new Dictionary<string, int> { ["episodeNumber"] = 1 }
                            },
                            Images: [],
                            Children: [],
                            Candidates: [])
                    ],
                    Candidates: [])
            ],
            Candidates: []);

        var service = new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(Path.GetTempPath()));
        await service.ApplyAsync(parentId, proposal, selectedFields: ["title"], selectedImages: null, CancellationToken.None);

        Assert.Equal("New Series", (await db.Entities.FindAsync([parentId]))?.Title);
        Assert.Equal("New Season", (await db.Entities.FindAsync([childId]))?.Title);
        Assert.Equal("2026-01-01", (await db.EntityDates.FindAsync([childId, "air"]))?.Value);
        Assert.Equal("New Episode", (await db.Entities.FindAsync([grandchildId]))?.Title);
        Assert.Equal("Episode metadata from its own proposal.", (await db.EntityDescriptions.FindAsync([grandchildId]))?.Value);
        Assert.Equal(1, (await db.EntityPositions.FindAsync([grandchildId, "episode"]))?.Value);
    }

    [Fact]
    public async Task ApplyProposalChildrenWithoutTargetEntityIdsMatchesExistingStructuralBookChildren() {
        await using var db = CreateContext();
        var bookId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var volumeId = Guid.Parse("11111111-2222-3333-4444-555555555556");
        var chapterId = Guid.Parse("11111111-2222-3333-4444-555555555557");
        SeedEntity(db, bookId, "book", "Manga");
        SeedEntity(db, volumeId, "book-volume", "Volume 1", parentEntityId: bookId, sortOrder: 1);
        SeedEntity(db, chapterId, "book-chapter", "Chapter 1", parentEntityId: volumeId, sortOrder: 1);
        await db.SaveChangesAsync();

        var proposal = new EntityMetadataProposal(
            ProposalId: "mangadex:manga-1",
            Provider: "mangadex",
            TargetKind: ProposalKind.Book,
            Confidence: 1,
            MatchReason: "external-id",
            Patch: EmptyPatch(),
            Images: [],
            Children:
            [
                new EntityMetadataProposal(
                    ProposalId: "mangadex:manga-1:volume:1",
                    Provider: "mangadex",
                    TargetKind: ProposalKind.BookVolume,
                    Confidence: 0.8m,
                    MatchReason: "volume-map",
                    Patch: EmptyPatch() with {
                        Title = "Volume 1",
                        ExternalIds = new Dictionary<string, string> { ["mangadex"] = "manga-1", ["volume"] = "1" },
                        Positions = new Dictionary<string, int> { ["volumeNumber"] = 1 }
                    },
                    Images: [],
                    Children:
                    [
                        new EntityMetadataProposal(
                            ProposalId: "mangadex:manga-1:chapter:chapter-1",
                            Provider: "mangadex",
                            TargetKind: ProposalKind.BookChapter,
                            Confidence: 0.7m,
                            MatchReason: "chapter-feed",
                            Patch: EmptyPatch() with {
                                Title = "Chapter 1",
                                ExternalIds = new Dictionary<string, string> { ["mangadexChapter"] = "chapter-1" },
                                Positions = new Dictionary<string, int> { ["chapterNumber"] = 1 }
                            },
                            Images: [],
                            Children: [],
                            Candidates: [])
                    ],
                    Candidates: [])
            ],
            Candidates: []);

        var service = new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(Path.GetTempPath()));
        await service.ApplyAsync(bookId, proposal, selectedFields: ["externalIds"], selectedImages: null, CancellationToken.None);

        var volume = await db.Entities.SingleAsync(row => row.Id == volumeId);
        var chapter = await db.Entities.SingleAsync(row => row.Id == chapterId);
        Assert.Equal("Volume 1", volume.Title);
        Assert.Equal("Chapter 1", chapter.Title);
        Assert.Equal(1, volume.SortOrder);
        Assert.Equal(1, chapter.SortOrder);
        Assert.Equal("chapter-1", (await db.EntityExternalIds.SingleAsync(row => row.EntityId == chapterId)).Value);
        Assert.Equal(3, await db.Entities.CountAsync());
    }

    [Fact]
    public async Task ApplyProposalChildrenWithoutTargetEntityIdsDoesNotCreateMissingStructuralBookChildren() {
        await using var db = CreateContext();
        var bookId = Guid.Parse("11111111-2222-3333-4444-555555555558");
        SeedEntity(db, bookId, "book", "Manga");
        await db.SaveChangesAsync();

        var proposal = new EntityMetadataProposal(
            ProposalId: "mangadex:manga-1",
            Provider: "mangadex",
            TargetKind: ProposalKind.Book,
            Confidence: 1,
            MatchReason: "external-id",
            Patch: EmptyPatch(),
            Images: [],
            Children:
            [
                new EntityMetadataProposal(
                    ProposalId: "mangadex:manga-1:volume:1",
                    Provider: "mangadex",
                    TargetKind: ProposalKind.BookVolume,
                    Confidence: 0.8m,
                    MatchReason: "volume-map",
                    Patch: EmptyPatch() with {
                        Title = "Volume 1",
                        ExternalIds = new Dictionary<string, string> { ["mangadex"] = "manga-1", ["volume"] = "1" },
                        Positions = new Dictionary<string, int> { ["volumeNumber"] = 1 }
                    },
                    Images: [],
                    Children: [],
                    Candidates: [])
            ],
            Candidates: []);

        var service = new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(Path.GetTempPath()));
        await service.ApplyAsync(bookId, proposal, selectedFields: ["externalIds"], selectedImages: null, CancellationToken.None);

        Assert.Equal([bookId], await db.Entities.Select(row => row.Id).ToArrayAsync());
        Assert.Empty(await db.EntityExternalIds.ToArrayAsync());
    }

    [Fact]
    public async Task ApplyBookCascadePromotesMatchedStructuralChildStudioRelationshipToBook() {
        await using var db = CreateContext();
        var bookId = Guid.Parse("22222222-3333-4444-5555-666666666666");
        var volumeId = Guid.Parse("22222222-3333-4444-5555-666666666667");
        var chapterId = Guid.Parse("22222222-3333-4444-5555-666666666668");
        SeedEntity(db, bookId, "book", "Manga");
        SeedEntity(db, volumeId, "book-volume", "Volume 1", parentEntityId: bookId, sortOrder: 1);
        SeedEntity(db, chapterId, "book-chapter", "Chapter 1", parentEntityId: volumeId, sortOrder: 1);
        await db.SaveChangesAsync();

        var proposal = new EntityMetadataProposal(
            ProposalId: "mangaplus:manga-1",
            Provider: "mangaplus",
            TargetKind: ProposalKind.Book,
            Confidence: 1,
            MatchReason: "external-id",
            Patch: EmptyPatch(),
            Images: [],
            Children:
            [
                new EntityMetadataProposal(
                    ProposalId: "mangaplus:manga-1:volume:1",
                    Provider: "mangaplus",
                    TargetKind: ProposalKind.BookVolume,
                    Confidence: 0.8m,
                    MatchReason: "volume-map",
                    Patch: EmptyPatch() with {
                        Title = "Volume 1",
                        Positions = new Dictionary<string, int> { ["volumeNumber"] = 1 }
                    },
                    Images: [],
                    Children:
                    [
                        new EntityMetadataProposal(
                            ProposalId: "mangaplus:manga-1:chapter:chapter-1",
                            Provider: "mangaplus",
                            TargetKind: ProposalKind.BookChapter,
                            Confidence: 0.7m,
                            MatchReason: "chapter-feed",
                            Patch: EmptyPatch() with {
                                Title = "Chapter 1",
                                Studio = "MangaPlus",
                                Positions = new Dictionary<string, int> { ["chapterNumber"] = 1 }
                            },
                            Images: [],
                            Children: [],
                            Candidates: [])
                    ],
                    Candidates: [])
            ],
            Candidates: []);

        var service = new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(Path.GetTempPath()));
        await service.ApplyAsync(bookId, proposal, selectedFields: ["externalIds"], selectedImages: null, CancellationToken.None);

        var studioId = await db.Entities
            .Where(row => row.KindCode == "studio" && row.Title == "MangaPlus")
            .Select(row => row.Id)
            .SingleAsync();

        var link = await db.EntityRelationshipLinks.SingleAsync(row => row.RelationshipCode == "studio");
        Assert.Equal(bookId, link.EntityId);
        Assert.Equal(studioId, link.TargetEntityId);
        Assert.DoesNotContain(db.EntityRelationshipLinks, row => row.EntityId == volumeId || row.EntityId == chapterId);
    }

    [Fact]
    public async Task ApplyCascadePositionsUpdatesCanonicalPositionsAndStructuralSortOrder() {
        await using var db = CreateContext();
        var seriesId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var seasonId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var episodeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        SeedEntity(db, seriesId, "video-series", "Series");
        SeedEntity(db, seasonId, "video-season", "Season", parentEntityId: seriesId, sortOrder: 1);
        SeedEntity(db, episodeId, "video", "Episode", parentEntityId: seasonId, sortOrder: 1);
        await db.SaveChangesAsync();

        var proposal = new EntityMetadataProposal(
            ProposalId: "provider:series:positions",
            Provider: "provider",
            TargetKind: ProposalKind.VideoSeries,
            TargetEntityId: seriesId,
            Confidence: 1,
            MatchReason: "external-id",
            Patch: EmptyPatch(),
            Images: [],
            Children:
            [
                new EntityMetadataProposal(
                    ProposalId: "provider:season:3",
                    Provider: "provider",
                    TargetKind: ProposalKind.VideoSeason,
                    TargetEntityId: seasonId,
                    Confidence: 1,
                    MatchReason: "structural-child",
                    Patch: EmptyPatch() with
                    {
                        Positions = new Dictionary<string, int> { ["seasonNumber"] = 3 }
                    },
                    Images: [],
                    Children:
                    [
                        new EntityMetadataProposal(
                            ProposalId: "provider:episode:2",
                            Provider: "provider",
                            TargetKind: ProposalKind.Video,
                            TargetEntityId: episodeId,
                            Confidence: 1,
                            MatchReason: "structural-child",
                            Patch: EmptyPatch() with
                            {
                                Positions = new Dictionary<string, int> { ["episodeNumber"] = 2 }
                            },
                            Images: [],
                            Children: [],
                            Candidates: [])
                    ],
                    Candidates: [])
            ],
            Candidates: []);

        var service = new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(Path.GetTempPath()));
        await service.ApplyAsync(seriesId, proposal, selectedFields: [], selectedImages: null, CancellationToken.None);

        Assert.Equal(3, (await db.Entities.FindAsync([seasonId]))?.SortOrder);
        Assert.Equal(2, (await db.Entities.FindAsync([episodeId]))?.SortOrder);
        Assert.Equal(3, (await db.EntityPositions.FindAsync([seasonId, "season"]))?.Value);
        Assert.Equal(2, (await db.EntityPositions.FindAsync([episodeId, "episode"]))?.Value);
        Assert.Null(await db.EntityPositions.FindAsync([seasonId, "seasonNumber"]));
        Assert.Null(await db.EntityPositions.FindAsync([episodeId, "episodeNumber"]));
    }

    [Fact]
    public async Task ApplyStructuralChildCreditsDeduplicatesRepeatedPeople() {
        await using var db = CreateContext();
        var seriesId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var episodeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var personId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        SeedEntity(db, seriesId, "video-series", "Series");
        SeedEntity(db, episodeId, "video", "Episode", parentEntityId: seriesId, sortOrder: 1);
        SeedEntity(db, personId, "person", "Returning Actor");
        await db.SaveChangesAsync();

        var proposal = new EntityMetadataProposal(
            ProposalId: "provider:series:credits",
            Provider: "provider",
            TargetKind: ProposalKind.VideoSeries,
            TargetEntityId: seriesId,
            Confidence: 1,
            MatchReason: "external-id",
            Patch: EmptyPatch(),
            Images: [],
            Children:
            [
                new EntityMetadataProposal(
                    ProposalId: "provider:episode:credits",
                    Provider: "provider",
                    TargetKind: ProposalKind.Video,
                    TargetEntityId: episodeId,
                    Confidence: 1,
                    MatchReason: "structural-child",
                    Patch: EmptyPatch() with
                    {
                        Credits =
                        [
                            new CreditPatch("Returning Actor", "person", "New Character", 0),
                            new CreditPatch("Returning Actor", "person", "Duplicate Character", 1)
                        ]
                    },
                    Images: [],
                    Children: [],
                    Candidates: [])
            ],
            Candidates: []);

        var service = new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(Path.GetTempPath()));
        await service.ApplyAsync(seriesId, proposal, selectedFields: [], selectedImages: null, CancellationToken.None);

        var credit = await db.EntityRelationshipLinks.SingleAsync(row => row.EntityId == episodeId && row.RelationshipCode == "cast");
        Assert.Equal(personId, credit.TargetEntityId);
        Assert.Contains("New Character", credit.MetadataJson ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(0, credit.SortOrder);
    }

    [Fact]
    public async Task ApplyStructuralChildArtworkDeduplicatesRepeatedLinkedPersonImages() {
        await using var db = CreateContext();
        var seriesId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var episodeId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var personId = Guid.Parse("12121212-1212-1212-1212-121212121212");
        SeedEntity(db, seriesId, "video-series", "Series");
        SeedEntity(db, episodeId, "video", "Episode", parentEntityId: seriesId, sortOrder: 1);
        SeedEntity(db, personId, "person", "Returning Actor");
        await db.SaveChangesAsync();

        var personImage = new ImageCandidate(
            Kind: "poster",
            Url: "https://example.test/actor.jpg",
            Source: "provider",
            Rank: null,
            Language: null,
            Width: null,
            Height: null);
        var duplicatePersonChild = new EntityMetadataProposal(
            ProposalId: "provider:person:actor",
            Provider: "provider",
            TargetKind: ProposalKind.Person,
            Confidence: 1,
            MatchReason: "credit",
            Patch: EmptyPatch() with { Title = "Returning Actor" },
            Images: [personImage],
            Children: [],
            Candidates: []);
        var proposal = new EntityMetadataProposal(
            ProposalId: "provider:series:artwork",
            Provider: "provider",
            TargetKind: ProposalKind.VideoSeries,
            TargetEntityId: seriesId,
            Confidence: 1,
            MatchReason: "external-id",
            Patch: EmptyPatch(),
            Images: [],
            Children:
            [
                new EntityMetadataProposal(
                    ProposalId: "provider:episode:artwork",
                    Provider: "provider",
                    TargetKind: ProposalKind.Video,
                    TargetEntityId: episodeId,
                    Confidence: 1,
                    MatchReason: "structural-child",
                    Patch: EmptyPatch() with
                    {
                        Credits = [new CreditPatch("Returning Actor", "person", "Character", 0)]
                    },
                    Images: [],
                    Children: [],
                    Candidates: [],
                    Relationships: [duplicatePersonChild, duplicatePersonChild])
            ],
            Candidates: []);

        var service = new EntityMetadataApplyService(
            db,
            new PluginArtworkServiceOptions(Path.GetTempPath()),
            new HttpClient(new FixedImageHandler()));
        await service.ApplyAsync(seriesId, proposal, selectedFields: [], selectedImages: null, CancellationToken.None);

        var file = await db.EntityFiles.SingleAsync(row => row.EntityId == personId && row.Role == EntityFileRole.Poster);
        Assert.Equal("/assets/plugins/artwork/12121212-1212-1212-1212-121212121212/poster-bcec36434214.jpg", file.Path);
    }

    [Fact]
    public async Task ApplyDownloadsRelationshipArtworkFromSeparatedRelationshipProposals() {
        await using var db = CreateContext();
        var seriesId = Guid.Parse("13131313-1313-1313-1313-131313131313");
        var episodeId = Guid.Parse("14141414-1414-1414-1414-141414141414");
        SeedEntity(db, seriesId, "video-series", "The Chair Company");
        SeedEntity(db, episodeId, "video", "Old Episode", parentEntityId: seriesId, sortOrder: 1);
        await db.SaveChangesAsync();

        var personRelationship = new EntityMetadataProposal(
            ProposalId: "tmdb:person:guest",
            Provider: "tmdb",
            TargetKind: ProposalKind.Person,
            Confidence: 1,
            MatchReason: "credit",
            Patch: EmptyPatch() with { Title = "Guest Actor" },
            Images: [new ImageCandidate("poster", "https://example.test/guest.jpg", "tmdb", null, null, null, null)],
            Children: [],
            Candidates: []);
        var proposal = new EntityMetadataProposal(
            ProposalId: "tmdb:tv:the-chair-company",
            Provider: "tmdb",
            TargetKind: ProposalKind.VideoSeries,
            TargetEntityId: seriesId,
            Confidence: 1,
            MatchReason: "external-id",
            Patch: EmptyPatch(),
            Images: [],
            Children:
            [
                new EntityMetadataProposal(
                    ProposalId: "tmdb:tv:the-chair-company:s1:e1",
                    Provider: "tmdb",
                    TargetKind: ProposalKind.Video,
                    TargetEntityId: episodeId,
                    Confidence: 1,
                    MatchReason: "structural-child",
                    Patch: EmptyPatch() with
                    {
                        Title = "Episode One",
                        Credits = [new CreditPatch("Guest Actor", "guest", "Company Man", 0)]
                    },
                    Images: [],
                    Children: [],
                    Candidates: [],
                    Relationships: [personRelationship])
            ],
            Candidates: []);

        var service = new EntityMetadataApplyService(
            db,
            new PluginArtworkServiceOptions(Path.GetTempPath()),
            new HttpClient(new FixedImageHandler()));
        await service.ApplyAsync(seriesId, proposal, selectedFields: [], selectedImages: null, CancellationToken.None);

        var personId = await db.Entities
            .Where(row => row.KindCode == "person" && row.Title == "Guest Actor")
            .Select(row => row.Id)
            .SingleAsync();
        Assert.Equal("Episode One", (await db.Entities.FindAsync([episodeId]))?.Title);
        Assert.Equal(personId, (await db.EntityRelationshipLinks.SingleAsync(row => row.EntityId == episodeId)).TargetEntityId);
        Assert.Equal("/assets/plugins/artwork/" + personId + "/poster-af1fa5679394.jpg", (await db.EntityFiles.SingleAsync(row => row.EntityId == personId)).Path);
    }

    [Fact]
    public async Task ApplyRootCreditsAndStudioUseSeparatedRelationshipArtwork() {
        await using var db = CreateContext();
        var movieId = Guid.Parse("15151515-1515-1515-1515-151515151515");
        SeedEntity(db, movieId, "video", "Old Movie");
        await db.SaveChangesAsync();

        var actorRelationship = new EntityMetadataProposal(
            ProposalId: "tmdb:person:lead",
            Provider: "tmdb",
            TargetKind: ProposalKind.Person,
            Confidence: 1,
            MatchReason: "credit",
            Patch: EmptyPatch() with { Title = "Lead Actor" },
            Images: [new ImageCandidate("poster", "https://example.test/lead.jpg", "tmdb", null, null, null, null)],
            Children: [],
            Candidates: []);
        var studioRelationship = new EntityMetadataProposal(
            ProposalId: "tmdb:studio:chair-pictures",
            Provider: "tmdb",
            TargetKind: ProposalKind.Studio,
            Confidence: 1,
            MatchReason: "studio",
            Patch: EmptyPatch() with { Title = "Chair Pictures" },
            Images: [
                new ImageCandidate("logo", "https://example.test/studio.png", "tmdb", null, null, null, null),
                new ImageCandidate("backdrop", "https://example.test/studio-banner.jpg", "tmdb", null, null, null, null)
            ],
            Children: [],
            Candidates: []);
        var proposal = new EntityMetadataProposal(
            ProposalId: "tmdb:movie:chair",
            Provider: "tmdb",
            TargetKind: ProposalKind.Video,
            Confidence: 1,
            MatchReason: "external-id",
            Patch: EmptyPatch() with {
                Studio = "Chair Pictures",
                Credits = [new CreditPatch("Lead Actor", "cast", "Lead", 0)]
            },
            Images: [],
            Children: [],
            Candidates: [],
            Relationships: [actorRelationship, studioRelationship]);

        var service = new EntityMetadataApplyService(
            db,
            new PluginArtworkServiceOptions(Path.GetTempPath()),
            new HttpClient(new FixedImageHandler()));
        await service.ApplyAsync(movieId, proposal, selectedFields: ["credits", "studio"], selectedImages: null, CancellationToken.None);

        var actorId = await db.Entities.Where(row => row.KindCode == "person" && row.Title == "Lead Actor").Select(row => row.Id).SingleAsync();
        var studioId = await db.Entities.Where(row => row.KindCode == "studio" && row.Title == "Chair Pictures").Select(row => row.Id).SingleAsync();
        Assert.Equal(EntityFileRole.Poster, (await db.EntityFiles.SingleAsync(row => row.EntityId == actorId)).Role);
        Assert.Equal(
            [EntityFileRole.Backdrop, EntityFileRole.Logo],
            await db.EntityFiles
                .Where(row => row.EntityId == studioId)
                .OrderBy(row => row.Role)
                .Select(row => row.Role)
                .ToArrayAsync());
    }

    [Fact]
    public async Task ApplyRelationshipProposalsHydratesLinkedEntityMetadata() {
        await using var db = CreateContext();
        var movieId = Guid.Parse("24242424-2424-2424-2424-242424242424");
        SeedEntity(db, movieId, "video", "Old Movie");
        await db.SaveChangesAsync();

        var actorRelationship = new EntityMetadataProposal(
            ProposalId: "tmdb:person:31",
            Provider: "tmdb",
            TargetKind: ProposalKind.Person,
            Confidence: 1,
            MatchReason: "credit",
            Patch: EmptyPatch() with {
                Title = "Lead Actor",
                Description = "Actor biography.",
                ExternalIds = new Dictionary<string, string> { ["tmdb"] = "31" },
                Urls = ["https://www.themoviedb.org/person/31"],
                Stats = new Dictionary<string, int> { ["popularity"] = 12 }
            },
            Images: [new ImageCandidate("poster", "https://example.test/lead.jpg", "tmdb", null, null, null, null)],
            Children: [],
            Candidates: []);
        var proposal = new EntityMetadataProposal(
            ProposalId: "tmdb:movie:1",
            Provider: "tmdb",
            TargetKind: ProposalKind.Video,
            Confidence: 1,
            MatchReason: "external-id",
            Patch: EmptyPatch() with {
                Credits = [new CreditPatch("Lead Actor", "cast", "Lead", 0)]
            },
            Images: [],
            Children: [],
            Candidates: [],
            Relationships: [actorRelationship]);

        var service = new EntityMetadataApplyService(
            db,
            new PluginArtworkServiceOptions(Path.GetTempPath()),
            new HttpClient(new FixedImageHandler()));
        await service.ApplyAsync(movieId, proposal, selectedFields: ["credits"], selectedImages: null, CancellationToken.None);

        var actorId = await db.Entities
            .Where(row => row.KindCode == "person" && row.Title == "Lead Actor")
            .Select(row => row.Id)
            .SingleAsync();
        Assert.Equal("Actor biography.", (await db.EntityDescriptions.FindAsync([actorId]))?.Value);
        Assert.Equal("31", (await db.EntityExternalIds.SingleAsync(row => row.EntityId == actorId)).Value);
        Assert.Equal("https://www.themoviedb.org/person/31", await db.EntityUrls
            .Where(row => row.EntityId == actorId)
            .Select(row => row.Url)
            .SingleAsync());
        Assert.Null(await db.EntityStats.FindAsync([actorId, "popularity"]));
        Assert.Equal(EntityFileRole.Poster, (await db.EntityFiles.SingleAsync(row => row.EntityId == actorId)).Role);
    }

    [Fact]
    public async Task ApplyRelationshipProposalsReplacesExistingLinkedArtwork() {
        await using var db = CreateContext();
        var movieId = Guid.Parse("29292929-2929-2929-2929-292929292929");
        var actorId = Guid.Parse("30303030-3030-3030-3030-303030303030");
        SeedEntity(db, movieId, "video", "Old Movie");
        SeedEntity(db, actorId, "person", "Lead Actor");
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = actorId,
            Role = EntityFileRole.Poster,
            Path = "/assets/plugins/artwork/old-poster.jpg",
            MimeType = "image/jpeg",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var actorRelationship = new EntityMetadataProposal(
            ProposalId: "tmdb:person:31",
            Provider: "tmdb",
            TargetKind: ProposalKind.Person,
            Confidence: 1,
            MatchReason: "credit",
            Patch: EmptyPatch() with { Title = "Lead Actor" },
            Images: [new ImageCandidate("poster", "https://example.test/lead-new.jpg", "tmdb", null, null, null, null)],
            Children: [],
            Candidates: []);
        var proposal = new EntityMetadataProposal(
            ProposalId: "tmdb:movie:chair",
            Provider: "tmdb",
            TargetKind: ProposalKind.Video,
            Confidence: 1,
            MatchReason: "external-id",
            Patch: EmptyPatch() with {
                Credits = [new CreditPatch("Lead Actor", "cast", "Lead", 0)]
            },
            Images: [],
            Children: [],
            Candidates: [],
            Relationships: [actorRelationship]);

        var service = new EntityMetadataApplyService(
            db,
            new PluginArtworkServiceOptions(Path.GetTempPath()),
            new HttpClient(new FixedImageHandler()));
        await service.ApplyAsync(movieId, proposal, selectedFields: ["credits"], selectedImages: null, CancellationToken.None);

        var file = await db.EntityFiles.SingleAsync(row => row.EntityId == actorId && row.Role == EntityFileRole.Poster);
        Assert.StartsWith($"/assets/plugins/artwork/{actorId}/poster-", file.Path);
        Assert.EndsWith(".jpg", file.Path);
    }

    [Fact]
    public async Task ApplyRelationshipProposalsUpsertsRepeatedExternalIdsWithinOneSave() {
        await using var db = CreateContext();
        var movieId = Guid.Parse("34343434-3434-3434-3434-343434343434");
        SeedEntity(db, movieId, "video", "Old Movie");
        await db.SaveChangesAsync();

        static EntityMetadataProposal ActorRelationship(string description) => new(
            ProposalId: $"tmdb:person:31:{description}",
            Provider: "tmdb",
            TargetKind: ProposalKind.Person,
            Confidence: 1,
            MatchReason: "credit",
            Patch: EmptyPatch() with {
                Title = "Lead Actor",
                Description = description,
                ExternalIds = new Dictionary<string, string> { ["tmdb"] = "31" },
                Urls = ["https://www.themoviedb.org/person/31"]
            },
            Images: [],
            Children: [],
            Candidates: []);

        var proposal = new EntityMetadataProposal(
            ProposalId: "tmdb:movie:1",
            Provider: "tmdb",
            TargetKind: ProposalKind.Video,
            Confidence: 1,
            MatchReason: "external-id",
            Patch: EmptyPatch() with {
                Credits = [new CreditPatch("Lead Actor", "cast", "Lead", 0)]
            },
            Images: [],
            Children: [],
            Candidates: [],
            Relationships: [ActorRelationship("First hydrate."), ActorRelationship("Second hydrate.")]);

        var service = new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(Path.GetTempPath()));
        await service.ApplyAsync(movieId, proposal, selectedFields: ["credits"], selectedImages: null, CancellationToken.None);

        var actorId = await db.Entities
            .Where(row => row.KindCode == "person" && row.Title == "Lead Actor")
            .Select(row => row.Id)
            .SingleAsync();
        Assert.Equal("Second hydrate.", (await db.EntityDescriptions.FindAsync([actorId]))?.Value);
        Assert.Equal("31", (await db.EntityExternalIds.SingleAsync(row => row.EntityId == actorId && row.Provider == "tmdb")).Value);
        Assert.Equal("https://www.themoviedb.org/person/31", await db.EntityUrls
            .Where(row => row.EntityId == actorId)
            .Select(row => row.Url)
            .SingleAsync());
    }

    [Fact]
    public async Task ApplyStructuralChildResolvesTheCompleteValidIdentitySetOnce() {
        await using var db = CreateContext();
        var bookId = Guid.NewGuid();
        var identityMatchId = Guid.NewGuid();
        var titleMatchId = Guid.NewGuid();
        SeedEntity(db, bookId, EntityKind.Book.ToCode(), "Book");
        SeedEntity(db, identityMatchId, EntityKind.BookVolume.ToCode(), "Identity Match", bookId);
        SeedEntity(db, titleMatchId, EntityKind.BookVolume.ToCode(), "Provider Volume", bookId);
        await db.SaveChangesAsync();

        var tmdb = new ExternalIdentity("tmdb", "603");
        var isbn = new ExternalIdentity("isbn-13", "9780000000001");
        var identities = new RecordingExternalIdentityStore {
            Resolution = new ExternalIdentityResolution([
                new ExternalIdentityMatch(identityMatchId, [tmdb, isbn])
            ])
        };
        var service = new EntityMetadataApplyService(
            db,
            new PluginArtworkServiceOptions(Path.GetTempPath()),
            externalIdentities: identities);
        var proposal = ProposalWithStructuralChild(
            bookId,
            "Provider Volume",
            new Dictionary<string, string> {
                [" TMDB "] = " 603 ",
                ["isbn-13"] = "9780000000001",
                ["candidate"] = "https://metadata.example/items/603",
                ["blank"] = " "
            });

        await service.ApplyAsync(bookId, proposal, selectedFields: [], selectedImages: null, CancellationToken.None);

        var call = Assert.Single(identities.ResolveCalls);
        Assert.Equal(EntityKind.BookVolume, call.Kind);
        Assert.Equal(bookId, call.ParentEntityId);
        Assert.Equal([isbn, tmdb], call.Identities.OrderBy(identity => identity.Namespace).ToArray());
        Assert.Equal("Provider Volume", (await db.Entities.FindAsync([identityMatchId]))?.Title);
        Assert.Equal("Provider Volume", (await db.Entities.FindAsync([titleMatchId]))?.Title);
    }

    [Fact]
    public async Task ApplyStructuralChildThrowsWhenExternalIdentitiesMatchDifferentEntities() {
        await using var db = CreateContext();
        var bookId = Guid.NewGuid();
        var firstMatchId = Guid.NewGuid();
        var titleMatchId = Guid.NewGuid();
        SeedEntity(db, bookId, EntityKind.Book.ToCode(), "Book");
        SeedEntity(db, firstMatchId, EntityKind.BookVolume.ToCode(), "First Match", bookId);
        SeedEntity(db, titleMatchId, EntityKind.BookVolume.ToCode(), "Provider Volume", bookId);
        await db.SaveChangesAsync();

        var tmdb = new ExternalIdentity("tmdb", "603");
        var isbn = new ExternalIdentity("isbn-13", "9780000000001");
        var identities = new RecordingExternalIdentityStore {
            Resolution = new ExternalIdentityResolution([
                new ExternalIdentityMatch(firstMatchId, [tmdb]),
                new ExternalIdentityMatch(titleMatchId, [isbn])
            ])
        };
        var service = new EntityMetadataApplyService(
            db,
            new PluginArtworkServiceOptions(Path.GetTempPath()),
            externalIdentities: identities);
        var proposal = ProposalWithStructuralChild(
            bookId,
            "Provider Volume",
            new Dictionary<string, string> {
                ["tmdb"] = "603",
                ["isbn-13"] = "9780000000001"
            });

        var exception = await Assert.ThrowsAsync<ExternalIdentityAmbiguityException>(() => service.ApplyAsync(
            bookId,
            proposal,
            selectedFields: [],
            selectedImages: null,
            CancellationToken.None));

        Assert.Equal(EntityKind.BookVolume, exception.Kind);
        Assert.Equal(2, exception.Matches.Count);
        Assert.Empty(identities.WriteCalls);
        Assert.Equal("First Match", (await db.Entities.FindAsync([firstMatchId]))?.Title);
        Assert.Equal("Provider Volume", (await db.Entities.FindAsync([titleMatchId]))?.Title);
    }

    [Fact]
    public async Task ApplyPatchDelegatesExternalIdentityReplacementToStore() {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        SeedEntity(db, entityId, EntityKind.Movie.ToCode(), "Movie");
        await db.SaveChangesAsync();
        var identities = new RecordingExternalIdentityStore();
        var service = new EntityMetadataApplyService(
            db,
            new PluginArtworkServiceOptions(Path.GetTempPath()),
            externalIdentities: identities);

        await service.ApplyPatchAsync(
            entityId,
            new EntityMetadataUpdateRequest(
                Fields: ["externalIds"],
                Patch: EmptyPatch() with {
                    ExternalIds = new Dictionary<string, string> { [" TMDB "] = " 603 " },
                    Urls = ["https://www.themoviedb.org/movie/603"]
                }),
            CancellationToken.None);

        var call = Assert.Single(identities.WriteCalls);
        Assert.Equal(ExternalIdentityWriteMode.ReplaceAll, call.Mode);
        var association = Assert.Single(call.Identities);
        Assert.Equal(new ExternalIdentity("tmdb", "603"), association.Identity);
        Assert.Equal("https://www.themoviedb.org/movie/603", association.Url);
    }

    [Fact]
    public async Task ApplyProposalDelegatesValidExternalIdentityUpsertsAndSkipsUrlLocators() {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        SeedEntity(db, entityId, EntityKind.Movie.ToCode(), "Movie");
        await db.SaveChangesAsync();
        var identities = new RecordingExternalIdentityStore();
        var service = new EntityMetadataApplyService(
            db,
            new PluginArtworkServiceOptions(Path.GetTempPath()),
            externalIdentities: identities);
        var proposal = new EntityMetadataProposal(
            ProposalId: "tmdb:movie:603",
            Provider: "tmdb",
            TargetKind: ProposalKind.Movie,
            Confidence: 1,
            MatchReason: "external-id",
            Patch: EmptyPatch() with {
                ExternalIds = new Dictionary<string, string> {
                    ["tmdb"] = "603",
                    ["candidate"] = "https://metadata.example/items/603"
                }
            },
            Images: [],
            Children: [],
            Candidates: []);

        await service.ApplyAsync(
            entityId,
            proposal,
            selectedFields: ["externalIds"],
            selectedImages: null,
            CancellationToken.None);

        var call = Assert.Single(identities.WriteCalls);
        Assert.Equal(ExternalIdentityWriteMode.Upsert, call.Mode);
        Assert.Equal(new ExternalIdentity("tmdb", "603"), Assert.Single(call.Identities).Identity);
    }

    [Fact]
    public async Task ApplyProposalBindsOnlyAcceptedProvidersDeclaredIdentityRoute() {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        SeedEntity(db, entityId, EntityKind.Movie.ToCode(), "Movie");
        await db.SaveChangesAsync();
        var identities = new EfEntityExternalIdentityStore(db, TimeProvider.System);
        var providerIdentities = new EfEntityProviderIdentityStore(db, TimeProvider.System);
        var router = new ConfiguredIdentityRouter(
            new PluginIdentityRoute("tmdb", new ExternalIdentity("tmdb", "603")),
            new PluginIdentityRoute("imdb", new ExternalIdentity("imdb", "tt0133093")));
        var service = new EntityMetadataApplyService(
            db,
            new PluginArtworkServiceOptions(Path.GetTempPath()),
            externalIdentities: identities,
            providerIdentities: providerIdentities,
            identityRouter: router);
        var proposal = new EntityMetadataProposal(
            ProposalId: "tmdb:movie:603",
            Provider: "TMDB",
            TargetKind: ProposalKind.Movie,
            Confidence: 1,
            MatchReason: "external-id",
            Patch: EmptyPatch() with {
                ExternalIds = new Dictionary<string, string> {
                    ["tmdb"] = "603",
                    ["imdb"] = "tt0133093"
                }
            },
            Images: [],
            Children: [],
            Candidates: []);

        await service.ApplyAsync(
            entityId,
            proposal,
            selectedFields: ["externalIds"],
            selectedImages: null,
            CancellationToken.None);

        var binding = await providerIdentities.GetAsync(entityId, CancellationToken.None);
        Assert.NotNull(binding);
        Assert.Equal("tmdb", binding.PluginId);
        Assert.Equal(new ExternalIdentity("tmdb", "603"), binding.Identity);
        Assert.Single(await db.EntityProviderIdentities.ToArrayAsync());
        Assert.Contains(db.EntityExternalIds, value =>
            value.EntityId == entityId
            && value.Provider == "imdb"
            && value.Value == "tt0133093");
    }

    [Fact]
    public async Task ApplyProposalDoesNotInferProviderIdentityWhenAcceptedPluginHasMultipleEligibleRoutes() {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        SeedEntity(db, entityId, EntityKind.Movie.ToCode(), "Ambiguous movie");
        await db.SaveChangesAsync();
        var identities = new EfEntityExternalIdentityStore(db, TimeProvider.System);
        var providerIdentities = new EfEntityProviderIdentityStore(db, TimeProvider.System);
        var router = new ConfiguredIdentityRouter(
            new PluginIdentityRoute("tmdb", new ExternalIdentity("tmdb", "603")),
            new PluginIdentityRoute("tmdb", new ExternalIdentity("tmdblegacy", "movie:603")));
        var service = new EntityMetadataApplyService(
            db,
            new PluginArtworkServiceOptions(Path.GetTempPath()),
            externalIdentities: identities,
            providerIdentities: providerIdentities,
            identityRouter: router);
        var proposal = new EntityMetadataProposal(
            ProposalId: "tmdb:movie:603",
            Provider: "tmdb",
            TargetKind: ProposalKind.Movie,
            Confidence: 1,
            MatchReason: "external-id",
            Patch: EmptyPatch() with {
                ExternalIds = new Dictionary<string, string> {
                    ["tmdb"] = "603",
                    ["tmdblegacy"] = "movie:603"
                }
            },
            Images: [],
            Children: [],
            Candidates: []);

        await service.ApplyAsync(
            entityId,
            proposal,
            selectedFields: ["externalIds"],
            selectedImages: null,
            CancellationToken.None);

        Assert.Null(await providerIdentities.GetAsync(entityId, CancellationToken.None));
        Assert.Empty(await db.EntityProviderIdentities.ToArrayAsync());
    }

    [Fact]
    public async Task ApplyProposalBindsRecursiveStructuralChildrenToTheirOwnProviderIdentity() {
        await using var db = CreateContext();
        var seriesId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        SeedEntity(db, seriesId, EntityKind.VideoSeries.ToCode(), "Series");
        SeedEntity(db, seasonId, EntityKind.VideoSeason.ToCode(), "Season 2", seriesId, sortOrder: 2);
        await db.SaveChangesAsync();
        var identities = new EfEntityExternalIdentityStore(db, TimeProvider.System);
        var providerIdentities = new EfEntityProviderIdentityStore(db, TimeProvider.System);
        var router = new ConfiguredIdentityRouter(
            new PluginIdentityRoute("tmdb", new ExternalIdentity("tmdb", "82728")),
            new PluginIdentityRoute("imdb", new ExternalIdentity("imdb", "tt7678620")),
            new PluginIdentityRoute("tmdb", new ExternalIdentity("tmdbseason", "82728:2")),
            new PluginIdentityRoute("tvdb", new ExternalIdentity("tvdbseason", "1921360")));
        var service = new EntityMetadataApplyService(
            db,
            new PluginArtworkServiceOptions(Path.GetTempPath()),
            externalIdentities: identities,
            providerIdentities: providerIdentities,
            identityRouter: router);
        var child = new EntityMetadataProposal(
            ProposalId: "tmdb:tv:82728:season:2",
            Provider: "tmdb",
            TargetKind: ProposalKind.VideoSeason,
            Confidence: 1,
            MatchReason: "structural-child",
            Patch: EmptyPatch() with {
                Title = "Season 2",
                ExternalIds = new Dictionary<string, string> {
                    ["tmdbseason"] = "82728:2",
                    ["tvdbseason"] = "1921360"
                }
            },
            Images: [],
            Children: [],
            Candidates: [],
            TargetEntityId: seasonId);
        var proposal = new EntityMetadataProposal(
            ProposalId: "tmdb:tv:82728",
            Provider: "tmdb",
            TargetKind: ProposalKind.VideoSeries,
            Confidence: 1,
            MatchReason: "external-id",
            Patch: EmptyPatch() with {
                ExternalIds = new Dictionary<string, string> {
                    ["tmdb"] = "82728",
                    ["imdb"] = "tt7678620"
                }
            },
            Images: [],
            Children: [child],
            Candidates: []);

        await service.ApplyAsync(
            seriesId,
            proposal,
            selectedFields: ["externalIds"],
            selectedImages: null,
            CancellationToken.None);

        var rootBinding = await providerIdentities.GetAsync(seriesId, CancellationToken.None);
        var childBinding = await providerIdentities.GetAsync(seasonId, CancellationToken.None);
        Assert.Equal(new ExternalIdentity("tmdb", "82728"), rootBinding?.Identity);
        Assert.Equal("tmdb", rootBinding?.PluginId);
        Assert.Equal(new ExternalIdentity("tmdbseason", "82728:2"), childBinding?.Identity);
        Assert.Equal("tmdb", childBinding?.PluginId);
        Assert.Equal(2, await db.EntityProviderIdentities.CountAsync());
        Assert.Contains(db.EntityExternalIds, value =>
            value.EntityId == seasonId
            && value.Provider == "tvdbseason"
            && value.Value == "1921360");
    }

    [Fact]
    public async Task ApplyMergesMultipleCreditRolesForSamePersonIntoOneRelationship() {
        await using var db = CreateContext();
        var episodeId = Guid.Parse("17171717-1717-1717-1717-171717171717");
        SeedEntity(db, episodeId, "video", "Old Episode");
        await db.SaveChangesAsync();

        var proposal = new EntityMetadataProposal(
            ProposalId: "tmdb:tv:chair:s1:e1",
            Provider: "tmdb",
            TargetKind: ProposalKind.Video,
            TargetEntityId: episodeId,
            Confidence: 1,
            MatchReason: "external-id",
            Patch: EmptyPatch() with {
                Credits =
                [
                    new CreditPatch("Tim Robinson", "cast", "Ron Trosper", 0),
                    new CreditPatch("Tim Robinson", "writer", null, 20),
                    new CreditPatch("Tim Robinson", "creator", null, 21)
                ]
            },
            Images: [],
            Children: [],
            Candidates: []);

        var service = new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(Path.GetTempPath()));
        await service.ApplyAsync(episodeId, proposal, selectedFields: ["credits"], selectedImages: null, CancellationToken.None);

        var credit = await db.EntityRelationshipLinks.SingleAsync(row => row.EntityId == episodeId && row.RelationshipCode == "cast");
        Assert.Equal("Tim Robinson", await db.Entities
            .Where(row => row.Id == credit.TargetEntityId)
            .Select(row => row.Title)
            .SingleAsync());
        Assert.Equal(0, credit.SortOrder);
        Assert.Contains("\"role\":\"cast\"", credit.MetadataJson ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("\"roles\":[\"cast\",\"writer\",\"creator\"]", credit.MetadataJson ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("Ron Trosper", credit.MetadataJson ?? string.Empty, StringComparison.Ordinal);
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"metadata-apply-{Guid.NewGuid():N}")
            .Options;

        return new PrismediaDbContext(options);
    }

    private static void SeedEntity(
        PrismediaDbContext db,
        Guid id,
        string kind,
        string title,
        Guid? parentEntityId = null,
        int? sortOrder = null) {
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = kind,
            Title = title,
            ParentEntityId = parentEntityId,
            SortOrder = sortOrder,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }

    private static EntityMetadataPatch EmptyPatch() => new(
        Title: null,
        Description: null,
        ExternalIds: new Dictionary<string, string>(),
        Urls: [],
        Tags: [],
        Studio: null,
        Credits: [],
        Dates: new Dictionary<string, string>(),
        Stats: new Dictionary<string, int>(),
        Positions: new Dictionary<string, int>(),
        Classification: null);

    private static EntityMetadataProposal ProposalWithStructuralChild(
        Guid bookId,
        string childTitle,
        IReadOnlyDictionary<string, string> externalIds) =>
        new(
            ProposalId: "provider:book",
            Provider: "provider",
            TargetKind: ProposalKind.Book,
            TargetEntityId: bookId,
            Confidence: 1,
            MatchReason: "external-id",
            Patch: EmptyPatch(),
            Images: [],
            Children: [new EntityMetadataProposal(
                ProposalId: "provider:volume",
                Provider: "provider",
                TargetKind: ProposalKind.BookVolume,
                Confidence: 1,
                MatchReason: "external-id",
                Patch: EmptyPatch() with { Title = childTitle, ExternalIds = externalIds },
                Images: [],
                Children: [],
                Candidates: [])],
            Candidates: []);

    private sealed class RecordingExternalIdentityStore : IEntityExternalIdentityStore {
        public ExternalIdentityResolution Resolution { get; init; } = new([]);

        public List<ExternalIdentityResolveCall> ResolveCalls { get; } = [];

        public List<ExternalIdentityWriteCall> WriteCalls { get; } = [];

        public Task<IReadOnlyList<DomainEntityExternalId>> ListAsync(
            Guid entityId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DomainEntityExternalId>>([]);

        public Task<ExternalIdentityResolution> ResolveAsync(
            EntityKind kind,
            IReadOnlyCollection<ExternalIdentity> identities,
            Guid? parentEntityId,
            CancellationToken cancellationToken) {
            ResolveCalls.Add(new ExternalIdentityResolveCall(kind, identities.ToArray(), parentEntityId));
            return Task.FromResult(Resolution);
        }

        public Task WriteAsync(
            Guid entityId,
            IReadOnlyCollection<DomainEntityExternalId> identities,
            ExternalIdentityWriteMode mode,
            CancellationToken cancellationToken) {
            WriteCalls.Add(new ExternalIdentityWriteCall(entityId, identities.ToArray(), mode));
            return Task.CompletedTask;
        }
    }

    private sealed record ExternalIdentityResolveCall(
        EntityKind Kind,
        IReadOnlyList<ExternalIdentity> Identities,
        Guid? ParentEntityId);

    private sealed record ExternalIdentityWriteCall(
        Guid EntityId,
        IReadOnlyList<DomainEntityExternalId> Identities,
        ExternalIdentityWriteMode Mode);

    private sealed class ConfiguredIdentityRouter(params PluginIdentityRoute[] routes) : IPluginIdentityRouter {
        public Task<IReadOnlyList<PluginIdentityRoute>> ResolveAsync(
            string entityKindCode,
            IdentifyAction action,
            IReadOnlyList<ExternalIdentity> identities,
            CancellationToken cancellationToken) {
            var requested = identities.ToHashSet();
            return Task.FromResult<IReadOnlyList<PluginIdentityRoute>>(
                routes.Where(route => requested.Contains(route.Identity)).ToArray());
        }
    }

    private sealed class FixedImageHandler : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                Content = new ByteArrayContent([1, 2, 3])
            });
    }

    private sealed class RequiresUserAgentImageHandler : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            if (!request.Headers.UserAgent.Any()) {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest) {
                    Content = new StringContent("You must set an appropriate User-Agent header")
                });
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                Content = new ByteArrayContent([1, 2, 3])
            });
        }
    }

    private sealed class LeaseObservingImageHandler(Func<bool> insideLease) : HttpMessageHandler {
        public bool WasCalled { get; private set; }
        public bool ObservedInsideLease { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) {
            WasCalled = true;
            ObservedInsideLease |= insideLease();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                Content = new ByteArrayContent([1, 2, 3])
            });
        }
    }

    private sealed class ObservingLifecycleLease : IEntityLifecycleMutationLease {
        public bool InsideLease { get; private set; }

        public async Task<bool> ExecuteAsync(
            Guid entityId,
            Func<CancellationToken, Task> mutation,
            CancellationToken cancellationToken) {
            InsideLease = true;
            try {
                await mutation(cancellationToken);
                return true;
            } finally {
                InsideLease = false;
            }
        }
    }
}
