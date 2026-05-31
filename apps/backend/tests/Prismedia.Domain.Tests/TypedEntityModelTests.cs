using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Domain.Taxonomy;

namespace Prismedia.Domain.Tests;

public sealed class TypedEntityModelTests {
    private static CapabilityTechnical MakeTechnical(
        TimeSpan? duration = null, int? width = null, int? height = null, string? codec = null) {
        var technical = new CapabilityTechnical();
        technical.Apply(duration: duration, width: width, height: height, codec: codec);
        return technical;
    }

    [Theory]
    [InlineData(typeof(Video), EntityKind.Video)]
    [InlineData(typeof(Movie), EntityKind.Movie)]
    [InlineData(typeof(VideoSeries), EntityKind.VideoSeries)]
    [InlineData(typeof(VideoSeason), EntityKind.VideoSeason)]
    [InlineData(typeof(AudioLibrary), EntityKind.AudioLibrary)]
    [InlineData(typeof(AudioTrack), EntityKind.AudioTrack)]
    [InlineData(typeof(Book), EntityKind.Book)]
    [InlineData(typeof(BookVolume), EntityKind.BookVolume)]
    [InlineData(typeof(BookChapter), EntityKind.BookChapter)]
    [InlineData(typeof(BookPage), EntityKind.BookPage)]
    [InlineData(typeof(Collection), EntityKind.Collection)]
    [InlineData(typeof(Gallery), EntityKind.Gallery)]
    [InlineData(typeof(Image), EntityKind.Image)]
    [InlineData(typeof(Person), EntityKind.Person)]
    [InlineData(typeof(Studio), EntityKind.Studio)]
    [InlineData(typeof(Tag), EntityKind.Tag)]
    public void ConcreteEntitiesInheritTheSharedAbstractEntityRoot(Type aggregateType, EntityKind kind) {
        Assert.True(typeof(Entity).IsAssignableFrom(aggregateType));
        Assert.True(typeof(Entity).IsAbstract);
        Assert.Equal(kind, EntityKindRegistry.RequireType(aggregateType));
    }

    [Fact]
    public void PersonOwnsPersonSpecificDetailsAndMutators() {
        var person = new Person(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "Ada Person",
            capabilities: [new CapabilityDescription("Profile"), new CapabilityLifetime()]);

        person.SetCountry("US");
        person.RequireCapability<CapabilityLifetime>();

        Assert.Equal(EntityKind.Person, person.Kind);
        Assert.Equal("US", person.Country);
        Assert.Equal("Profile", person.Description!.Value);
    }

    [Fact]
    public void AudioTrackOwnsPlaybackAwareAudioDetails() {
        var track = new AudioTrack(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "Main Theme",
            embeddedArtist: null,
            embeddedAlbum: null,
            capabilities:
            [
                new CapabilityPlayback(),
                MakeTechnical(duration: TimeSpan.FromSeconds(90), codec: "flac")
            ]);

        track.MarkPlayed(TimeSpan.FromSeconds(45), DateTimeOffset.Parse("2026-05-12T12:00:00Z"));

        Assert.Equal(EntityKind.AudioTrack, track.Kind);
        Assert.Equal("flac", track.Technical!.Codec);
        Assert.Equal(1, track.Playback!.PlayCount);
        Assert.Equal(TimeSpan.FromSeconds(45), track.Playback.ResumeTime);
    }

    [Fact]
    public void VideoSubtypeExposesSpecificFieldsDirectly() {
        var video = new Video(
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            "Feature",
            capabilities:
            [
                new CapabilityDescription("Direct description"),
                MakeTechnical(duration: TimeSpan.FromMinutes(2), width: 1920, height: 1080),
                new CapabilityClassification("PG"),
                new CapabilityPlayback(),
                new CapabilityMarkers(),
                new CapabilitySubtitles()
            ]);

        Entity entity = video;

        Assert.Equal(video.Id, entity.Id);
        Assert.Equal(EntityKind.Video, entity.Kind);
        Assert.Equal("Direct description", video.Description!.Value);
        Assert.Equal(TimeSpan.FromMinutes(2), video.Technical!.Duration);
        Assert.Equal("PG", video.Classification!.Value);
        Assert.True(video.HasCapability<CapabilityPlayback>());
    }
}
