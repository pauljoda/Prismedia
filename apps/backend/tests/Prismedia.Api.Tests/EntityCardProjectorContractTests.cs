using System.Text.Json;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Domain.Taxonomy;
using ContractEntityCapability = Prismedia.Contracts.Entities.EntityCapability;
using DomainEntityDate = Prismedia.Domain.Capabilities.EntityDate;

namespace Prismedia.Api.Tests;

public sealed class EntityCardProjectorContractTests {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ProjectsFileAndLinkCapabilitiesToContractOwnedWireShape() {
        var video = new Video(Guid.NewGuid(), "Contract Video");
        video.AttachFile(EntityFileRole.Source, "/media/video.mkv", "video/x-matroska");
        video.AddUrl("https://example.test/video", "Example");
        video.SetExternalId("tmdb", "123", "https://tmdb.test/123");

        var card = EntityCardProjector.ToCard(video);

        var files = AssertCapability<FilesCapability>(card);
        var file = Assert.Single(files.Items);
        Assert.Equal("source", file.Role);
        Assert.Equal("/media/video.mkv", file.Path);
        Assert.Equal("video/x-matroska", file.MimeType);
        Assert.DoesNotContain("Prismedia.Domain", file.GetType().FullName, StringComparison.Ordinal);

        var links = AssertCapability<LinksCapability>(card);
        Assert.Equal("https://example.test/video", Assert.Single(links.Urls).Value);
        Assert.Equal("tmdb", Assert.Single(links.ExternalIds).Provider);

        var json = JsonSerializer.Serialize(new { files, links }, JsonOptions);
        Assert.Contains("\"role\":\"source\"", json, StringComparison.Ordinal);
        Assert.Contains("\"provider\":\"tmdb\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectsDomainCapabilityItemsToContractOwnedCodeValues() {
        var video = new Video(Guid.NewGuid(), "Capability Video");
        video.AttachFile(EntityFileRole.Poster, "/art/poster.jpg", "image/jpeg");
        video.RequireCapability<CapabilityMarkers>().Add("Intro", 12.5, 20);
        video.RequireCapability<CapabilitySubtitles>().Add(
            "en",
            "English",
            "vtt",
            EntitySubtitleSource.Sidecar,
            "/subs/en.vtt",
            "srt",
            "/media/en.srt",
            isDefault: true);
        video.RequireCapability<CapabilityPosition>().Set("episode", 3, "Episode 3");

        var fingerprints = new CapabilityFingerprints();
        fingerprints.Set(FingerprintAlgorithm.Md5, "abc123");
        video.AddCapability(fingerprints);

        var stats = new CapabilityStats();
        stats.Set("chapters", 4);
        video.AddCapability(stats);

        var dates = new CapabilityDates();
        dates.Set("release", "2024-05-01", new DateOnly(2024, 5, 1), "day");
        video.AddCapability(dates);
        video.AddCapability(new CapabilityLifetime(new DomainEntityDate("career-start", "2020", new DateOnly(2020, 1, 1), "year")));

        var source = new CapabilitySource();
        source.Add("file", "/media/video.mkv");
        video.AddCapability(source);

        var card = EntityCardProjector.ToCard(video);

        Assert.Equal("poster", Assert.Single(AssertCapability<ImagesCapability>(card).Items).Kind);
        Assert.Equal("Intro", Assert.Single(AssertCapability<MarkersCapability>(card).Items).Title);
        Assert.Equal(EntitySubtitleSource.Sidecar, Assert.Single(AssertCapability<SubtitlesCapability>(card).Items).Source);
        Assert.Equal("md5", Assert.Single(AssertCapability<FingerprintsCapability>(card).Items).Algorithm);
        Assert.Equal("chapters", Assert.Single(AssertCapability<StatsCapability>(card).Items).Code);
        Assert.Equal("release", Assert.Single(AssertCapability<DatesCapability>(card).Items).Code);
        Assert.Equal("career-start", AssertCapability<LifetimeCapability>(card).Start?.Code);
        Assert.Equal("file", Assert.Single(AssertCapability<SourceCapability>(card).Items).Code);
        Assert.Equal("episode", Assert.Single(AssertCapability<PositionCapability>(card).Items).Code);
    }

    [Fact]
    public void ProjectsLogoBeforeBackdropForThumbnailCoverUrls() {
        var studio = new Studio(Guid.NewGuid(), "GameChops");
        studio.AttachFile(EntityFileRole.Backdrop, "/assets/plugins/artwork/gamechops/banner.webp", "image/webp");
        studio.AttachFile(EntityFileRole.Logo, "/assets/plugins/artwork/gamechops/logo.webp", "image/webp");

        var card = EntityCardProjector.ToCard(studio);
        var images = AssertCapability<ImagesCapability>(card);

        Assert.Equal("/assets/plugins/artwork/gamechops/logo.webp", images.CoverUrl);
        Assert.Equal("/assets/plugins/artwork/gamechops/logo.webp", images.ThumbnailUrl);
        Assert.Equal(["logo", "backdrop"], images.Items.Select(item => item.Kind));
    }

    private static TCapability AssertCapability<TCapability>(EntityCard card)
        where TCapability : ContractEntityCapability =>
        Assert.IsType<TCapability>(Assert.Single(card.Capabilities.OfType<TCapability>()));
}
