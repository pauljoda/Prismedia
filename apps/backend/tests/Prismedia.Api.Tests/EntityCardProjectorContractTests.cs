using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Domain.Taxonomy;
using ContractEntityCapability = Prismedia.Contracts.Entities.EntityCapability;

namespace Prismedia.Api.Tests;

public sealed class EntityCardProjectorContractTests {
    [Fact]
    public void ProjectsLogoBeforeBackdropForThumbnailCoverUrls() {
        var studio = new Studio(Guid.NewGuid(), "GameChops");
        studio.AttachFile(EntityFileRole.Backdrop, "/assets/plugins/artwork/gamechops/banner.webp", "image/webp");
        studio.AttachFile(EntityFileRole.Logo, "/assets/plugins/artwork/gamechops/logo.webp", "image/webp");

        var card = EntityCardProjector.ToCard(studio, hasSourceBackedSubtree: false);
        var images = AssertCapability<ImagesCapability>(card);

        Assert.Equal("/assets/plugins/artwork/gamechops/logo.webp", images.CoverUrl);
        Assert.Equal("/assets/plugins/artwork/gamechops/logo.webp", images.ThumbnailUrl);
        Assert.Equal(["logo", "backdrop"], images.Items.Select(item => item.Kind));
    }

    [Fact]
    public void ProjectsFileManagementOnlyForSourceBackedSafeManagedTreeRoots() {
        var emptyMovie = EntityCardProjector.ToCard(new Movie(Guid.NewGuid(), "Wanted Arrival"), hasSourceBackedSubtree: false);
        var sourceBackedMovie = EntityCardProjector.ToCard(new Movie(Guid.NewGuid(), "Arrival"), hasSourceBackedSubtree: true);
        var archiveChapter = new BookChapter(Guid.NewGuid(), "Chapter 1", coverPageId: null);
        archiveChapter.AttachFile(EntityFileRole.Source, "/media/books/Arrival.cbz::001.jpg", "image/jpeg");
        var archivePage = new BookPage(Guid.NewGuid(), "Page 1");
        archivePage.AttachFile(EntityFileRole.Source, "/media/books/Arrival.cbz::001.jpg", "image/jpeg");
        var studio = EntityCardProjector.ToCard(new Studio(Guid.NewGuid(), "Paramount"), hasSourceBackedSubtree: false);

        Assert.Empty(emptyMovie.Capabilities.OfType<FileManagementCapability>());
        Assert.False(emptyMovie.HasSourceMedia);
        Assert.True(sourceBackedMovie.HasSourceMedia);
        Assert.True(AssertCapability<FileManagementCapability>(sourceBackedMovie).CanDeleteFiles);
        Assert.Empty(EntityCardProjector.ToCard(archiveChapter, hasSourceBackedSubtree: true).Capabilities.OfType<FileManagementCapability>());
        Assert.Empty(EntityCardProjector.ToCard(archivePage, hasSourceBackedSubtree: true).Capabilities.OfType<FileManagementCapability>());
        Assert.Empty(studio.Capabilities.OfType<FileManagementCapability>());
    }

    [Fact]
    public void ProjectsPlayableVideoOnlyForPlayableDefinitionsWithTheirOwnSourceFile() {
        var movie = new Movie(Guid.NewGuid(), "Arrival");
        movie.AttachFile(EntityFileRole.Source, "/media/movies/Arrival.mkv", "video/x-matroska");
        var standalone = new Video(Guid.NewGuid(), "Clip");
        standalone.AttachFile(EntityFileRole.Source, "/media/videos/Clip.mkv", "video/x-matroska");
        var episode = new VideoEpisode(Guid.NewGuid(), "Pilot", Guid.NewGuid());
        episode.AttachFile(EntityFileRole.Source, "/media/tv/Pilot.mkv", "video/x-matroska");
        var series = new VideoSeries(Guid.NewGuid(), "Series");
        series.AttachFile(EntityFileRole.Source, "/media/tv/Series.mkv", "video/x-matroska");

        AssertCapability<PlayableVideoCapability>(EntityCardProjector.ToCard(movie, hasSourceBackedSubtree: true));
        AssertCapability<PlayableVideoCapability>(EntityCardProjector.ToCard(standalone, hasSourceBackedSubtree: true));
        AssertCapability<PlayableVideoCapability>(EntityCardProjector.ToCard(episode, hasSourceBackedSubtree: true));
        Assert.Empty(EntityCardProjector.ToCard(series, hasSourceBackedSubtree: true)
            .Capabilities.OfType<PlayableVideoCapability>());
    }

    [Fact]
    public void ProjectsFileManagementForARecoverableDeletionWithoutClaimingSourceMedia() {
        var wantedMovie = new Movie(Guid.NewGuid(), "Wanted Arrival");

        var recovering = EntityCardProjector.ToCard(
            wantedMovie,
            new EntityFileManagementState(
                HasSourceBackedSubtree: false,
                HasRecoverableDeletion: true));
        var ordinaryWanted = EntityCardProjector.ToCard(
            wantedMovie,
            new EntityFileManagementState(
                HasSourceBackedSubtree: false,
                HasRecoverableDeletion: false));

        Assert.False(recovering.HasSourceMedia);
        Assert.True(AssertCapability<FileManagementCapability>(recovering).CanDeleteFiles);
        Assert.Empty(ordinaryWanted.Capabilities.OfType<FileManagementCapability>());
    }

    [Fact]
    public void ProjectsKindDataThroughCapabilitiesInsteadOfDerivedDetailShapes() {
        var coverPageId = Guid.NewGuid();
        var book = new Book(
            Guid.NewGuid(),
            "The Left Hand of Darkness",
            BookType.Novel,
            coverPageId,
            BookFormat.Epub);

        var card = EntityCardProjector.ToCard(book, hasSourceBackedSubtree: false);

        var metadata = AssertCapability<BookMetadataCapability>(card);
        Assert.Equal(BookType.Novel, metadata.BookType);
        Assert.Equal(BookFormat.Epub, metadata.Format);
        Assert.Equal(coverPageId, AssertCapability<CoverSelectionCapability>(card).EntityId);
    }

    [Fact]
    public void ProjectsSharedCreditsAndSubtitleStateAsCapabilities() {
        var extractedAt = DateTimeOffset.UtcNow;
        var director = new Person(Guid.NewGuid(), "Jane Doe");
        var video = new Video(Guid.NewGuid(), "Feature");
        video.Credits!.Add(director, CreditRole.Director);
        video.SubtitleCapability!.MarkExtracted(extractedAt);

        var card = EntityCardProjector.ToCard(video, hasSourceBackedSubtree: false);

        var credit = Assert.Single(AssertCapability<CreditsCapability>(card).Items);
        Assert.Equal(director.Id, credit.PersonId);
        Assert.Equal(CreditRole.Director.ToCode(), credit.Role);
        Assert.Equal(extractedAt, AssertCapability<SubtitlesCapability>(card).ExtractedAt);
    }

    [Fact]
    public void ProjectsCallerSpecificCollectionConfigurationAsACapability() {
        var ownerId = Guid.NewGuid();
        var coverItemId = Guid.NewGuid();
        var refreshedAt = DateTimeOffset.UtcNow;
        var collection = new Collection(
            Guid.NewGuid(),
            "Favorites",
            ownerId,
            CollectionMode.Dynamic,
            "{\"all\":[]}",
            CollectionCoverMode.Item,
            coverItemId,
            refreshedAt,
            isShared: true);

        var card = EntityCardProjector.ToCard(
            collection,
            new EntityFileManagementState(false, false),
            currentUserId: ownerId);

        var configuration = AssertCapability<CollectionConfigurationCapability>(card);
        Assert.True(configuration.IsShared);
        Assert.True(configuration.CanEdit);
        Assert.Equal(CollectionMode.Dynamic, configuration.Mode);
        Assert.Equal(refreshedAt, configuration.LastRefreshedAt);
        Assert.Equal(coverItemId, AssertCapability<CoverSelectionCapability>(card).EntityId);
    }

    private static TCapability AssertCapability<TCapability>(EntityCard card)
        where TCapability : ContractEntityCapability =>
        Assert.IsType<TCapability>(Assert.Single(card.Capabilities.OfType<TCapability>()));
}
