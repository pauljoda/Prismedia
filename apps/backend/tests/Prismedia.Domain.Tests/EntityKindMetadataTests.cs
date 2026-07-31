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
            .Select(definition => definition.Identification.AutoIdentifySelector)
            .OfType<AutoIdentifySelectorKind>()
            .Distinct()
            .Order()
            .ToArray();

        Assert.Equal(Enum.GetValues<AutoIdentifySelectorKind>(), selectorsInUse);
        Assert.Equal(
            AutoIdentifySelectorKind.Video,
            EntityKindRegistry.Describe(EntityKind.Movie).Identification.AutoIdentifySelector);
        Assert.Null(EntityKindRegistry.Describe(EntityKind.VideoSeason).Identification.AutoIdentifySelector);

        var album = EntityKindRegistry.Describe(EntityKind.AudioLibrary).Identification;
        Assert.True(album.AllowsParentedAutoIdentifyRoot);
        Assert.True(album.UsesParentExternalIdentityContext);
        Assert.True(album.CascadesChildrenAutomatically);

        var artist = EntityKindRegistry.Describe(EntityKind.MusicArtist).Identification;
        Assert.True(artist.EnumeratesChildren);
        Assert.False(artist.CascadesChildrenAutomatically);

        Assert.True(EntityKindRegistry.Describe(EntityKind.Video)
            .Identification.AllowsDirectReconcileChildTarget);
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
    public void DefinitionsOwnManualAcquisitionSupportWithoutAParallelKindRegistry() {
        var replaceableKinds = EntityKindRegistry.All
            .Where(definition => definition.ManualAcquisition.SupportsReplacement)
            .Select(definition => definition.Kind)
            .Order()
            .ToArray();
        var uploadableKinds = EntityKindRegistry.All
            .Where(definition => definition.ManualAcquisition.SupportsUpload)
            .Select(definition => definition.Kind)
            .Order()
            .ToArray();

        Assert.Equal(
            [EntityKind.AudioLibrary, EntityKind.Book, EntityKind.Movie, EntityKind.Video],
            replaceableKinds);
        Assert.Equal(
            [EntityKind.AudioLibrary, EntityKind.Book, EntityKind.Movie, EntityKind.Video, EntityKind.VideoSeason],
            uploadableKinds);
        Assert.All(
            EntityKindRegistry.All.Where(definition => definition.ManualAcquisition.SupportsReplacement),
            definition => Assert.True(definition.ManualAcquisition.SupportsUpload));
    }

    [Fact]
    public void ManualAcquisitionPolicyRejectsReplacementWithoutUpload() {
        Assert.Throws<ArgumentException>(() =>
            new EntityManualAcquisitionPolicy(supportsReplacement: true));
    }

    [Fact]
    public void DefinitionsOwnDerivedMediaProcessingPolicy() {
        var video = EntityKindRegistry.Describe(EntityKind.Video).Processing;
        var image = EntityKindRegistry.Describe(EntityKind.Image).Processing;
        var audio = EntityKindRegistry.Describe(EntityKind.AudioTrack).Processing;
        var page = EntityKindRegistry.Describe(EntityKind.BookPage).Processing;

        Assert.Equal(JobType.ProbeVideo, video.ResolveProbe(needsProbe: true, automaticMetadataEnabled: true));
        Assert.Null(video.ResolveProbe(needsProbe: true, automaticMetadataEnabled: false));
        Assert.Equal(JobType.FingerprintVideo, video.ResolveFingerprint(shouldFingerprint: true));
        Assert.Equal(JobType.ExtractSubtitles, video.ResolveSubtitleExtraction(false, hasSourcePath: true));
        Assert.Equal(JobType.GeneratePreview, video.ResolvePreview(false, true, false, true));
        Assert.Contains(EntityFileRole.Hls, video.GeneratedFileRoles);

        Assert.Equal(JobType.GenerateImageThumbnail, image.PreviewJobType);
        Assert.Equal(JobType.ProbeAudio, audio.ResolveProbe(needsProbe: true, automaticMetadataEnabled: false));
        Assert.Equal([EntityFileRole.Waveform], audio.GeneratedFileRoles);
        Assert.Equal(JobType.GenerateBookPageThumbnail, page.PreviewJobType);
        Assert.Empty(EntityKindRegistry.Describe(EntityKind.Movie).Processing.GeneratedFileRoles);
    }

    [Fact]
    public void ProcessingPolicyRejectsGatesWithoutTheirJobs() {
        Assert.Throws<ArgumentException>(() =>
            new EntityProcessingPolicy(probeRequiresAutomaticMetadata: true));
        Assert.Throws<ArgumentException>(() =>
            new EntityProcessingPolicy(supportsTrickplayGeneration: true));
        Assert.Throws<ArgumentException>(() =>
            new EntityProcessingPolicy(
                generatedFileRoles: [EntityFileRole.Thumbnail, EntityFileRole.Thumbnail]));
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
        Assert.Throws<ArgumentException>(() =>
            new EntityKindBehavior(supportsAtomicMediaUpgrade: true));
    }

    [Fact]
    public void DefinitionsOwnEngagementVocabularyAndChildAggregation() {
        var engagingKinds = EntityKindRegistry.All
            .Where(definition => definition.Engagement.Mode != EntityEngagementMode.None)
            .Select(definition => definition.Kind)
            .Order()
            .ToArray();
        var childAggregates = EntityKindRegistry.All
            .Where(definition => definition.Engagement.AggregatesDirectChildPlayback)
            .Select(definition => definition.Kind)
            .ToArray();

        Assert.Equal(
            [
                EntityKind.AudioLibrary,
                EntityKind.AudioTrack,
                EntityKind.Book,
                EntityKind.BookVolume,
                EntityKind.BookChapter,
                EntityKind.Movie,
                EntityKind.Video,
                EntityKind.VideoSeries,
                EntityKind.VideoSeason
            ],
            engagingKinds);
        Assert.Equal(EntityEngagementMode.Reading,
            EntityKindRegistry.Describe(EntityKind.Book).Engagement.Mode);
        Assert.Equal(EntityEngagementMode.Playback,
            EntityKindRegistry.Describe(EntityKind.AudioLibrary).Engagement.Mode);
        Assert.Equal([EntityKind.Movie], childAggregates);
    }

    [Fact]
    public void DefinitionsOwnBrowseHierarchyAndAggregateDeduplication() {
        var defaultWantedExclusions = EntityKindRegistry.All
            .Where(definition => definition.Browse.ExcludesWantedByDefault)
            .Select(definition => definition.Kind)
            .ToArray();
        var topLevelBrowseKinds = EntityKindRegistry.All
            .Where(definition => definition.Browse.RequiresTopLevel)
            .Select(definition => definition.Kind)
            .ToArray();

        Assert.Equal([EntityKind.AudioTrack], defaultWantedExclusions);
        Assert.Equal([EntityKind.Gallery], topLevelBrowseKinds);
        Assert.Equal(
            [EntityKind.Book],
            EntityKindRegistry.Describe(EntityKind.Book).Browse.HiddenParentKinds);
        Assert.Equal(
            [EntityKind.Movie],
            EntityKindRegistry.Describe(EntityKind.Video).Browse.AggregateParentKinds);
        Assert.All(
            EntityKindRegistry.All.Where(definition => definition.Kind is not (
                EntityKind.AudioTrack or EntityKind.Book or EntityKind.Gallery or EntityKind.Video)),
            definition => Assert.Equal(EntityBrowsePolicy.Default, definition.Browse));
    }

    [Fact]
    public void BrowsePolicyRejectsAmbiguousAndDuplicateHierarchyRules() {
        Assert.Throws<ArgumentException>(() =>
            new EntityBrowsePolicy(
                requiresTopLevel: true,
                hiddenParentKinds: [EntityKind.Gallery]));
        Assert.Throws<ArgumentException>(() =>
            new EntityBrowsePolicy(
                aggregateParentKinds: [EntityKind.Movie, EntityKind.Movie]));
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

        Assert.Equal(EntityKind.Video, movie.Identification.PluginFallbackKind);
        Assert.Null(video.Identification.PluginFallbackKind);

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
