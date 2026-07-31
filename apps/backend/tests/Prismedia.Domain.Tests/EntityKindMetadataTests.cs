using Prismedia.Domain.Capabilities;
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

    [Fact]
    public void PlayableVideoDefinitionsShareTheFacetAndDeclaredDefaults() {
        var playableKinds = EntityKindRegistry.All
            .Where(definition => definition is IPlayableVideoKindDefinition)
            .Select(definition => definition.Kind)
            .Order()
            .ToArray();

        Assert.Equal([EntityKind.Movie, EntityKind.Video, EntityKind.VideoEpisode], playableKinds);
        foreach (var kind in playableKinds) {
            var definition = EntityKindRegistry.Describe(kind);
            Assert.True(definition.SupportsDefaultCapability<CapabilityPlayback>());
            Assert.True(definition.SupportsDefaultCapability<CapabilityMarkers>());
            Assert.True(definition.SupportsDefaultCapability<CapabilitySubtitles>());
            Assert.True(definition.SupportsDefaultCapability<CapabilityCredits>());
        }

        Assert.False(EntityKindRegistry.Describe(EntityKind.Video).SupportsDefaultCapability<CapabilityPosition>());
        Assert.True(EntityKindRegistry.Describe(EntityKind.VideoEpisode).SupportsDefaultCapability<CapabilityPosition>());
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
    [InlineData(EntityKind.VideoEpisode, true)]
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

        Assert.True(EntityKindRegistry.Describe(EntityKind.VideoEpisode)
            .Identification.AllowsDirectReconcileChildTarget);
    }

    [Fact]
    public void DefinitionsResolveTheirOwnPluginKindCompatibility() {
        Assert.All(
            EntityKindRegistry.All,
            definition => Assert.True(definition.AcceptsPluginKind(definition.Kind)));
        Assert.True(EntityKindRegistry.Describe(EntityKind.Movie).AcceptsPluginKind(EntityKind.Video));
        Assert.False(EntityKindRegistry.Describe(EntityKind.Video).AcceptsPluginKind(EntityKind.Movie));
        Assert.False(EntityKindRegistry.Describe(EntityKind.Book).AcceptsPluginKind(EntityKind.Video));
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
    public void StandaloneVideoUploadDoesNotRequireARequestDescriptor() {
        var video = EntityKindRegistry.Describe(EntityKind.Video);

        Assert.True(video.ManualAcquisition.SupportsUpload);
        Assert.True(video.ManualAcquisition.SupportsReplacement);
        Assert.Empty(video.RequestKinds);
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

        var videoPlan = video.Plan(new EntityProcessingInputs(
            NeedsProbe: true, ShouldFingerprint: true, NeedsSubtitleExtraction: false, ForceSubtitleReconciliationForOwnedSource: true,
            NeedsPreview: false, NeedsTrickplay: true, NeedsGridThumbnail: true,
            AutomaticMetadataEnabled: true, AutomaticPreviewEnabled: false, TrickplayEnabled: true));
        Assert.Equal(JobType.ProbeVideo, videoPlan.ProbeJobType);
        Assert.Equal(JobType.FingerprintVideo, videoPlan.FingerprintJobType);
        Assert.Equal(JobType.ExtractSubtitles, videoPlan.SubtitleExtractionJobType);
        Assert.Equal(JobType.GeneratePreview, videoPlan.PreviewJobType);
        Assert.Null(videoPlan.GridThumbnailJobType);
        Assert.Contains(EntityFileRole.Hls, video.GeneratedFileRoles);

        Assert.Equal(JobType.GenerateImageThumbnail, image.PreviewJobType);
        Assert.Equal(JobType.ProbeAudio, audio.Plan(new EntityProcessingInputs(
            true, false, false, false, false, false, false, false, false, false)).ProbeJobType);
        Assert.Equal([EntityFileRole.Waveform], audio.GeneratedFileRoles);
        Assert.Equal(JobType.GenerateBookPageThumbnail, page.PreviewJobType);
        Assert.Equal(
            EntityKindRegistry.Describe(EntityKind.Video).Processing.GeneratedFileRoles,
            EntityKindRegistry.Describe(EntityKind.Movie).Processing.GeneratedFileRoles);
        Assert.Equal(
            EntityKindRegistry.Describe(EntityKind.Video).Processing.GeneratedFileRoles,
            EntityKindRegistry.Describe(EntityKind.VideoEpisode).Processing.GeneratedFileRoles);
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
        Assert.Throws<ArgumentException>(() =>
            new EntityProcessingPolicy(gridThumbnailJobType: JobType.GenerateGridThumbnail));
        Assert.Throws<ArgumentException>(() =>
            new EntityProcessingPolicy(generatedFileRoles: [EntityFileRole.Thumbnail]));
    }

    [Theory]
    [InlineData(EntityKind.Movie)]
    [InlineData(EntityKind.VideoEpisode)]
    [InlineData(EntityKind.Video)]
    public void PlayableVideoDefinitionsUseTheSharedFamilyAndCompletePlan(EntityKind kind) {
        var processing = EntityKindRegistry.Describe(kind).Processing;
        var fullPlan = processing.Plan(new EntityProcessingInputs(
            NeedsProbe: true, ShouldFingerprint: true, NeedsSubtitleExtraction: true,
            ForceSubtitleReconciliationForOwnedSource: false, NeedsPreview: true, NeedsTrickplay: true,
            NeedsGridThumbnail: true, AutomaticMetadataEnabled: true, AutomaticPreviewEnabled: true,
            TrickplayEnabled: true));
        var gridPlan = processing.Plan(new EntityProcessingInputs(
            NeedsProbe: false, ShouldFingerprint: false, NeedsSubtitleExtraction: false,
            ForceSubtitleReconciliationForOwnedSource: false, NeedsPreview: false, NeedsTrickplay: false,
            NeedsGridThumbnail: true, AutomaticMetadataEnabled: false, AutomaticPreviewEnabled: false,
            TrickplayEnabled: false));
        var gatedProbePlan = processing.Plan(new EntityProcessingInputs(
            NeedsProbe: true, ShouldFingerprint: false, NeedsSubtitleExtraction: false,
            ForceSubtitleReconciliationForOwnedSource: false, NeedsPreview: false, NeedsTrickplay: false,
            NeedsGridThumbnail: false, AutomaticMetadataEnabled: false, AutomaticPreviewEnabled: false,
            TrickplayEnabled: false));

        Assert.Equal(GeneratedAssetFamily.Video, processing.AssetFamily);
        Assert.Equal(JobType.ProbeVideo, fullPlan.ProbeJobType);
        Assert.Equal(JobType.FingerprintVideo, fullPlan.FingerprintJobType);
        Assert.Equal(JobType.ExtractSubtitles, fullPlan.SubtitleExtractionJobType);
        Assert.Equal(JobType.GeneratePreview, fullPlan.PreviewJobType);
        Assert.Equal(JobType.GenerateGridThumbnail, gridPlan.GridThumbnailJobType);
        Assert.Null(gatedProbePlan.ProbeJobType);
        Assert.Contains(EntityFileRole.Hls, processing.GeneratedFileRoles);
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
        Assert.Equal(EntityUpgradeMode.AtomicBookFile, EntityKindRegistry.Describe(EntityKind.Book).UpgradeMode);
        Assert.Equal(EntityUpgradeMode.AtomicMediaFile, movie.UpgradeMode);
        Assert.Equal(EntityUpgradeMode.Import, season.UpgradeMode);
        Assert.Equal(EntityUpgradeMode.Import, album.UpgradeMode);
        Assert.True(movie.SupportsAtomicMediaUpgrade);
        Assert.False(season.SupportsAtomicMediaUpgrade);

        Assert.True(EntityKindRegistry.Describe(EntityKind.Book).Presentation.UsesRepresentativeChildArtwork);
        Assert.True(season.Presentation.UsesRepresentativeChildArtwork);
        Assert.Equal([EntityKind.AudioLibrary], track.Presentation.BorrowArtworkFromParentKinds);
        Assert.Empty(EntityKindRegistry.Describe(EntityKind.Video).Presentation.BorrowArtworkFromParentKinds);
        Assert.Empty(movie.Presentation.BorrowArtworkFromParentKinds);
        Assert.Throws<ArgumentException>(() =>
            new EntityKindBehavior(upgradeMode: EntityUpgradeMode.AtomicMediaFile));
    }

    [Fact]
    public void DefinitionsOwnEmptyContainerPruningPolicy() {
        var prunableKinds = EntityKindRegistry.All
            .Where(definition => definition.PrunesWhenEmpty)
            .Select(definition => definition.Kind)
            .Order()
            .ToArray();

        Assert.Equal(
            [EntityKind.VideoSeries, EntityKind.VideoSeason],
            prunableKinds);
    }

    [Fact]
    public void DefinitionsOwnEngagementVocabulary() {
        var engagingKinds = EntityKindRegistry.All
            .Where(definition => definition.Engagement.Mode != EntityEngagementMode.None)
            .Select(definition => definition.Kind)
            .Order()
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
                EntityKind.VideoEpisode,
                EntityKind.VideoSeries,
                EntityKind.VideoSeason
            ],
            engagingKinds);
        Assert.Equal(EntityEngagementMode.Reading,
            EntityKindRegistry.Describe(EntityKind.Book).Engagement.Mode);
        Assert.Equal(EntityEngagementMode.Playback,
            EntityKindRegistry.Describe(EntityKind.AudioLibrary).Engagement.Mode);
        Assert.True(EntityKindRegistry.Describe(EntityKind.Video).Engagement.DerivesCompletionFromPlaybackFraction);
        Assert.True(EntityKindRegistry.Describe(EntityKind.Movie).Engagement.DerivesCompletionFromPlaybackFraction);
        Assert.False(EntityKindRegistry.Describe(EntityKind.AudioTrack).Engagement.DerivesCompletionFromPlaybackFraction);
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
        Assert.Equal(EntityBrowsePolicy.Default, EntityKindRegistry.Describe(EntityKind.Video).Browse);
        Assert.All(
            EntityKindRegistry.All.Where(definition => definition.Kind is not (
                EntityKind.AudioTrack or EntityKind.Book or EntityKind.Gallery)),
            definition => Assert.Equal(EntityBrowsePolicy.Default, definition.Browse));
    }

    [Fact]
    public void BrowsePolicyRejectsAmbiguousHierarchyRules() {
        Assert.Throws<ArgumentException>(() =>
            new EntityBrowsePolicy(
                requiresTopLevel: true,
                hiddenParentKinds: [EntityKind.Gallery]));
    }

    [Fact]
    public void DefinitionsOwnLibraryRootVisibilityTopology() {
        var directRoots = EntityKindRegistry.All
            .Where(definition => definition.LibraryVisibility.Mode == EntityLibraryVisibilityMode.DirectRoot)
            .Select(definition => definition.Kind)
            .Order()
            .ToArray();
        var inheritedRoots = EntityKindRegistry.All
            .Where(definition => definition.LibraryVisibility.Mode == EntityLibraryVisibilityMode.AncestorRoot)
            .Select(definition => definition.Kind)
            .Order()
            .ToArray();
        var descendantRoots = EntityKindRegistry.All
            .Where(definition => definition.LibraryVisibility.Mode == EntityLibraryVisibilityMode.DescendantRoot)
            .ToDictionary(definition => definition.Kind, definition => definition.LibraryVisibility);

        Assert.Equal(
            [
                EntityKind.AudioLibrary,
                EntityKind.Book,
                EntityKind.Gallery,
                EntityKind.MusicArtist,
                EntityKind.Movie,
                EntityKind.Video,
                EntityKind.VideoEpisode
            ],
            directRoots);
        Assert.Equal(
            [
                EntityKind.AudioTrack,
                EntityKind.BookVolume,
                EntityKind.BookChapter,
                EntityKind.BookPage,
                EntityKind.Image
            ],
            inheritedRoots);
        Assert.Equal(
            (EntityKind.Book, 1),
            (descendantRoots[EntityKind.BookAuthor].DescendantKind, descendantRoots[EntityKind.BookAuthor].MaximumDepth));
        Assert.Equal(
            (EntityKind.VideoEpisode, 1),
            (descendantRoots[EntityKind.VideoSeason].DescendantKind, descendantRoots[EntityKind.VideoSeason].MaximumDepth));
        Assert.Equal(
            (EntityKind.VideoEpisode, 2),
            (descendantRoots[EntityKind.VideoSeries].DescendantKind, descendantRoots[EntityKind.VideoSeries].MaximumDepth));
        Assert.Equal(3, descendantRoots.Count);
        Assert.All(descendantRoots.Values, policy =>
            Assert.Equal(
                EntityLibraryVisibilityMode.DirectRoot,
                EntityKindRegistry.Describe(policy.DescendantKind!.Value).LibraryVisibility.Mode));
    }

    [Fact]
    public void DescendantLibraryVisibilityRequiresABoundedDepth() {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EntityLibraryVisibilityPolicy.FromDescendants(EntityKind.Video, maximumDepth: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EntityLibraryVisibilityPolicy.FromDescendants(EntityKind.Video, maximumDepth: 4));
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
        var episode = EntityKindRegistry.Describe(EntityKind.VideoEpisode);
        var season = EntityKindRegistry.Describe(EntityKind.VideoSeason);
        var track = EntityKindRegistry.Describe(EntityKind.AudioTrack);

        Assert.Equal(EntityKind.Video, movie.Identification.PluginFallbackKind);
        Assert.Null(video.Identification.PluginFallbackKind);

        Assert.Equal(EntityKind.Video, episode.Identification.PluginFallbackKind);
        Assert.Equal([EntityPositionCodes.Episode, EntityPositionCodes.AbsoluteEpisode, EntityPositionCodes.Sort],
            episode.PositionSortOrderPrecedence);
        Assert.Equal(EntityKindDefinition.DefaultPositionSortOrderPrecedence, video.PositionSortOrderPrecedence);
        Assert.Equal([EntityPositionCodes.Season, EntityPositionCodes.Sort],
            season.PositionSortOrderPrecedence);
        Assert.Equal([EntityPositionCodes.Track, EntityPositionCodes.Page, EntityPositionCodes.Chapter,
            EntityPositionCodes.Volume, EntityPositionCodes.Sort], track.PositionSortOrderPrecedence);

        Assert.True(movie.OwnsMetadataRelationships);
        Assert.True(EntityKindRegistry.Describe(EntityKind.MusicArtist).OwnsMetadataRelationships);
        Assert.False(season.OwnsMetadataRelationships);
    }

    [Fact]
    public void VideoStructurePoliciesDeclareTheEnforcedTargetGraph() {
        var movie = EntityKindRegistry.Describe(EntityKind.Movie).StructurePolicy;
        var video = EntityKindRegistry.Describe(EntityKind.Video).StructurePolicy;
        var episode = EntityKindRegistry.Describe(EntityKind.VideoEpisode).StructurePolicy;
        var series = EntityKindRegistry.Describe(EntityKind.VideoSeries).StructurePolicy;
        var season = EntityKindRegistry.Describe(EntityKind.VideoSeason).StructurePolicy;

        Assert.Equal([], movie.AllowedChildKinds);
        Assert.Equal([], video.AllowedChildKinds);
        Assert.True(episode.RequiresParent);
        Assert.Equal([EntityKind.VideoSeries, EntityKind.VideoSeason], episode.AllowedParentKinds);
        Assert.Equal([EntityKind.VideoSeason, EntityKind.VideoEpisode], series.AllowedChildKinds);
        Assert.True(season.RequiresParent);
        Assert.Equal([EntityKind.VideoSeries], season.AllowedParentKinds);
        Assert.Equal([EntityKind.VideoEpisode], season.AllowedChildKinds);
    }

    [Fact]
    public void StructurePolicyRejectsDuplicateOrInconsistentParentDeclarations() {
        Assert.Throws<ArgumentException>(() => new EntityStructurePolicy(
            requiresParent: true,
            allowedParentKinds: [EntityKind.VideoSeries, EntityKind.VideoSeries],
            allowedChildKinds: []));
        Assert.Throws<ArgumentException>(() => new EntityStructurePolicy(
            requiresParent: true,
            allowedParentKinds: [],
            allowedChildKinds: []));
        Assert.Throws<ArgumentException>(() => new EntityStructurePolicy(
            requiresParent: false,
            allowedParentKinds: [EntityKind.VideoSeries],
            allowedChildKinds: []));
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
    [InlineData(EntityKind.Movie, "movie", "Movie", EntityKindCategory.Media, EntityStorageShape.File, typeof(Prismedia.Domain.Media.Movie))]
    [InlineData(EntityKind.VideoEpisode, "video-episode", "Video Episode", EntityKindCategory.Media, EntityStorageShape.File, typeof(Prismedia.Domain.Media.VideoEpisode))]
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
