using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Tests;

/// <summary>Each managed kind declares its request rules once; these cases pin the rules connected managers rely on.</summary>
public sealed class ManagedFulfillmentPolicyTests {
    [Fact]
    public void OnlyDeclaringKindsCanBeFulfilledByAManager() {
        Assert.True(ManagedFulfillmentPolicy.Supports(EntityKind.Movie));
        Assert.True(ManagedFulfillmentPolicy.Supports(EntityKind.VideoSeries));
        Assert.True(ManagedFulfillmentPolicy.Supports(EntityKind.Book));
        Assert.True(ManagedFulfillmentPolicy.Supports(EntityKind.ComicSeries));
        Assert.False(ManagedFulfillmentPolicy.Supports(EntityKind.Image));
        Assert.Throws<ArgumentException>(() => ManagedFulfillmentPolicy.For(EntityKind.Image));
    }

    [Fact]
    public void RequestsFollowTheirKindsProfileTargetMonitoringAndSearchRules() {
        var movie = ManagedFulfillmentPolicy.For(EntityKind.Movie);
        movie.RequireRequest("1", null, 0, [], monitored: false, search: false);
        Assert.Throws<ArgumentException>(() => movie.RequireRequest(null, null, 0, [], true, true));
        Assert.Throws<ArgumentException>(() => movie.RequireRequest("1", BookRendition.Ebook, 0, [], true, true));

        var series = ManagedFulfillmentPolicy.For(EntityKind.VideoSeries);
        series.RequireRequest("1", null, 2, [null, null], monitored: false, search: true);
        Assert.Throws<ArgumentException>(() => series.RequireRequest("1", null, 0, [], false, true));
        Assert.Throws<ArgumentException>(() => series.RequireRequest("1", null, 1, [null], true, true));
        Assert.Throws<ArgumentException>(() => series.RequireRequest("1", null, 1, [null], false, false));

        var book = ManagedFulfillmentPolicy.For(EntityKind.Book);
        book.RequireRequest(null, BookRendition.Audiobook, 0, [], monitored: true, search: false);
        Assert.Throws<ArgumentException>(() => book.RequireRequest(null, null, 0, [], true, true));
        Assert.Throws<ArgumentException>(() => book.RequireRequest("1", BookRendition.Ebook, 0, [], true, true));
        Assert.Throws<ArgumentException>(() => book.RequireRequest(null, BookRendition.Ebook, 0, [], false, true));

        var comic = ManagedFulfillmentPolicy.For(EntityKind.ComicSeries);
        comic.RequireRequest(null, null, 1, ["½"], monitored: true, search: true);
        Assert.Throws<ArgumentException>(() => comic.RequireRequest(null, null, 1, [null], true, true));
        Assert.Throws<ArgumentException>(() => comic.RequireRequest(null, null, 2, ["1", "2"], true, true));
        Assert.False(comic.CreatesHolding);
    }

    [Theory]
    [InlineData("item", "42", "42", null, null, null, true)]
    [InlineData("item", "42", "43", null, null, null, false)]
    [InlineData("episode", "series", "e1", 1, 2, null, true)]
    [InlineData("episode", "series", "e1", 1, null, null, false)]
    [InlineData("issue", "run", "i1", null, null, "½", true)]
    [InlineData("issue", "run", "i1", null, null, null, false)]
    [InlineData("part", "book", "p1", null, null, "1", false)]
    public void TargetShapesAcceptOnlyTheirIdentity(string shape, string item, string target, int? season, int? episode,
        string? label, bool accepted) =>
        Assert.Equal(accepted, ManagedTargetShape.All.Single(candidate => candidate.Name == shape)
            .Accepts(item, target, season, episode, null, label));

    [Theory]
    [InlineData("tmdb", "603", true)]
    [InlineData("tmdb", "0603", false)]
    [InlineData("comicvine", "4050-1234", true)]
    [InlineData("comicvine", "4000-1234", false)]
    [InlineData("openlibrarywork", "OL45804W", true)]
    [InlineData("openlibrarywork", "OL45804M", false)]
    public void PinningIdentitiesUseTheirProvidersCanonicalSpelling(string provider, string value, bool pinned) {
        var kind = provider switch {
            "tmdb" => EntityKind.Movie,
            "comicvine" => EntityKind.ComicSeries,
            _ => EntityKind.Book
        };
        var identity = ManagedFulfillmentPolicy.For(kind).PinningIdentity(new Dictionary<string, string> { [provider] = value });
        Assert.Equal(pinned, identity is not null);
    }
}
