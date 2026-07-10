using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Plugins;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Domain.Taxonomy;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Entities.Mappers;
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
    public async Task FindAsyncHydratesConcreteEntityRelationshipMapsAndCredits() {
        await using var db = CreateContext();
        var seriesId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var seasonId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var personId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var tagId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        SeedEntity(db, seriesId, EntityKind.VideoSeries, "Series");
        SeedEntity(db, seasonId, EntityKind.VideoSeason, "Season 1", seriesId, sortOrder: 1);
        SeedEntity(db, personId, EntityKind.Person, "Ada Person");
        SeedEntity(db, tagId, EntityKind.Tag, "Noir");
        db.EntityRelationshipLinks.AddRange(
            new EntityRelationshipLinkRow {
                EntityId = seriesId,
                RelationshipCode = "related",
                Label = "Ada Person",
                TargetEntityId = personId,
                TargetKindCode = EntityKindRegistry.Person.Code,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new EntityRelationshipLinkRow {
                EntityId = seriesId,
                RelationshipCode = "credits",
                Label = "Detective",
                TargetEntityId = personId,
                TargetKindCode = EntityKindRegistry.Person.Code,
                SortOrder = 1,
                MetadataJson = """{"role":"actor"}""",
                CreatedAt = DateTimeOffset.UtcNow
            });
        db.EntityRelationshipLinks.Add(
            new EntityRelationshipLinkRow {
                EntityId = seriesId,
                RelationshipCode = "related",
                Label = "Noir",
                TargetEntityId = tagId,
                TargetKindCode = EntityKindRegistry.Tag.Code,
                CreatedAt = DateTimeOffset.UtcNow
            });
        await db.SaveChangesAsync();

        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var series = await repository.RequireAsync<VideoSeries>(seriesId, CancellationToken.None);

        Assert.Equal(EntityKind.VideoSeries, series.Kind);
        Assert.Same(Assert.Single(series.ChildrenOf<VideoSeason>()), series.ChildrenByKind[EntityKind.VideoSeason][0]);
        Assert.IsType<Tag>(Assert.Single(series.RelationshipsOf<Tag>()));
        var relatedPerson = Assert.Single(series.RelationshipsOf<Person>());
        var credits = series.Credits!.Credits;
        var credit = Assert.Single(credits);
        Assert.Equal(relatedPerson.Id, credit.Person.Id);
        Assert.Equal(CreditRole.Actor, credits[0].Role);
        Assert.Equal("Detective", credits[0].Label);
    }

    [Fact]
    public async Task SaveAsyncPersistsBasicFieldsRelationshipMapsAndCredits() {
        await using var db = CreateContext();
        var series = new VideoSeries(Guid.Parse("55555555-5555-5555-5555-555555555555"), "Series");
        var season = new VideoSeason(Guid.Parse("66666666-6666-6666-6666-666666666666"), "Season 1", parentEntityId: null);
        var tag = new Tag(Guid.Parse("77777777-7777-7777-7777-777777777777"), "Noir");
        var person = new Person(Guid.Parse("88888888-8888-8888-8888-888888888888"), "Ada Person");
        series.AddChild(season, sortOrder: 3);
        series.AddRelationship(tag);
        series.Credits!.Add(person, CreditRole.Actor, "Detective");

        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        await repository.SaveAsync(series, CancellationToken.None);

        Assert.Equal("video-series", Assert.Single(db.Entities.Where(row => row.Id == series.Id)).KindCode);
        Assert.Equal(series.Id, Assert.Single(db.Entities.Where(row => row.Id == season.Id)).ParentEntityId);
        Assert.Equal(3, Assert.Single(db.Entities.Where(row => row.Id == season.Id)).SortOrder);
        Assert.Contains(db.EntityRelationshipLinks, link =>
            link.EntityId == series.Id &&
            link.RelationshipCode == "related" &&
            link.TargetEntityId == tag.Id);
        var credit = Assert.Single(db.EntityRelationshipLinks.Where(link =>
            link.EntityId == series.Id &&
            link.RelationshipCode == "credits" &&
            link.TargetEntityId == person.Id));
        Assert.Equal("Detective", credit.Label);
        Assert.Contains("actor", credit.MetadataJson, StringComparison.OrdinalIgnoreCase);
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
        await repository.SaveAsync(track, CancellationToken.None);

        var row = await db.Entities.SingleAsync(entity => entity.Id == trackId);
        Assert.Equal(libraryId, row.ParentEntityId);
        Assert.Equal(7, row.SortOrder);
        var state = await db.UserEntityStates.SingleAsync(s => s.EntityId == trackId);
        Assert.Equal(TestUserContext.UserId, state.UserId);
        Assert.Equal(4, state.RatingValue);
    }

    [Fact]
    public async Task SaveThenFindRoundTripsEveryPersistedCapabilityWithoutLoss() {
        await using var db = CreateContext();
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var video = new Video(id, "Faithful");

        Set(video, new CapabilityDescription("A noir mystery"));
        Set(video, MakeTechnical(width: 1920, height: 1080, codec: "h264"));
        video.AttachFile(EntityFileRole.Source, "/media/v.mp4", "video/mp4");
        Set(video, new CapabilityStats([new CapabilityStats.Item("scenes", 12)]));
        Set(video, new CapabilityDates([new EntityDate("released", "2020-01-01", new DateOnly(2020, 1, 1), "day")]));
        Set(video, new CapabilitySource([new CapabilitySource.Item("stash", "abc")]));
        Set(video, new CapabilityPosition([new CapabilityPosition.Item("episode", 5, "E5")]));
        video.AddUrl("https://example.test", "Example");
        video.SetExternalId("tmdb", "42", "https://tmdb.test/42");
        var subtitlePath = Path.GetTempFileName();
        Set(video, new CapabilitySubtitles([new CapabilitySubtitles.Item(
            Guid.NewGuid(), "en", "English", "srt", EntitySubtitleSource.Embedded, subtitlePath, "srt", null, true)]));
        Set(video, new CapabilityFingerprints([new CapabilityFingerprints.Item(FingerprintAlgorithm.Md5, "deadbeef")]));
        Set(video, new CapabilityClassification("R", "MPAA"));
        Set(video, new CapabilityProgress(currentEntityId: null, unit: ProgressUnit.Chapter, index: 4, total: 10, mode: ReaderMode.Paged, updatedAt: DateTimeOffset.UtcNow));

        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        await repository.SaveAsync(video, CancellationToken.None);

        var loaded = await repository.RequireAsync<Video>(id, CancellationToken.None);

        Assert.Equal("A noir mystery", loaded.Description!.Value);
        Assert.Equal(1920, loaded.Technical!.Width);
        Assert.Equal("h264", loaded.Technical!.Codec);
        Assert.Equal(EntityFileRole.Source, Assert.Single(loaded.EntityFiles).Role);
        Assert.Equal(12, Assert.Single(loaded.Stats!.Items).Value);
        Assert.Equal("released", Assert.Single(loaded.Dates!.Items).Code);
        Assert.Equal("abc", Assert.Single(loaded.Source!.Items).Value);
        Assert.Equal("E5", Assert.Single(loaded.Position!.Items).Label);
        Assert.Equal("https://example.test", Assert.Single(loaded.Urls).Value);
        Assert.Equal("tmdb", Assert.Single(loaded.ExternalIds).Provider);
        Assert.Equal("en", Assert.Single(loaded.SubtitleCapability!.Items).Language);
        Assert.Equal(FingerprintAlgorithm.Md5, Assert.Single(loaded.GetCapability<CapabilityFingerprints>()!.Items).Algorithm);
        Assert.Equal("R", loaded.Classification!.Value);
        Assert.Equal(4, loaded.Progress!.Index);
        Assert.Equal(10, loaded.Progress!.Total);

        File.Delete(subtitlePath);
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
        Assert.Equal(EntityKindRegistry.Video.Code, urlCall.EntityKindCode);
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
        Assert.Equal(EntityKindRegistry.Video.Code, call.EntityKindCode);
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
    public async Task SaveAsyncReplacesTheCompleteIdentitySetWithoutChurningUnchangedRows() {
        await using var db = CreateContext();
        var id = Guid.Parse("cccccccc-1111-2222-3333-dddddddddddd");
        SeedEntity(db, id, EntityKind.Video, "Stable identity");
        var unchanged = AddExternalIdentity(
            db,
            id,
            "tmdb",
            "603",
            "https://www.themoviedb.org/movie/603",
            IdentityCreatedAt);
        var removed = AddExternalIdentity(
            db,
            id,
            "imdb",
            "tt0133093",
            "https://www.imdb.com/title/tt0133093",
            IdentityCreatedAt.AddMinutes(1));
        await db.SaveChangesAsync();
        var unchangedRowId = unchanged.Id;
        var unchangedCreatedAt = unchanged.CreatedAt;
        var unchangedUpdatedAt = unchanged.UpdatedAt;
        var identities = new RecordingExternalIdentityStore(
            inner: new EfEntityExternalIdentityStore(db, new FixedTimeProvider(IdentityWriteTime)));
        var repository = new EfEntityRepository(
            db,
            TestUserContext.Admin(),
            EntityMappers.Kinds(db),
            EntityMappers.Capabilities(db, TestUserContext.Admin()),
            identities);
        var video = new Video(id, "Stable identity");
        video.SetExternalId(" TMDB ", " 603 ", "https://www.themoviedb.org/movie/603");

        await repository.SaveAsync(video, CancellationToken.None);

        var persisted = Assert.Single(await db.EntityExternalIds.AsNoTracking().ToArrayAsync());
        Assert.Equal(ExternalIdentityWriteMode.ReplaceAll, Assert.Single(identities.WriteModes));
        Assert.Equal(id, Assert.Single(identities.WrittenEntityIds));
        Assert.Equal(unchangedRowId, persisted.Id);
        Assert.Equal(unchangedCreatedAt, persisted.CreatedAt);
        Assert.Equal(unchangedUpdatedAt, persisted.UpdatedAt);
        Assert.Equal("tmdb", persisted.Provider);
        Assert.Equal("603", persisted.Value);
        Assert.DoesNotContain(db.EntityExternalIds, row => row.Id == removed.Id);
    }

    [Fact]
    public async Task SavingTheSameEntityTwiceDoesNotDuplicateCapabilityRows() {
        // A retried job re-runs its handler from scratch and re-saves the entity. The save must be
        // idempotent so retries cannot accumulate duplicate capability rows.
        await using var db = CreateContext();
        var id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));

        for (var i = 0; i < 3; i++) {
            var video = new Video(id, "Retried");
            Set(video, new CapabilityStats([new CapabilityStats.Item("scenes", 12), new CapabilityStats.Item("tags", 3)]));
            Set(video, new CapabilitySource([new CapabilitySource.Item("stash", "abc")]));
            video.AttachFile(EntityFileRole.Source, "/media/v.mp4", "video/mp4");
            await repository.SaveAsync(video, CancellationToken.None);
        }

        Assert.Equal(2, await db.EntityStats.CountAsync(r => r.EntityId == id));
        Assert.Equal(1, await db.EntitySources.CountAsync(r => r.EntityId == id));
        Assert.Equal(1, await db.EntityFiles.CountAsync(r => r.EntityId == id));
        Assert.Equal(1, await db.Entities.CountAsync(r => r.Id == id));
    }

    [Fact]
    public async Task FindAsyncDoesNotHydrateMissingSubtitleFiles() {
        await using var db = CreateContext();
        var id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        SeedEntity(db, id, EntityKind.Video, "Missing subtitle");
        db.VideoDetails.Add(new VideoDetailRow {
            EntityId = id
        });
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

    [Fact]
    public async Task SaveThenFindRoundTripsKindSpecificDetail() {
        await using var db = CreateContext();
        var repository = new EfEntityRepository(db, TestUserContext.Admin(), EntityMappers.Kinds(db), EntityMappers.Capabilities(db, TestUserContext.Admin()));
        var cover = Guid.Parse("12121212-1212-1212-1212-121212121212");
        var refreshed = DateTimeOffset.UtcNow;

        var gallery = new Gallery(Guid.NewGuid(), "G", GalleryType.Zip, cover);
        var person = new Person(Guid.NewGuid(), "P", disambiguation: "the elder", gender: "f", country: "US", height: 170);
        var tag = new Tag(Guid.NewGuid(), "T", ignoreAutoTag: true);
        var track = new AudioTrack(Guid.NewGuid(), "A", "Artist", "Album");
        var book = new Book(Guid.NewGuid(), "B", BookType.Manga, cover);
        var collection = new Collection(Guid.NewGuid(), "C", CollectionMode.Dynamic, "{}", CollectionCoverMode.Custom, cover, refreshed);

        foreach (var entity in new Entity[] { gallery, person, tag, track, book, collection }) {
            await repository.SaveAsync(entity, CancellationToken.None);
        }

        var loadedGallery = await repository.RequireAsync<Gallery>(gallery.Id, CancellationToken.None);
        Assert.Equal(GalleryType.Zip, loadedGallery.GalleryType);
        Assert.Equal(cover, loadedGallery.CoverImageId);

        var loadedPerson = await repository.RequireAsync<Person>(person.Id, CancellationToken.None);
        Assert.Equal("the elder", loadedPerson.Disambiguation);
        Assert.Equal("US", loadedPerson.Country);
        Assert.Equal(170, loadedPerson.Height);

        Assert.True((await repository.RequireAsync<Tag>(tag.Id, CancellationToken.None)).IgnoreAutoTag);

        var loadedTrack = await repository.RequireAsync<AudioTrack>(track.Id, CancellationToken.None);
        Assert.Equal("Artist", loadedTrack.EmbeddedArtist);
        Assert.Equal("Album", loadedTrack.EmbeddedAlbum);

        Assert.Equal(BookType.Manga, (await repository.RequireAsync<Book>(book.Id, CancellationToken.None)).BookType);

        var loadedCollection = await repository.RequireAsync<Collection>(collection.Id, CancellationToken.None);
        Assert.Equal(CollectionMode.Dynamic, loadedCollection.Mode);
        Assert.Equal(CollectionCoverMode.Custom, loadedCollection.CoverMode);
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

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
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
