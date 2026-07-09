using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

/// <summary>Pins the deterministic per-kind policy registry and each module's search policy.</summary>
public sealed class AcquisitionPolicyModuleTests {
    [Fact]
    public void RegistryRejectsTwoModulesForTheSameKind() {
        var first = new FakePolicyModule(EntityKind.Book);
        var second = new FakePolicyModule(EntityKind.Book);

        var error = Assert.Throws<InvalidOperationException>(() =>
            new AcquisitionPolicyRegistry([first, second]));

        Assert.Contains(EntityKind.Book.ToCode(), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(FakePolicyModule), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistryRejectsAKindWithoutARegisteredModule() {
        var registry = new AcquisitionPolicyRegistry([new BookAcquisitionPolicyModule()]);

        var error = Assert.Throws<InvalidOperationException>(() => registry.Get(EntityKind.Movie));

        Assert.Contains(EntityKind.Movie.ToCode(), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistryResolvesEveryKindOwnedByTheBuiltInModules() {
        var registry = BuiltInRegistry();

        Assert.IsType<BookAcquisitionPolicyModule>(registry.Get(EntityKind.Book));
        Assert.IsType<MovieAcquisitionPolicyModule>(registry.Get(EntityKind.Movie));
        Assert.IsType<MusicAcquisitionPolicyModule>(registry.Get(EntityKind.AudioLibrary));
        Assert.IsType<MusicAcquisitionPolicyModule>(registry.Get(EntityKind.AudioTrack));
        Assert.IsType<MusicAcquisitionPolicyModule>(registry.Get(EntityKind.MusicArtist));
        Assert.IsType<TvAcquisitionPolicyModule>(registry.Get(EntityKind.VideoSeries));
        Assert.IsType<TvAcquisitionPolicyModule>(registry.Get(EntityKind.VideoSeason));
        Assert.IsType<TvAcquisitionPolicyModule>(registry.Get(EntityKind.Video));
    }

    [Fact]
    public void ModulesBuildTheExistingContextRichQueryLadders() {
        var registry = BuiltInRegistry();
        var book = new AcquisitionSearchInput(Guid.NewGuid(), "Book", "Author");
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
    public void TvModuleBuildsSeasonEpisodeAndDirectVideoLadders() {
        var module = new TvAcquisitionPolicyModule();
        var season = new AcquisitionSearchInput(
            Guid.NewGuid(), "Season 1", null, EntityKind.VideoSeason,
            Series: "Andor", SeasonNumber: 1);
        var episode = new AcquisitionSearchInput(
            Guid.NewGuid(), "Pilot", null, EntityKind.Video,
            Series: "Andor", SeasonNumber: 1, EpisodeNumber: 5);
        var directVideo = new AcquisitionSearchInput(
            Guid.NewGuid(), "Some Video", null, EntityKind.Video, Year: 2020);

        Assert.Equal(["Andor S01", "Andor Season 1", "Andor complete"], module.BuildQueries(season));
        Assert.Equal(["Andor S01E05", "Andor 1x05"], module.BuildQueries(episode));
        Assert.Equal(["Some Video 2020", "Some Video"], module.BuildQueries(directVideo));
    }

    [Fact]
    public void ModulesRouteConfiguredCategoriesWithinTheirTorznabRange() {
        var book = new BookAcquisitionPolicyModule();
        var movie = new MovieAcquisitionPolicyModule();
        var music = new MusicAcquisitionPolicyModule();
        var tv = new TvAcquisitionPolicyModule();

        Assert.Equal([7000, 7030], book.RouteCategories([7000, 7030, 2000]));
        Assert.Equal([2000], movie.RouteCategories([7000, 7030]));
        Assert.Equal([3000], music.RouteCategories([]));
        Assert.Equal([5000], tv.RouteCategories([7000]));
    }

    [Fact]
    public void ModulesPreserveConfiguredOtherRangeCategories() {
        Assert.Equal([7000, 8000], new BookAcquisitionPolicyModule().RouteCategories([7000, 8000]));
        Assert.Equal([2000, 8010], new MovieAcquisitionPolicyModule().RouteCategories([2000, 7000, 8010]));
        Assert.Equal([7000, 8000], new BookAcquisitionPolicyModule().RouteCategories([8000]));
        Assert.Equal([5000, 5040], new TvAcquisitionPolicyModule().RouteCategories([5000, 5040]));
    }

    [Fact]
    public void MultiKindModulesReturnAnEngineBoundToTheRequestedKind() {
        var music = new MusicAcquisitionPolicyModule();
        var tv = new TvAcquisitionPolicyModule();

        Assert.Equal(EntityKind.AudioTrack, music.DecisionEngineFor(EntityKind.AudioTrack).Kind);
        Assert.Equal(EntityKind.MusicArtist, music.DecisionEngineFor(EntityKind.MusicArtist).Kind);
        Assert.Equal(EntityKind.VideoSeries, tv.DecisionEngineFor(EntityKind.VideoSeries).Kind);
        Assert.Equal(EntityKind.Video, tv.DecisionEngineFor(EntityKind.Video).Kind);
    }

    private static AcquisitionPolicyRegistry BuiltInRegistry() => new([
        new BookAcquisitionPolicyModule(),
        new MovieAcquisitionPolicyModule(),
        new MusicAcquisitionPolicyModule(),
        new TvAcquisitionPolicyModule()
    ]);

    private sealed class FakePolicyModule(params EntityKind[] supportedKinds) : IAcquisitionPolicyModule {
        public IReadOnlyCollection<EntityKind> SupportedKinds { get; } = supportedKinds;

        public IReadOnlyList<string> BuildQueries(AcquisitionSearchInput input) => [input.Title];

        public IReadOnlyList<int> RouteCategories(IReadOnlyList<int> configuredCategories) =>
            configuredCategories;

        public IAcquisitionDecisionEngine DecisionEngineFor(EntityKind kind) =>
            throw new NotSupportedException();
    }
}
