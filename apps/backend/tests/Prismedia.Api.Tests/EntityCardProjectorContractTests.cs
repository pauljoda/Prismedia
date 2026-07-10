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
        var video = new Video(Guid.NewGuid(), "Arrival");
        video.AttachFile(EntityFileRole.Source, "/media/movies/Arrival/Arrival.mkv", "video/x-matroska");
        var sourceBackedMovie = EntityCardProjector.ToCard(new Movie(Guid.NewGuid(), "Arrival", [video]), hasSourceBackedSubtree: true);
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

    private static TCapability AssertCapability<TCapability>(EntityCard card)
        where TCapability : ContractEntityCapability =>
        Assert.IsType<TCapability>(Assert.Single(card.Capabilities.OfType<TCapability>()));
}
