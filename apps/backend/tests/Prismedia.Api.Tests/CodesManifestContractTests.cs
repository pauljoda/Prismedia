using Prismedia.Api.Codegen;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Tests;

public sealed class CodesManifestContractTests {
    [Fact]
    public void ThumbnailMetaIconManifestCarriesExactStructuralUnits() {
        var icons = CodesManifest.Build().ThumbnailMetaIcons
            .Select(icon => icon.Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(EntityThumbnailMetaIcons.Season, icons);
        Assert.Contains(EntityThumbnailMetaIcons.Episode, icons);
        Assert.Contains(EntityThumbnailMetaIcons.Volume, icons);
        Assert.Contains(EntityThumbnailMetaIcons.Chapter, icons);
        Assert.Contains(EntityThumbnailMetaIcons.Page, icons);
        Assert.Contains(EntityThumbnailMetaIcons.Album, icons);
        Assert.Contains(EntityThumbnailMetaIcons.Track, icons);
    }

    [Fact]
    public void EntityKindManifestCarriesTheRegistryFileDeletionPolicy() {
        var kinds = CodesManifest.Build().EntityKinds.ToDictionary(kind => kind.Code);

        Assert.True(kinds[EntityKind.Audio.ToCode()].SupportsFileDeletion);
        Assert.True(kinds[EntityKind.VideoSeries.ToCode()].SupportsFileDeletion);
        Assert.False(kinds[EntityKind.BookChapter.ToCode()].SupportsFileDeletion);
        Assert.False(kinds[EntityKind.BookPage.ToCode()].SupportsFileDeletion);
        Assert.False(kinds[EntityKind.Collection.ToCode()].SupportsFileDeletion);
    }

    [Fact]
    public void EntityKindManifestProjectsDefinitionOwnedPresentation() {
        var kinds = CodesManifest.Build().EntityKinds.ToDictionary(kind => kind.Code);

        var video = kinds[EntityKind.Video.ToCode()];
        Assert.Equal(EntityKindIcon.Video.ToCode(), video.Icon);
        Assert.Equal(EntityKindIcon.Video.ToCode(), video.ReferenceIcon);
        Assert.Equal(16, video.ThumbnailWidth);
        Assert.Equal(9, video.ThumbnailHeight);
        Assert.Equal(EntityAccentHue.Red.ToCode(), video.PrimaryAccent);
        Assert.Equal(EntityAccentHue.Orange.ToCode(), video.SecondaryAccent);
        Assert.Equal(EntityArtworkFit.Cover.ToCode(), video.ArtworkFit);

        var author = kinds[EntityKind.BookAuthor.ToCode()];
        Assert.Equal(EntityKindIcon.Author.ToCode(), author.Icon);
        Assert.Equal(EntityKindIcon.Book.ToCode(), author.ReferenceIcon);
        Assert.Equal(2, author.ThumbnailWidth);
        Assert.Equal(3, author.ThumbnailHeight);

        Assert.Equal(
            EntityArtworkFit.Contain.ToCode(),
            kinds[EntityKind.Studio.ToCode()].ArtworkFit);
    }

    [Fact]
    public void EntityKindManifestProjectsDefinitionOwnedNavigationAndSearch() {
        var kinds = CodesManifest.Build().EntityKinds.ToDictionary(kind => kind.Code);
        var searchableCodes = kinds.Values
            .Where(kind => kind.Search is not null)
            .OrderBy(kind => kind.Search!.Order)
            .Select(kind => kind.Code)
            .ToArray();

        Assert.Equal([
            EntityKind.Movie.ToCode(),
            EntityKind.VideoSeries.ToCode(),
            EntityKind.Video.ToCode(),
            EntityKind.Person.ToCode(),
            EntityKind.Studio.ToCode(),
            EntityKind.Tag.ToCode(),
            EntityKind.Gallery.ToCode(),
            EntityKind.Book.ToCode(),
            EntityKind.Image.ToCode(),
            EntityKind.Collection.ToCode(),
            EntityKind.AudioLibrary.ToCode(),
            EntityKind.AudioTrack.ToCode()
        ], searchableCodes);

        Assert.True(kinds[EntityKind.Person.ToCode()].Search!.ExpandsRelationshipResults);
        Assert.False(kinds[EntityKind.Movie.ToCode()].Search!.ExpandsRelationshipResults);

        var season = kinds[EntityKind.VideoSeason.ToCode()].Navigation!;
        Assert.Equal(EntityKind.VideoSeries.ToCode(), season.CanonicalBrowseKind);
        Assert.Equal("series", season.DestinationId);
        Assert.Equal("/series", season.BrowsePath);
        Assert.Equal("/series/{parentId}/seasons/{id}", season.DetailPathTemplate);
        Assert.Equal(EntityKind.VideoSeries.ToCode(), season.RequiredAncestorKind);
        Assert.False(season.IsTopLevel);
    }

    [Fact]
    public void EntityKindManifestDerivesRequestSupportFromTheRequestKindRegistry() {
        var manifestKinds = CodesManifest.Build().EntityKinds
            .Where(kind => kind.SupportsRequests)
            .Select(kind => EntityKindRegistry.Require(kind.Code))
            .ToHashSet();
        var registryKinds = RequestKindRegistry.All
            .Where(descriptor => descriptor.Committable)
            .Select(descriptor => descriptor.WantedEntityKind)
            .ToHashSet();

        Assert.Equal(registryKinds.Order(), manifestKinds.Order());
        Assert.Contains(EntityKind.Book, manifestKinds);
        Assert.Contains(EntityKind.VideoSeason, manifestKinds);
        Assert.Contains(EntityKind.AudioLibrary, manifestKinds);
        Assert.Contains(EntityKind.AudioTrack, manifestKinds);
        Assert.DoesNotContain(EntityKind.BookVolume, manifestKinds);
    }

    [Fact]
    public void RequestKindManifestIsProjectedFromTheCanonicalRegistry() {
        var manifest = CodesManifest.Build().RequestKinds;

        Assert.Equal(RequestKindRegistry.All.Count, manifest.Count);
        Assert.Equal(
            RequestKindRegistry.All.Select(descriptor => descriptor.Kind.ToCode()),
            manifest.Select(kind => kind.Kind));

        var book = Assert.Single(manifest, kind => kind.Kind == RequestMediaKind.Book.ToCode());
        Assert.Equal("Book", book.Label);
        Assert.Equal("Books", book.Plural);
        Assert.Equal("volume", book.ChildNoun);
        Assert.Equal(EntityKind.Book.ToCode(), book.EntityKind);
        Assert.Equal(EntityKind.Book.ToCode(), book.PluginEntityKind);
        Assert.Equal(EntityKind.Book.ToCode(), book.AcquisitionKind);
        Assert.Equal(EntityKind.Book.ToCode(), book.ProfileKind);
        Assert.Equal(LibraryRootMediaCapability.ScanBooks.ToCode(), book.RootFlag);
        Assert.Equal(RequestReviewSelection.DirectChildrenWhenPresent.ToCode(), book.ReviewSelection);

        var episode = Assert.Single(manifest, kind => kind.Kind == RequestMediaKind.Episode.ToCode());
        Assert.False(episode.Discoverable);
        Assert.Null(episode.ChildNoun);
        Assert.Equal(EntityKind.Video.ToCode(), episode.AcquisitionKind);
        Assert.Equal(EntityKind.VideoSeries.ToCode(), episode.ProfileKind);
        Assert.Equal(LibraryRootMediaCapability.ScanVideos.ToCode(), episode.RootFlag);
        Assert.Equal(RequestReviewSelection.Root.ToCode(), episode.ReviewSelection);
    }
}
