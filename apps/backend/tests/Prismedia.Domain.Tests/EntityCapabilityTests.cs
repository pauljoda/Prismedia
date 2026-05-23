using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Domain.Taxonomy;

namespace Prismedia.Domain.Tests;

public sealed class EntityCapabilityTests {
    [Fact]
    public void GetCapabilityReturnsAttachedReference() {
        var rating = new CapabilityRating();
        var video = new Video(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "Projected Video",
            subtitlesExtractedAt: null,
            capabilities: [rating]);

        var attached = video.GetCapability<CapabilityRating>();

        Assert.Same(rating, attached);
    }

    [Fact]
    public void MutatingReturnedCapabilityMutatesEntityState() {
        var video = new Video(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "Projected Video",
            subtitlesExtractedAt: null,
            capabilities: [new CapabilityRating()]);

        video.GetCapability<CapabilityRating>()!.Rate(4);

        Assert.Equal(4, video.GetCapability<CapabilityRating>()!.Value);
        Assert.Equal(4, video.Rating!.Value);
    }

    [Fact]
    public void EntityRejectsDuplicateCapabilityKinds() {
        var ex = Assert.Throws<ArgumentException>(() => new Video(
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            "Projected Video",
            subtitlesExtractedAt: null,
            capabilities: [new CapabilityRating(1), new CapabilityRating(2)]));

        Assert.Contains(nameof(CapabilityRating), ex.Message);
    }

    [Fact]
    public void MissingCapabilityHelpersExposeOptionalAndRequiredPaths() {
        var image = new Image(
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            "Projected Image",
            capabilities: []);

        Assert.False(image.HasCapability<CapabilityRating>());
        Assert.Null(image.GetCapability<CapabilityRating>());
        Assert.Throws<InvalidOperationException>(() => image.RequireCapability<CapabilityRating>());
    }

    [Fact]
    public void RemoveCapabilityDetachesTheCapabilityFromTheEntity() {
        var rating = new CapabilityRating(5);
        var image = new Image(
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            "Projected Image",
            capabilities: [rating]);

        var removed = image.RemoveCapability<CapabilityRating>();

        Assert.True(removed);
        Assert.Null(image.GetCapability<CapabilityRating>());
    }

    [Fact]
    public void EntityChildrenCanBeAddedAndReturnedByConcreteType() {
        var season = new VideoSeason(
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            "Season 1",
            parentEntityId: null);
        var episode = new Video(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Episode 1",
            subtitlesExtractedAt: null);
        var series = new VideoSeries(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "Series");

        series.AddChild(season);
        series.AddChild(episode);

        Assert.Same(season, Assert.Single(series.ChildrenOf<VideoSeason>()));
        Assert.Same(episode, Assert.Single(series.ChildrenOf<Video>()));
        Assert.Equal([season, episode], series.ChildEntities);
        Assert.Equal([season], series.ChildrenByKind[EntityKind.VideoSeason]);
        Assert.Equal([episode], series.ChildrenByKind[EntityKind.Video]);
    }

    [Fact]
    public void EntityRelationshipsCanBeAddedAndReturnedByKindAndConcreteType() {
        var video = new Video(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "Episode 1",
            subtitlesExtractedAt: null);
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
        var video = new Video(
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            "Episode 1",
            subtitlesExtractedAt: null);
        var series = new VideoSeries(
            Guid.Parse("77777777-7777-7777-7777-777777777777"),
            "Series");

        series.AddChild(video);
        series.AddRelationship(video);

        Assert.Throws<ArgumentException>(() => series.AddChild(video));
        Assert.Throws<ArgumentException>(() => series.AddRelationship(video));
    }

    [Fact]
    public void CreditsCapabilityReferencesTheSamePersonInstance() {
        var person = new Person(
            Guid.Parse("88888888-8888-8888-8888-888888888888"),
            "Ada Person");
        var video = new Video(
            Guid.Parse("99999999-9999-9999-9999-999999999999"),
            "Episode 1",
            subtitlesExtractedAt: null);

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
    public void EntityRequiresConcreteTypesToProvideDefaultCapabilities() {
        var factory = typeof(Entity).GetMethod("CreateDefaultCapabilities", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var video = new Video(
            Guid.Parse("dddddddd-eeee-ffff-0000-111111111111"),
            "Episode 1",
            subtitlesExtractedAt: null);
        var image = new Image(
            Guid.Parse("eeeeeeee-ffff-0000-1111-222222222222"),
            "Still");

        Assert.NotNull(factory);
        Assert.True(factory!.IsAbstract);
        Assert.NotNull(video.Credits);
        Assert.NotNull(image.Rating);
    }
}
