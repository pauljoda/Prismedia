using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Tests;

public sealed class EntityKindMetadataTests {
    [Fact]
    public void EveryEntityKindHasExactlyOneDiscoveredDefinition() {
        var expectedKinds = Enum.GetValues<EntityKind>();
        var definitions = EntityKindRegistry.All;

        Assert.Equal(expectedKinds.Length, definitions.Count);
        Assert.Equal(expectedKinds, definitions.Select(definition => definition.Kind));
        Assert.Equal(definitions.Count, definitions.Select(definition => definition.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            definitions.Count(definition => definition.ClrType is not null),
            definitions.Where(definition => definition.ClrType is not null).Select(definition => definition.ClrType).Distinct().Count());
    }

    [Fact]
    public void DefinitionsCreateFreshDefaultCapabilities() {
        var first = EntityKindRegistry.Describe(EntityKind.Book).CreateDefaultCapabilities();
        var second = EntityKindRegistry.Describe(EntityKind.Book).CreateDefaultCapabilities();

        Assert.Equal(first.Select(capability => capability.GetType()), second.Select(capability => capability.GetType()));
        Assert.All(first.Zip(second), pair => Assert.NotSame(pair.First, pair.Second));
    }

    [Theory]
    [InlineData("video-series", true)]
    [InlineData("video-season", true)]
    [InlineData("audio-library", true)]
    [InlineData("music-artist", true)]
    [InlineData("book", true)]
    [InlineData("book-volume", true)]
    [InlineData("movie", false)]
    [InlineData("video", false)]
    [InlineData("image", false)]
    [InlineData("audio-track", false)]
    [InlineData("gallery", false)]
    public void EnumeratesIdentifyChildrenMatchesContainerClassification(string code, bool expected) {
        Assert.Equal(expected, EntityKindRegistry.EnumeratesIdentifyChildren(code));
    }

    [Theory]
    [InlineData(EntityKind.Audio, true)]
    [InlineData(EntityKind.AudioLibrary, true)]
    [InlineData(EntityKind.AudioTrack, true)]
    [InlineData(EntityKind.Book, true)]
    [InlineData(EntityKind.BookAuthor, true)]
    [InlineData(EntityKind.BookChapter, false)]
    [InlineData(EntityKind.BookVolume, true)]
    [InlineData(EntityKind.Gallery, true)]
    [InlineData(EntityKind.Image, true)]
    [InlineData(EntityKind.Movie, true)]
    [InlineData(EntityKind.MusicArtist, true)]
    [InlineData(EntityKind.Video, true)]
    [InlineData(EntityKind.VideoSeason, true)]
    [InlineData(EntityKind.VideoSeries, true)]
    [InlineData(EntityKind.BookPage, false)]
    [InlineData(EntityKind.Collection, false)]
    [InlineData(EntityKind.Person, false)]
    [InlineData(EntityKind.Studio, false)]
    [InlineData(EntityKind.Tag, false)]
    public void SupportsFileDeletionMatchesManagedTreeRoots(EntityKind kind, bool expected) {
        Assert.Equal(expected, EntityKindRegistry.Describe(kind).SupportsFileDeletion);
    }

    [Fact]
    public void AutoIdentifySelectorsAreOwnedByTheirEntityKindDefinitions() {
        var selectorsInUse = EntityKindRegistry.All
            .Select(definition => definition.AutoIdentifySelector)
            .OfType<AutoIdentifySelectorKind>()
            .Distinct()
            .Order()
            .ToArray();

        Assert.Equal(Enum.GetValues<AutoIdentifySelectorKind>(), selectorsInUse);
        Assert.Equal(
            AutoIdentifySelectorKind.Video,
            EntityKindRegistry.Describe(EntityKind.Movie).AutoIdentifySelector);
        Assert.Null(EntityKindRegistry.Describe(EntityKind.VideoSeason).AutoIdentifySelector);
    }

    [Fact]
    public void UserManageableKindsAreOwnedByTheirEntityKindDefinitions() {
        var manageable = EntityKindRegistry.All
            .Where(definition => definition.SupportsManualManagement)
            .ToArray();

        Assert.NotEmpty(manageable);
        Assert.All(manageable, definition => Assert.Equal(EntityKindCategory.Taxonomy, definition.Category));
        Assert.True(EntityKindRegistry.Describe(EntityKind.Person).SupportsManualManagement);
        Assert.False(EntityKindRegistry.Describe(EntityKind.Video).SupportsManualManagement);
    }

    [Fact]
    public void DefinitionsOwnQualityAndArtworkPolicies() {
        var movie = EntityKindRegistry.Describe(EntityKind.Movie);
        var season = EntityKindRegistry.Describe(EntityKind.VideoSeason);
        var album = EntityKindRegistry.Describe(EntityKind.AudioLibrary);
        var track = EntityKindRegistry.Describe(EntityKind.AudioTrack);

        Assert.Equal(EntityMediaQualityFamily.Video, movie.MediaQualityFamily);
        Assert.Equal(EntityMediaQualityFamily.Video, season.MediaQualityFamily);
        Assert.Equal(EntityMediaQualityFamily.Audio, album.MediaQualityFamily);
        Assert.True(movie.SupportsAtomicMediaUpgrade);
        Assert.False(season.SupportsAtomicMediaUpgrade);

        Assert.True(EntityKindRegistry.Describe(EntityKind.Book).Presentation.UsesRepresentativeChildArtwork);
        Assert.True(season.Presentation.UsesRepresentativeChildArtwork);
        Assert.Equal([EntityKind.AudioLibrary], track.Presentation.BorrowArtworkFromParentKinds);
        Assert.Equal([EntityKind.Movie],
            EntityKindRegistry.Describe(EntityKind.Video).Presentation.BorrowArtworkFromParentKinds);
        Assert.Empty(movie.Presentation.BorrowArtworkFromParentKinds);
    }

    [Fact]
    public void AcquisitionProfilesAreOwnedByExactlyTheProfileEntityKinds() {
        var profiles = EntityKindRegistry.All
            .Where(definition => definition.AcquisitionProfile is not null)
            .ToDictionary(definition => definition.Kind, definition => definition.AcquisitionProfile!);

        Assert.Equal(
            [EntityKind.AudioLibrary, EntityKind.Book, EntityKind.Movie, EntityKind.VideoSeries],
            profiles.Keys.Order().ToArray());
        Assert.Equal("TV (series)", profiles[EntityKind.VideoSeries].Label);
        Assert.Equal(0, profiles[EntityKind.Book].DisplayOrder);
        Assert.Equal(3, profiles[EntityKind.AudioLibrary].DisplayOrder);
        Assert.Equal(LibraryRootMediaCapability.ScanBooks, profiles[EntityKind.Book].LibraryRootMediaCapability);
        Assert.Equal(
            [EntityDateType.Release, EntityDateType.DigitalRelease, EntityDateType.PhysicalRelease],
            profiles[EntityKind.AudioLibrary].SupportedReleaseDateTypes);
        Assert.Equal(AcquisitionNamingFamily.Book, profiles[EntityKind.Book].NamingFamily);
    }

    [Fact]
    public void DefinitionsOwnPluginFallbackPositionPrecedenceAndRelationshipScope() {
        var movie = EntityKindRegistry.Describe(EntityKind.Movie);
        var video = EntityKindRegistry.Describe(EntityKind.Video);
        var season = EntityKindRegistry.Describe(EntityKind.VideoSeason);
        var track = EntityKindRegistry.Describe(EntityKind.AudioTrack);

        Assert.Equal(EntityKind.Video, movie.IdentifyPluginFallbackKind);
        Assert.Null(video.IdentifyPluginFallbackKind);

        Assert.Equal([EntityPositionCodes.Episode, EntityPositionCodes.AbsoluteEpisode, EntityPositionCodes.Sort],
            video.PositionSortOrderPrecedence);
        Assert.Equal([EntityPositionCodes.Season, EntityPositionCodes.Sort],
            season.PositionSortOrderPrecedence);
        Assert.Equal([EntityPositionCodes.Track, EntityPositionCodes.Page, EntityPositionCodes.Chapter,
            EntityPositionCodes.Volume, EntityPositionCodes.Sort], track.PositionSortOrderPrecedence);

        Assert.True(movie.OwnsMetadataRelationships);
        Assert.True(EntityKindRegistry.Describe(EntityKind.MusicArtist).OwnsMetadataRelationships);
        Assert.False(season.OwnsMetadataRelationships);
    }

    [Fact]
    public void RegistryRoundTripsEveryKindByCodeAndType() {
        foreach (var kind in Enum.GetValues<EntityKind>()) {
            var descriptor = EntityKindRegistry.Describe(kind);
            Assert.Equal(kind, EntityKindRegistry.Require(descriptor.Code));
            if (descriptor.ClrType is not null) {
                Assert.Equal(kind, EntityKindRegistry.RequireType(descriptor.ClrType));
            }
        }
    }

    [Theory]
    [InlineData(EntityKind.Audio, "audio", "Audio", EntityKindCategory.Media, EntityStorageShape.File, null)]
    [InlineData(EntityKind.Movie, "movie", "Movie", EntityKindCategory.Media, EntityStorageShape.Folder, typeof(Prismedia.Domain.Media.Movie))]
    [InlineData(EntityKind.VideoSeries, "video-series", "Video Series", EntityKindCategory.Media, EntityStorageShape.Folder, typeof(Prismedia.Domain.Media.VideoSeries))]
    [InlineData(EntityKind.BookPage, "book-page", "Book Page", EntityKindCategory.Media, EntityStorageShape.ArchiveEntry, typeof(Prismedia.Domain.Media.BookPage))]
    [InlineData(EntityKind.Person, "person", "Person", EntityKindCategory.Taxonomy, EntityStorageShape.None, typeof(Prismedia.Domain.Taxonomy.Person))]
    [InlineData(EntityKind.AudioLibrary, "audio-library", "Audio Library", EntityKindCategory.Media, EntityStorageShape.Folder, typeof(Prismedia.Domain.Media.AudioLibrary))]
    public void DescriptorValuesArePreservedExactly(
        EntityKind kind,
        string code,
        string displayName,
        EntityKindCategory category,
        EntityStorageShape storageShape,
        Type? clrType) {
        var descriptor = EntityKindRegistry.Describe(kind);

        Assert.Equal(code, descriptor.Code);
        Assert.Equal(displayName, descriptor.DisplayName);
        Assert.Equal(category, descriptor.Category);
        Assert.Equal(storageShape, descriptor.StorageShape);
        Assert.Equal(clrType, descriptor.ClrType);
    }
}
