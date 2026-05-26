using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Domain.Taxonomy;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Entities.Mappers;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfEntityRepositoryTests {
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

        var repository = new EfEntityRepository(db, EntityMappers.Kinds(db), EntityMappers.Capabilities(db));
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

        var repository = new EfEntityRepository(db, EntityMappers.Kinds(db), EntityMappers.Capabilities(db));
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

        var repository = new EfEntityRepository(db, EntityMappers.Kinds(db), EntityMappers.Capabilities(db));
        var track = await repository.FindShallowAsync(trackId, CancellationToken.None);

        Assert.NotNull(track);
        Assert.Equal(libraryId, track.ParentEntityId);
        Assert.Equal(7, track.SortOrder);

        track.Rate(4);
        await repository.SaveAsync(track, CancellationToken.None);

        var row = await db.Entities.SingleAsync(entity => entity.Id == trackId);
        Assert.Equal(libraryId, row.ParentEntityId);
        Assert.Equal(7, row.SortOrder);
        Assert.Equal(4, row.RatingValue);
    }

    [Fact]
    public async Task SaveThenFindRoundTripsEveryPersistedCapabilityWithoutLoss() {
        await using var db = CreateContext();
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var video = new Video(id, "Faithful", subtitlesExtractedAt: null);

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
        Set(video, new CapabilityProgress(currentEntityId: null, unit: "chapter", index: 4, total: 10, mode: "paged", updatedAt: DateTimeOffset.UtcNow));

        var repository = new EfEntityRepository(db, EntityMappers.Kinds(db), EntityMappers.Capabilities(db));
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

        var repository = new EfEntityRepository(db, EntityMappers.Kinds(db), EntityMappers.Capabilities(db));
        var loaded = await repository.RequireAsync<Video>(id, CancellationToken.None);

        Assert.Empty(loaded.SubtitleCapability!.Items);
    }

    [Fact]
    public async Task SaveThenFindRoundTripsKindSpecificDetail() {
        await using var db = CreateContext();
        var repository = new EfEntityRepository(db, EntityMappers.Kinds(db), EntityMappers.Capabilities(db));
        var cover = Guid.Parse("12121212-1212-1212-1212-121212121212");
        var refreshed = DateTimeOffset.UtcNow;

        var gallery = new Gallery(Guid.NewGuid(), "G", GalleryType.Zip, cover);
        var person = new Person(Guid.NewGuid(), "P", disambiguation: "the elder", gender: "f", country: "US", height: 170);
        var tag = new Tag(Guid.NewGuid(), "T", ignoreAutoTag: true);
        var track = new AudioTrack(Guid.NewGuid(), "A", "Artist", "Album");
        var book = new Book(Guid.NewGuid(), "B", BookType.Manga, cover);
        var collection = new Collection(Guid.NewGuid(), "C", CollectionMode.Dynamic, "{}", CollectionCoverMode.Custom, cover, TimeSpan.FromSeconds(9), true, refreshed);

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
        Assert.Equal(TimeSpan.FromSeconds(9), loadedCollection.SlideshowDuration);
        Assert.True(loadedCollection.SlideshowAutoAdvance);
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
        var repository = new EfEntityRepository(db, EntityMappers.Kinds(db), EntityMappers.Capabilities(db));
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
}
