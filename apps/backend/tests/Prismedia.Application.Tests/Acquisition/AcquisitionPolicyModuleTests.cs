using Prismedia.Application.Acquisition;
using Prismedia.Application.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

/// <summary>Pins the deterministic definition-family policy registry and its family search behavior.</summary>
public sealed class AcquisitionPolicyModuleTests {
    [Fact]
    public void RegistryRejectsTwoModulesForTheSameDefinitionFamily() {
        var first = new FakePolicyModule();
        var second = new FakePolicyModule();

        var error = Assert.Throws<InvalidOperationException>(() =>
            new AcquisitionPolicyRegistry([
                first,
                second,
                new MovieAcquisitionPolicyModule(),
                new MusicAcquisitionPolicyModule(),
                new TvAcquisitionPolicyModule()
            ]));

        Assert.Contains(AcquisitionNamingFamily.Book.ToCode(), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(FakePolicyModule), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistryRejectsAnIncompleteDefinitionFamilySet() {
        var error = Assert.Throws<InvalidOperationException>(() =>
            new AcquisitionPolicyRegistry([new BookAcquisitionPolicyModule()]));

        Assert.Contains(EntityKind.AudioLibrary.ToCode(), error.Message, StringComparison.Ordinal);
        Assert.Contains(AcquisitionNamingFamily.Music.ToCode(), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistryResolvesEveryKindOwnedByTheBuiltInModules() {
        var registry = BuiltInRegistry();

        Assert.IsType<BookAcquisitionPolicyModule>(registry.Get(EntityKind.Book));
        Assert.IsType<MovieAcquisitionPolicyModule>(registry.Get(EntityKind.Movie));
        Assert.IsType<MusicAcquisitionPolicyModule>(registry.Get(EntityKind.AudioLibrary));
        Assert.IsType<MusicAcquisitionPolicyModule>(registry.Get(EntityKind.AudioTrack));
        Assert.IsType<TvAcquisitionPolicyModule>(registry.Get(EntityKind.VideoSeries));
        Assert.IsType<TvAcquisitionPolicyModule>(registry.Get(EntityKind.VideoSeason));
        Assert.IsType<TvAcquisitionPolicyModule>(registry.Get(EntityKind.VideoEpisode));
    }

    [Fact]
    public void ModulesBuildTheExistingContextRichQueryLadders() {
        var registry = BuiltInRegistry();
        var book = new AcquisitionSearchInput(Guid.NewGuid(), "Book", "Author", EntityKind.Book);
        var album = new AcquisitionSearchInput(
            Guid.NewGuid(), "Discovery", "Daft Punk", EntityKind.AudioLibrary);
        var series = new AcquisitionSearchInput(
            Guid.NewGuid(), "Game of Thrones", null, EntityKind.VideoSeries);
        var movie = new AcquisitionSearchInput(
            Guid.NewGuid(), "Dune", null, EntityKind.Movie, Year: 2021);
        var movieWithoutYear = movie with { Year = null };

        Assert.Equal(["Book Author", "Book"], registry.Get(book.Kind).BuildQueries(book));
        Assert.Equal(["Daft Punk Discovery", "Discovery"], registry.Get(album.Kind).BuildQueries(album));
        Assert.Equal(["Game of Thrones complete", "Game of Thrones"], registry.Get(series.Kind).BuildQueries(series));
        Assert.Equal(["Dune 2021", "Dune"], registry.Get(movie.Kind).BuildQueries(movie));
        Assert.Equal(["Dune"], registry.Get(movieWithoutYear.Kind).BuildQueries(movieWithoutYear));
    }

    [Fact]
    public void TvModuleBuildsSeasonAndEpisodeLadders() {
        var module = new TvAcquisitionPolicyModule();
        var season = new AcquisitionSearchInput(
            Guid.NewGuid(), "Season 1", null, EntityKind.VideoSeason,
            Series: "Andor", SeasonNumber: 1);
        var episode = new AcquisitionSearchInput(
            Guid.NewGuid(), "Pilot", null, EntityKind.VideoEpisode,
            Series: "Andor", SeasonNumber: 1, EpisodeNumber: 5, AbsoluteEpisodeNumber: 1316);

        Assert.Equal(["Andor S01", "Andor Season 1", "Andor complete"], module.BuildQueries(season));
        Assert.Equal(["Andor S01E05", "Andor 1x05"], module.BuildQueries(episode));
        Assert.Empty(module.BuildFallbackQueries(season));
        Assert.Equal(["Andor Pilot", "Andor 1316"], module.BuildFallbackQueries(episode));
    }

    [Fact]
    public void MusicTrackQueriesDoNotRepeatAnAlbumTitleThatMatchesTheTrack() {
        var module = new MusicAcquisitionPolicyModule();
        var track = new AcquisitionSearchInput(
            Guid.NewGuid(),
            "Had Enough",
            "Divide Music",
            EntityKind.AudioTrack,
            Series: "Had Enough");

        Assert.Equal([
            "Divide Music Had Enough",
            "Had Enough"
        ], module.BuildQueries(track));
        Assert.Equal("Divide Music Had Enough", track.WorkTitle);
    }

    [Fact]
    public void ModulesRouteConfiguredCategoriesWithinTheirTorznabRange() {
        var book = new BookAcquisitionPolicyModule();
        var movie = new MovieAcquisitionPolicyModule();
        var music = new MusicAcquisitionPolicyModule();
        var tv = new TvAcquisitionPolicyModule();

        Assert.Equal([7020], book.RouteCategories(new AcquisitionSearchInput(Guid.NewGuid(), "Book", null, EntityKind.Book, BookRendition: BookRendition.Ebook), [7000, 7020, 7030, 3030]));
        Assert.Equal([3030], book.RouteCategories(new AcquisitionSearchInput(Guid.NewGuid(), "Book", null, EntityKind.Book, BookRendition: BookRendition.Audiobook), [3000, 3030, 7020]));
        Assert.Equal([2000], movie.RouteCategories(new AcquisitionSearchInput(Guid.NewGuid(), "Movie", null, EntityKind.Movie), [7000, 7030]));
        Assert.Equal([3000], music.RouteCategories(new AcquisitionSearchInput(Guid.NewGuid(), "Album", null, EntityKind.AudioLibrary), []));
        Assert.Equal([5000], tv.RouteCategories(new AcquisitionSearchInput(Guid.NewGuid(), "Series", null, EntityKind.VideoSeries), [7000]));
    }

    [Fact]
    public void ModulesPreserveConfiguredOtherRangeCategories() {
        var ebook = new AcquisitionSearchInput(Guid.NewGuid(), "Book", null, EntityKind.Book, BookRendition: BookRendition.Ebook);
        Assert.Equal([7020, 8000], new BookAcquisitionPolicyModule().RouteCategories(ebook, [7000, 7020, 8000]));
        Assert.Equal([2000, 8010], new MovieAcquisitionPolicyModule().RouteCategories(new AcquisitionSearchInput(Guid.NewGuid(), "Movie", null, EntityKind.Movie), [2000, 7000, 8010]));
        Assert.Equal([7020, 8000], new BookAcquisitionPolicyModule().RouteCategories(ebook, [8000]));
        Assert.Equal([5000, 5040], new TvAcquisitionPolicyModule().RouteCategories(new AcquisitionSearchInput(Guid.NewGuid(), "Series", null, EntityKind.VideoSeries), [5000, 5040]));
    }

    [Fact]
    public void FamilyModulesCreateDecisionEnginesForEveryDefinitionDerivedAcquisitionUnit() {
        var registry = BuiltInRegistry();

        foreach (var kind in RequestKindRegistry.All
                     .Select(descriptor => descriptor.AcquisitionKind)
                     .Distinct()) {
            Assert.Equal(kind, registry.Get(kind).DecisionEngineFor(kind).Kind);
        }
    }

    [Fact]
    public void MusicAndTvFamiliesResolveTheirDescriptorDerivedUnitsWithoutModuleKindLists() {
        var registry = BuiltInRegistry();
        var musicKinds = AcquisitionKindsFor(AcquisitionNamingFamily.Music);
        var televisionKinds = AcquisitionKindsFor(AcquisitionNamingFamily.Television);

        Assert.Equal([EntityKind.AudioLibrary, EntityKind.AudioTrack], musicKinds);
        Assert.Equal([EntityKind.VideoEpisode, EntityKind.VideoSeason, EntityKind.VideoSeries], televisionKinds);
        Assert.All(musicKinds, kind => Assert.IsType<MusicAcquisitionPolicyModule>(registry.Get(kind)));
        Assert.All(televisionKinds, kind => Assert.IsType<TvAcquisitionPolicyModule>(registry.Get(kind)));
    }

    private static AcquisitionPolicyRegistry BuiltInRegistry() => new([
        new BookAcquisitionPolicyModule(),
        new MovieAcquisitionPolicyModule(),
        new MusicAcquisitionPolicyModule(),
        new TvAcquisitionPolicyModule()
    ]);

    private static EntityKind[] AcquisitionKindsFor(AcquisitionNamingFamily family) =>
        RequestKindRegistry.All
            .SelectMany(descriptor => new[] { descriptor.AcquisitionKind, descriptor.ProfileEntityKind })
            .OfType<EntityKind>()
            .Distinct()
            .Where(kind => AcquisitionStrategyRegistration.TryGetNamingFamily(kind) == family)
            .OrderBy(kind => kind.ToCode(), StringComparer.Ordinal)
            .ToArray();

    [AcquisitionStrategy(AcquisitionNamingFamily.Book)]
    private sealed class FakePolicyModule : IAcquisitionPolicyModule {

        public IReadOnlyList<string> BuildQueries(AcquisitionSearchInput input) => [input.Title];

        public IReadOnlyList<int> RouteCategories(AcquisitionSearchInput input, IReadOnlyList<int> configuredCategories) =>
            configuredCategories;

        public IAcquisitionDecisionEngine DecisionEngineFor(EntityKind kind) =>
            throw new NotSupportedException();
    }
}
