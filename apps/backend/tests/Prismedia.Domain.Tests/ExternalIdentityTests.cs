using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;

namespace Prismedia.Domain.Tests;

public sealed class ExternalIdentityTests {
    [Fact]
    public void ConstructorNormalizesNamespaceAndTrimsOpaqueValue() {
        var identity = new ExternalIdentity("  TmDb  ", "  Movie-AbC123  ");

        Assert.Equal("tmdb", identity.Namespace);
        Assert.Equal("Movie-AbC123", identity.Value);
    }

    [Fact]
    public void EqualityUsesTheNormalizedNamespaceAndTrimmedValue() {
        var first = new ExternalIdentity(" TMDB ", " 603 ");
        var second = new ExternalIdentity("tmdb", "603");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void OpaqueValuesRemainCaseSensitive() {
        var upperCaseValue = new ExternalIdentity("openlibrary", "OL45883W");
        var lowerCaseValue = new ExternalIdentity("openlibrary", "ol45883w");

        Assert.NotEqual(upperCaseValue, lowerCaseValue);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ConstructorRejectsBlankNamespaces(string? value) {
        var exception = Assert.Throws<ArgumentException>(() => new ExternalIdentity(value!, "603"));

        Assert.Equal("namespace", exception.ParamName);
    }

    [Theory]
    [InlineData("tm db")]
    [InlineData("tmdb:movie")]
    [InlineData("/tmdb")]
    [InlineData("tmdb@primary")]
    public void ConstructorRejectsNamespacesThatCannotBeUsedAsStableCodes(string value) {
        var exception = Assert.Throws<ArgumentException>(() => new ExternalIdentity(value, "603"));

        Assert.Equal("namespace", exception.ParamName);
    }

    [Theory]
    [InlineData("openlibrary.work")]
    [InlineData("isbn-13")]
    [InlineData("provider_v2")]
    public void ConstructorAcceptsStableNamespaceSeparators(string value) {
        Assert.Equal(value, new ExternalIdentity(value, "603").Namespace);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ConstructorRejectsBlankValues(string? value) {
        var exception = Assert.Throws<ArgumentException>(() => new ExternalIdentity("tmdb", value!));

        Assert.Equal("value", exception.ParamName);
    }

    [Theory]
    [InlineData("https://www.themoviedb.org/movie/603")]
    [InlineData("FTP://metadata.example/603")]
    [InlineData("//metadata.example/603")]
    [InlineData("www.metadata.example/603")]
    public void ConstructorRejectsUrlShapedValues(string value) {
        var exception = Assert.Throws<ArgumentException>(() => new ExternalIdentity("tmdb", value));

        Assert.Equal("value", exception.ParamName);
        Assert.Contains("URLs", exception.Message);
    }

    [Fact]
    public void EntityExternalIdExposesItsCanonicalIdentity() {
        var externalId = new EntityExternalId(
            " TMDB ",
            " 603 ",
            "https://www.themoviedb.org/movie/603");

        Assert.Equal(new ExternalIdentity("tmdb", "603"), externalId.Identity);
        Assert.Equal("tmdb", externalId.Provider);
        Assert.Equal("603", externalId.Value);
        Assert.Equal("https://www.themoviedb.org/movie/603", externalId.Url);
    }

    [Fact]
    public void EntityReplacesAnIdentityFromTheSameNormalizedNamespace() {
        var movie = new Movie(Guid.NewGuid(), "The Matrix");
        movie.SetExternalId(new ExternalIdentity(" TMDB ", "603"));

        movie.SetExternalId(new ExternalIdentity("tmdb", "604"));

        var externalId = Assert.Single(movie.ExternalIds);
        Assert.Equal(new ExternalIdentity("tmdb", "604"), externalId.Identity);
    }
}
