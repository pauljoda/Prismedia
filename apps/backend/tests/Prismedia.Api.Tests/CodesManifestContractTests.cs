using Prismedia.Api.Codegen;
using Prismedia.Application.Requests;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using System.Reflection;

namespace Prismedia.Api.Tests;

public sealed class CodesManifestContractTests {
    [Fact]
    public void SettingsManifestUsesExactlyTheDiscoveredDefinitions() {
        var expected = AppSettingsRegistry.DefinitionsByClientName
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new ConstantEntry(entry.Key, entry.Value.Key));

        Assert.Equal(expected, CodesManifest.Build().SettingKeys);
    }

    [Fact]
    public void EveryCodeFamilyOwnsUniqueGeneratedClientNames() {
        var manifest = CodesManifest.Build();

        Assert.Equal(
            manifest.Enums.Keys.Order(StringComparer.Ordinal),
            manifest.CodeFamilies.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(
            manifest.CodeFamilies.Count,
            manifest.CodeFamilies.Values.Select(family => family.ConstantName).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            manifest.CodeFamilies.Count,
            manifest.CodeFamilies.Values.Select(family => family.TypeName).Distinct(StringComparer.Ordinal).Count());

        Assert.Equal(
            new CodeFamilyManifestEntry("RELATIONSHIP_CODE", "RelationshipCode"),
            manifest.CodeFamilies[nameof(RelationshipKind)]);
        Assert.Equal(
            new CodeFamilyManifestEntry("SUBTITLE_SOURCE", "SubtitleSourceCode"),
            manifest.CodeFamilies[nameof(EntitySubtitleSource)]);
        Assert.Equal(
            new CodeFamilyManifestEntry("ENTITY_KIND", "EntityKindCode"),
            manifest.CodeFamilies[nameof(EntityKind)]);
    }

    [Fact]
    public void CapabilityManifestDiscoversSharedPlaybackAndSequenceContracts() {
        var capabilityKinds = CodesManifest.Build().CapabilityKinds;
        var playableVideoKind = typeof(PlayableVideoCapability)
            .GetCustomAttribute<CapabilityKindAttribute>()!
            .Kind;
        var playableAudioKind = typeof(PlayableAudioCapability)
            .GetCustomAttribute<CapabilityKindAttribute>()!
            .Kind;
        var orderedSequenceKind = typeof(OrderedSequenceCapability)
            .GetCustomAttribute<CapabilityKindAttribute>()!
            .Kind;
        var pageSequenceKind = typeof(PageSequenceCapability)
            .GetCustomAttribute<CapabilityKindAttribute>()!
            .Kind;

        Assert.Contains(playableVideoKind, capabilityKinds);
        Assert.Contains(playableAudioKind, capabilityKinds);
        Assert.Contains(orderedSequenceKind, capabilityKinds);
        Assert.Contains(pageSequenceKind, capabilityKinds);
    }

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
        Assert.Equal(EntityArtworkSurface.Plain.ToCode(), video.ArtworkSurface);

        var author = kinds[EntityKind.BookAuthor.ToCode()];
        Assert.Equal(EntityKindIcon.Author.ToCode(), author.Icon);
        Assert.Equal(EntityKindIcon.Book.ToCode(), author.ReferenceIcon);
        Assert.Equal(2, author.ThumbnailWidth);
        Assert.Equal(3, author.ThumbnailHeight);

        Assert.Equal(
            EntityArtworkFit.Contain.ToCode(),
            kinds[EntityKind.Studio.ToCode()].ArtworkFit);
        Assert.Equal(
            EntityArtworkSurface.BrandPlate.ToCode(),
            kinds[EntityKind.Studio.ToCode()].ArtworkSurface);

        var collection = kinds[EntityKind.Collection.ToCode()];
        Assert.Equal(1, collection.ThumbnailWidth);
        Assert.Equal(1, collection.ThumbnailHeight);
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
            EntityKind.ComicSeries.ToCode(),
            EntityKind.Image.ToCode(),
            EntityKind.Collection.ToCode(),
            EntityKind.AudioLibrary.ToCode(),
            EntityKind.AudioTrack.ToCode()
        ], searchableCodes);

        Assert.True(kinds[EntityKind.Person.ToCode()].Search!.ExpandsRelationshipResults);
        Assert.False(kinds[EntityKind.Movie.ToCode()].Search!.ExpandsRelationshipResults);

        var comicInstallment = kinds[EntityKind.ComicInstallment.ToCode()];
        Assert.Equal(EntityKind.ComicSeries.ToCode(), comicInstallment.Navigation!.CanonicalBrowseKind);
        Assert.Equal("/comics/{parentId}/installments/{id}", comicInstallment.Navigation.DetailPathTemplate);
        Assert.Equal(EntityKind.ComicSeries.ToCode(), comicInstallment.Navigation.RequiredAncestorKind);

        var season = kinds[EntityKind.VideoSeason.ToCode()].Navigation!;
        Assert.Equal(EntityKind.VideoSeries.ToCode(), season.CanonicalBrowseKind);
        Assert.Equal("series", season.DestinationId);
        Assert.Equal("/series", season.BrowsePath);
        Assert.Equal("/series/{parentId}/seasons/{id}", season.DetailPathTemplate);
        Assert.Equal(EntityKind.VideoSeries.ToCode(), season.RequiredAncestorKind);
        Assert.False(season.IsTopLevel);
    }

    [Fact]
    public void EntityKindManifestProjectsDefinitionOwnedClientPolicies() {
        var manifest = CodesManifest.Build().EntityKinds;
        var kinds = manifest.ToDictionary(kind => kind.Code);
        var collectionPolicy = Assert.IsAssignableFrom<IEntityContainmentPolicy>(
            EntityKindRegistry.Describe(EntityKind.Collection));

        Assert.Equal(
            collectionPolicy.ContainableKinds.Select(kind => kind.ToCode()),
            kinds[EntityKind.Collection.ToCode()].ContainableKinds);
        Assert.Contains(EntityKind.ComicSeries, collectionPolicy.ContainableKinds);
        Assert.Contains(EntityKind.ComicInstallment, collectionPolicy.ContainableKinds);
        Assert.DoesNotContain(EntityKind.ComicVolume, collectionPolicy.ContainableKinds);
        Assert.All(
            manifest.Where(kind => kind.Code != EntityKind.Collection.ToCode()),
            kind => Assert.Null(kind.ContainableKinds));

        Assert.True(kinds[EntityKind.Person.ToCode()].SupportsManualManagement);
        Assert.True(kinds[EntityKind.Studio.ToCode()].SupportsManualManagement);
        Assert.True(kinds[EntityKind.Tag.ToCode()].SupportsManualManagement);
        Assert.False(kinds[EntityKind.Video.ToCode()].SupportsManualManagement);

        Assert.Equal(
            EntityMediaQualityFamily.Video.ToCode(),
            kinds[EntityKind.VideoSeries.ToCode()].MediaQualityFamily);
        Assert.Equal(
            EntityMediaQualityFamily.Audio.ToCode(),
            kinds[EntityKind.AudioLibrary.ToCode()].MediaQualityFamily);
        Assert.True(kinds[EntityKind.Movie.ToCode()].SupportsAtomicMediaUpgrade);
        Assert.False(kinds[EntityKind.VideoSeason.ToCode()].SupportsAtomicMediaUpgrade);
        Assert.True(kinds[EntityKind.Book.ToCode()].ManualAcquisition.SupportsReplacement);
        Assert.True(kinds[EntityKind.AudioLibrary.ToCode()].ManualAcquisition.SupportsReplacement);
        Assert.False(kinds[EntityKind.VideoSeason.ToCode()].ManualAcquisition.SupportsReplacement);
        Assert.True(kinds[EntityKind.VideoSeason.ToCode()].ManualAcquisition.SupportsUpload);
        Assert.False(kinds[EntityKind.VideoSeries.ToCode()].ManualAcquisition.SupportsUpload);
        Assert.Equal(
            EntityEngagementMode.Reading.ToCode(),
            kinds[EntityKind.Book.ToCode()].EngagementMode);
        Assert.Equal(
            EntityEngagementMode.Playback.ToCode(),
            kinds[EntityKind.AudioLibrary.ToCode()].EngagementMode);
        var selectors = manifest
            .Select(kind => kind.AutoIdentifySelector)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            Enum.GetValues<AutoIdentifySelectorKind>()
                .Select(selector => selector.ToCode())
                .Order(StringComparer.Ordinal),
            selectors);
        Assert.Equal(
            EntityKind.Video.ToCode(),
            kinds[EntityKind.Movie.ToCode()].IdentifyPluginFallbackKind);
        Assert.Null(kinds[EntityKind.Video.ToCode()].IdentifyPluginFallbackKind);
    }

    [Fact]
    public void EntityKindManifestProjectsDefinitionOwnedAcquisitionProfiles() {
        var kinds = CodesManifest.Build().EntityKinds.ToDictionary(kind => kind.Code);

        var series = Assert.IsType<AcquisitionProfileManifestEntry>(
            kinds[EntityKind.VideoSeries.ToCode()].AcquisitionProfile);
        Assert.Equal("TV (series)", series.Label);
        Assert.Equal(2, series.DisplayOrder);
        Assert.Equal(LibraryRootMediaCapability.ScanVideos.ToCode(), series.LibraryRootMediaCapability);
        Assert.Equal(
            [
                EntityDateType.Premiere.ToCode(),
                EntityDateType.Air.ToCode(),
                EntityDateType.FirstAir.ToCode(),
                EntityDateType.StreamingRelease.ToCode(),
                EntityDateType.DigitalRelease.ToCode(),
                EntityDateType.Release.ToCode()
            ],
            series.SupportedReleaseDateTypes);
        Assert.Equal(AcquisitionNamingFamily.Television.ToCode(), series.NamingFamily);
        Assert.Null(kinds[EntityKind.Video.ToCode()].AcquisitionProfile);
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
        Assert.Equal(EntityKind.VideoEpisode.ToCode(), episode.AcquisitionKind);
        Assert.Equal(EntityKind.VideoSeries.ToCode(), episode.ProfileKind);
        Assert.Equal(LibraryRootMediaCapability.ScanVideos.ToCode(), episode.RootFlag);
        Assert.Equal(RequestReviewSelection.Root.ToCode(), episode.ReviewSelection);
    }
}
