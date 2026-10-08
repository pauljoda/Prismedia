using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Tests;

public sealed class IdentifyMatchHintResolverTests {
    [Fact]
    public async Task ResolvePrefersStoredProviderIdOverProviderUrls() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        SeedEntity(db, entityId, "video", "Stored Match");
        db.EntityUrls.Add(new EntityUrlRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Url = "https://www.themoviedb.org/movie/999",
            SortOrder = 0,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.EntityExternalIds.Add(new EntityExternalIdRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Provider = "tmdb",
            Value = "123",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var resolver = new IdentifyMatchHintResolver(db);
        var hints = await resolver.ResolveAsync(entityId, "tmdb", CancellationToken.None);

        Assert.Equal("123", hints.ExternalIds["tmdb"]);
        Assert.Equal("Stored Match", hints.Title);
        Assert.Contains("https://www.themoviedb.org/movie/999", hints.Urls);
    }

    [Fact]
    public async Task ResolveParsesProviderUrlWhenStoredIdIsMissing() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        SeedEntity(db, entityId, "video-series", "URL Match");
        db.EntityUrls.Add(new EntityUrlRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Url = "https://www.themoviedb.org/tv/456-url-match",
            SortOrder = 0,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var resolver = new IdentifyMatchHintResolver(db);
        var hints = await resolver.ResolveAsync(entityId, "tmdb", CancellationToken.None);

        Assert.Equal("456", hints.ExternalIds["tmdb"]);
        Assert.Equal("URL Match", hints.Title);
    }

    [Fact]
    public async Task ResolveReadsProviderTagsFromMovieAndSeriesPathsButNotSeasons() {
        await using var db = CreateContext();
        var movieId = Guid.NewGuid();
        var identifiedMovieId = Guid.NewGuid();
        var seriesId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        SeedEntity(db, movieId, EntityKind.Movie.ToCode(), "Heat (1995) {tmdb-949}");
        SeedFolder(db, movieId, "/media/movies/Heat (1995) {tmdb-949}/");
        SeedSource(db, movieId, "/media/movies/Heat (1995) {tmdb-949}/Heat (1995) {imdb-tt0113277} - Bluray-1080p.mkv");
        SeedEntity(db, identifiedMovieId, EntityKind.Movie.ToCode(), "Heat");
        SeedFolder(db, identifiedMovieId, "/media/movies/Heat (1995) {tmdb-949}");
        db.EntityExternalIds.Add(new EntityExternalIdRow {
            Id = Guid.NewGuid(),
            EntityId = identifiedMovieId,
            Provider = ExternalIdProviders.Tmdb,
            Value = "123",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        SeedEntity(db, seriesId, EntityKind.VideoSeries.ToCode(), "The Wire");
        SeedFolder(db, seriesId, "/media/tv/The Wire (2002) [tvdbid-79126]");
        SeedEntity(db, seasonId, EntityKind.VideoSeason.ToCode(), "Season 1");
        SeedFolder(db, seasonId, "/media/tv/The Wire (2002) [tvdbid-79126]/Season 01 {tmdb-1438}");
        await db.SaveChangesAsync();

        var resolver = new IdentifyMatchHintResolver(db);
        var movie = await resolver.ResolveAsync(movieId, ExternalIdProviders.Tmdb, CancellationToken.None);
        var identified = await resolver.ResolveAsync(identifiedMovieId, ExternalIdProviders.Tmdb, CancellationToken.None);
        var series = await resolver.ResolveAsync(seriesId, ExternalIdProviders.Tmdb, CancellationToken.None);
        var season = await resolver.ResolveAsync(seasonId, ExternalIdProviders.Tmdb, CancellationToken.None);

        Assert.Equal("949", movie.ExternalIds[ExternalIdProviders.Tmdb]);
        Assert.Equal("tt0113277", movie.ExternalIds[ExternalIdProviders.Imdb]);
        Assert.Equal("123", identified.ExternalIds[ExternalIdProviders.Tmdb]);
        Assert.Equal("79126", series.ExternalIds[ExternalIdProviders.Tvdb]);
        Assert.Empty(season.ExternalIds);
    }

    [Theory]
    [InlineData("Heat (1995) {tmdb-949}", "tmdb", "949")]
    [InlineData("Heat (1995) [tmdbid-949]", "tmdb", "949")]
    [InlineData("Heat (1995) [TMDBID=949]", "tmdb", "949")]
    [InlineData("The Wire (2002) {tvdb-79126}", "tvdb", "79126")]
    [InlineData("Heat (1995) {imdb-TT0113277}", "imdb", "tt0113277")]
    [InlineData("Heat (1995) [imdbid-tt0113277]", "imdb", "tt0113277")]
    public void PathIdentityTagsReadsManagerAndServerNaming(string name, string provider, string value) {
        Assert.Equal(value, PathIdentityTags.Parse([name])[provider]);
    }

    [Theory]
    [InlineData("Heat (1995) {tmdb-949]")]
    [InlineData("Heat (1995) {tmdb-0}")]
    [InlineData("Heat (1995) {tmdb-abc}")]
    [InlineData("Heat (1995) {imdb-0113277}")]
    [InlineData("Heat (1995) {tmdb-949} {tmdb-950}")]
    [InlineData("Heat (1995) {edition-Director's Cut}")]
    public void PathIdentityTagsIgnoresMalformedAndConflictingTags(string name) {
        Assert.Empty(PathIdentityTags.Parse([name]));
    }

    private static void SeedFolder(PrismediaDbContext db, Guid entityId, string folder) {
        db.EntitySources.Add(new EntitySourceRow {
            EntityId = entityId,
            Code = EntitySourceCode.Folder.ToCode(),
            Value = folder,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }

    private static void SeedSource(PrismediaDbContext db, Guid entityId, string path) {
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Role = EntityFileRole.Source,
            Path = path,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"identify-hints-{Guid.NewGuid():N}")
            .Options;

        return new PrismediaDbContext(options);
    }

    private static void SeedEntity(PrismediaDbContext db, Guid id, string kind, string title) {
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = kind,
            Title = title,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }
}
