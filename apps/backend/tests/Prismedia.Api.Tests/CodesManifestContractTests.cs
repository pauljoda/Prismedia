using Prismedia.Api.Codegen;
using Prismedia.Application.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Tests;

public sealed class CodesManifestContractTests {
    [Fact]
    public void EntityKindManifestCarriesTheRegistryFileDeletionPolicy() {
        var kinds = CodesManifest.Build().EntityKinds.ToDictionary(kind => kind.Code);

        Assert.True(kinds[EntityKindRegistry.Audio.Code].SupportsFileDeletion);
        Assert.True(kinds[EntityKindRegistry.VideoSeries.Code].SupportsFileDeletion);
        Assert.False(kinds[EntityKindRegistry.BookChapter.Code].SupportsFileDeletion);
        Assert.False(kinds[EntityKindRegistry.BookPage.Code].SupportsFileDeletion);
        Assert.False(kinds[EntityKindRegistry.Collection.Code].SupportsFileDeletion);
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
        Assert.DoesNotContain(EntityKind.AudioTrack, manifestKinds);
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
