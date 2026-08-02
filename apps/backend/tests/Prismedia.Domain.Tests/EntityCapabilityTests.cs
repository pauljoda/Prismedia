using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Domain.Taxonomy;

namespace Prismedia.Domain.Tests;

public sealed class EntityCapabilityTests {
    [Fact]
    public void BookTracksReadingAndListeningProgressIndependently() {
        var book = new Book(Guid.NewGuid(), "Dune", BookType.Novel, null, BookFormat.Epub);

        Assert.NotNull(book.GetCapability<CapabilityProgress>());
        Assert.NotNull(book.GetCapability<CapabilityConsumption>());
    }

    [Fact]
    public void GetCapabilityReturnsAttachedReference() {
        var playback = new CapabilityConsumption();
        var video = new Video(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "Projected Video",
            capabilities: [playback]);

        var attached = video.GetCapability<CapabilityConsumption>();

        Assert.Same(playback, attached);
    }

    [Fact]
    public void MutatingReturnedCapabilityMutatesEntityState() {
        var video = new Video(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "Projected Video",
            capabilities: [new CapabilityConsumption()]);

        video.GetCapability<CapabilityConsumption>()!.RecordResume(
            TimeSpan.FromSeconds(10), DateTimeOffset.UtcNow);

        Assert.Equal(TimeSpan.FromSeconds(10), video.Consumption!.ResumeTime);
    }

    [Fact]
    public void EntityRejectsDuplicateCapabilityKinds() {
        var ex = Assert.Throws<ArgumentException>(() => new Video(
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            "Projected Video",
            capabilities: [new CapabilityConsumption(), new CapabilityConsumption()]));

        Assert.Contains(nameof(CapabilityConsumption), ex.Message);
    }

    [Fact]
    public void MissingCapabilityHelpersExposeOptionalAndRequiredPaths() {
        var image = new Image(
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            "Projected Image",
            capabilities: []);

        Assert.False(image.HasCapability<CapabilityConsumption>());
        Assert.Null(image.GetCapability<CapabilityConsumption>());
        Assert.Throws<InvalidOperationException>(() => image.RequireCapability<CapabilityConsumption>());
    }

    [Fact]
    public void RemoveCapabilityDetachesTheCapabilityFromTheEntity() {
        var playback = new CapabilityConsumption();
        var video = new Video(
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            "Projected Video",
            capabilities: [playback]);

        var removed = video.RemoveCapability<CapabilityConsumption>();

        Assert.True(removed);
        Assert.Null(video.GetCapability<CapabilityConsumption>());
    }

    [Fact]
    public void EntityChildrenCanBeAddedAndReturnedByConcreteType() {
        var season = new VideoSeason(
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            "Season 1",
            parentEntityId: null);
        var episode = new VideoEpisode(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Episode 1",
            parentEntityId: null);
        var series = new VideoSeries(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "Series");

        series.AddChild(season);
        series.AddChild(episode);

        Assert.Same(season, Assert.Single(series.ChildrenOf<VideoSeason>()));
        Assert.Same(episode, Assert.Single(series.ChildrenOf<VideoEpisode>()));
        Assert.Equal([season, episode], series.ChildEntities);
        Assert.Equal([season], series.ChildrenByKind[EntityKind.VideoSeason]);
        Assert.Equal([episode], series.ChildrenByKind[EntityKind.VideoEpisode]);
    }

    [Fact]
    public void EntityRelationshipsCanBeAddedAndReturnedByKindAndConcreteType() {
        var video = new Video(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "Episode 1");
        var person = new Person(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            "Ada Person");
        var tag = new Tag(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            "Noir");

        video.AddRelationship(person);
        video.AddRelationship(tag);

        Assert.Same(person, Assert.Single(video.RelationshipsOf<Person>()));
        Assert.Same(tag, Assert.Single(video.RelationshipsOf<Tag>()));
        Assert.Equal([person], video.RelationshipsByKind[EntityKind.Person]);
        Assert.Equal([tag], video.RelationshipsByKind[EntityKind.Tag]);
    }

    [Fact]
    public void EntityRejectsDuplicateChildrenAndRelationships() {
        var episode = new VideoEpisode(
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            "Episode 1",
            parentEntityId: null);
        var series = new VideoSeries(
            Guid.Parse("77777777-7777-7777-7777-777777777777"),
            "Series");

        series.AddChild(episode);
        series.AddRelationship(episode);

        Assert.Throws<ArgumentException>(() => series.AddChild(episode));
        Assert.Throws<ArgumentException>(() => series.AddRelationship(episode));
    }

    [Fact]
    public void DeclaredStructurePoliciesRejectInvalidKindsAndReparenting() {
        var series = new VideoSeries(Guid.NewGuid(), "Series");
        var season = new VideoSeason(Guid.NewGuid(), "Season 1", series.Id);
        var otherSeries = new VideoSeries(Guid.NewGuid(), "Other Series");
        var movie = new Movie(Guid.NewGuid(), "Movie");

        Assert.Throws<ArgumentException>(() => series.AddChild(movie));
        Assert.Throws<ArgumentException>(() => movie.AddChild(season));
        Assert.Throws<ArgumentException>(() => otherSeries.AddChild(season));
    }

    [Fact]
    public void StructurePoliciesRejectUndeclaredParentChildEdges() {
        var person = new Person(Guid.NewGuid(), "Parent");
        var book = new Book(Guid.NewGuid(), "Child", BookType.Novel, null, BookFormat.Epub);

        Assert.Throws<ArgumentException>(() => person.AddChild(book));
    }

    [Fact]
    public void HydrationEnforcesDeclaredParentPresence() {
        var episode = new VideoEpisode(Guid.NewGuid(), "Episode", parentEntityId: null);
        var movie = new Movie(Guid.NewGuid(), "Movie");

        Assert.Throws<InvalidOperationException>(() => episode.HydrateStructuralPlacement(null, null));
        Assert.Throws<InvalidOperationException>(() => movie.HydrateStructuralPlacement(Guid.NewGuid(), null));
    }

    [Fact]
    public void OptionalRootKindsCanRemainRootsOrBeNestedUnderTheirDeclaredParents() {
        var gallery = new Gallery(Guid.NewGuid(), "Gallery", GalleryType.Folder, null);
        var image = new Image(Guid.NewGuid(), "Loose image");
        var book = new Book(Guid.NewGuid(), "Book", BookType.Novel, null, BookFormat.Epub);
        var nestedBook = new Book(Guid.NewGuid(), "Nested book", BookType.Novel, null, BookFormat.Epub);

        image.HydrateStructuralPlacement(null, null);
        gallery.AddChild(image);
        book.AddChild(nestedBook);

        Assert.Equal(gallery.Id, image.ParentEntityId);
        Assert.Equal(book.Id, nestedBook.ParentEntityId);
    }

    [Fact]
    public void RecursiveBookAndGalleryTreesAreAllowedButSubtreeCyclesAreRejected() {
        var rootBook = new Book(Guid.NewGuid(), "Collection", BookType.Novel, null, BookFormat.Epub);
        var nestedBook = new Book(Guid.NewGuid(), "Volume", BookType.Novel, null, BookFormat.Epub);
        var leafBook = new Book(Guid.NewGuid(), "Issue", BookType.Novel, null, BookFormat.Epub);
        rootBook.AddChild(nestedBook);
        nestedBook.AddChild(leafBook);

        var rootGallery = new Gallery(Guid.NewGuid(), "Root", GalleryType.Folder, null);
        var nestedGallery = new Gallery(Guid.NewGuid(), "Nested", GalleryType.Folder, null);
        rootGallery.AddChild(nestedGallery);

        Assert.Throws<ArgumentException>(() => leafBook.AddChild(rootBook));
        Assert.Throws<ArgumentException>(() => nestedGallery.AddChild(rootGallery));
    }

    [Fact]
    public void CreditsCapabilityReferencesTheSamePersonInstance() {
        var person = new Person(
            Guid.Parse("88888888-8888-8888-8888-888888888888"),
            "Ada Person");
        var video = new Video(
            Guid.Parse("99999999-9999-9999-9999-999999999999"),
            "Episode 1");

        video.Credits!.Add(person, CreditRole.Actor, "Detective");
        person.Rename("Ada Renamed");

        var credit = Assert.Single(video.Credits.Credits);
        Assert.Same(person, credit.Person);
        Assert.Equal("Ada Renamed", credit.Person.Title);
        Assert.Equal(CreditRole.Actor, credit.Role);
        Assert.Equal("Detective", credit.Label);
    }

    [Fact]
    public void CreditsCapabilityFiltersCreditsByRole() {
        var actor = new Person(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            "Ada Actor");
        var director = new Person(
            Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff"),
            "Drew Director");
        var series = new VideoSeries(
            Guid.Parse("cccccccc-dddd-eeee-ffff-000000000000"),
            "Series");

        series.Credits!.Add(actor, CreditRole.Actor, "Lead");
        series.Credits.Add(director, CreditRole.Director);

        Assert.Equal([actor], series.Credits.ForRole(CreditRole.Actor).Select(credit => credit.Person));
        Assert.Equal([director], series.Credits.ForRole(CreditRole.Director).Select(credit => credit.Person));
    }

    [Fact]
    public void EntityUsesItsDiscoveredDefinitionForDefaultCapabilities() {
        var video = new Video(
            Guid.Parse("dddddddd-eeee-ffff-0000-111111111111"),
            "Episode 1");
        var image = new Image(
            Guid.Parse("eeeeeeee-ffff-0000-1111-222222222222"),
            "Still");

        Assert.Same(EntityKindRegistry.Describe(EntityKind.Video), video.Definition);
        Assert.Same(EntityKindRegistry.Describe(EntityKind.Image), image.Definition);
        Assert.NotNull(video.Credits);
        Assert.Empty(image.Capabilities);
    }

    [Fact]
    public void VideoSeriesRetainsDirectEpisodesWhenItAlsoHasSeasons() {
        var series = new VideoSeries(Guid.NewGuid(), "Series");
        var season = new VideoSeason(Guid.NewGuid(), "Season 1", series.Id);
        var directEpisode = new VideoEpisode(Guid.NewGuid(), "Special", series.Id);

        series.AddChild(season);
        series.AddChild(directEpisode);

        Assert.Equal(VideoSeriesRenderingMode.Mixed, series.RenderingMode);
        Assert.Same(directEpisode, Assert.Single(series.Episodes));
        Assert.Same(season, Assert.Single(series.Seasons));
    }
}
