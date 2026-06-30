using Prismedia.Application.Requests;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Requests;

/// <summary>
/// Covers the plugin-only request layer left after the *arr integrations were removed: the search
/// aggregator's kind gating across the book + author plugin sources, and the detail service's
/// author-vs-book routing.
/// </summary>
public sealed class RequestServicesTests {
    [Fact]
    public async Task SearchWithNoKindFilterQueriesBothBookAndAuthorSources() {
        var books = new FakeBookSource(Result(RequestMediaKind.Book, "b1"));
        var authors = new FakeAuthorSource(Result(RequestMediaKind.Author, "a1"));
        var service = new RequestSearchService(books, authors);

        var response = await service.SearchAsync(Request([]), CancellationToken.None);

        Assert.True(books.Called);
        Assert.True(authors.Called);
        Assert.Equal(["b1", "a1"], response.Results.Select(r => r.ExternalId).ToArray());
        Assert.Empty(response.ProviderErrors);
    }

    [Fact]
    public async Task SearchScopedToBooksDoesNotQueryTheAuthorSource() {
        var books = new FakeBookSource(Result(RequestMediaKind.Book, "b1"));
        var authors = new FakeAuthorSource(Result(RequestMediaKind.Author, "a1"));
        var service = new RequestSearchService(books, authors);

        var response = await service.SearchAsync(Request([RequestMediaKind.Book]), CancellationToken.None);

        Assert.True(books.Called);
        Assert.False(authors.Called);
        Assert.Equal(["b1"], response.Results.Select(r => r.ExternalId).ToArray());
    }

    [Fact]
    public async Task SearchScopedToAuthorsDoesNotQueryTheBookSource() {
        var books = new FakeBookSource(Result(RequestMediaKind.Book, "b1"));
        var authors = new FakeAuthorSource(Result(RequestMediaKind.Author, "a1"));
        var service = new RequestSearchService(books, authors);

        var response = await service.SearchAsync(Request([RequestMediaKind.Author]), CancellationToken.None);

        Assert.False(books.Called);
        Assert.True(authors.Called);
        Assert.Equal(["a1"], response.Results.Select(r => r.ExternalId).ToArray());
    }

    [Fact]
    public async Task SearchCapturesAFailingSourceAsAProviderErrorWithoutThrowing() {
        var books = new FakeBookSource(new InvalidOperationException("boom"));
        var authors = new FakeAuthorSource(Result(RequestMediaKind.Author, "a1"));
        var service = new RequestSearchService(books, authors);

        var response = await service.SearchAsync(Request([]), CancellationToken.None);

        Assert.Equal(["a1"], response.Results.Select(r => r.ExternalId).ToArray());
        var error = Assert.Single(response.ProviderErrors);
        Assert.Equal(RequestProviderKind.Plugin, error.Kind);
    }

    [Fact]
    public async Task DetailRoutesAuthorKindToTheAuthorLookup() {
        var source = new FakeDetailSource();
        var service = new RequestDetailService(source);

        await service.GetAsync(RequestProviderKind.Plugin, RequestMediaKind.Author, "openlibrary:OL1A", null, hideNsfw: true, CancellationToken.None);

        Assert.Equal("author", source.LastCall);
        Assert.True(source.LastHideNsfw);
    }

    [Fact]
    public async Task DetailRoutesBookKindToTheBookLookup() {
        var source = new FakeDetailSource();
        var service = new RequestDetailService(source);

        await service.GetAsync(RequestProviderKind.Plugin, RequestMediaKind.Book, "openlibrary:OL1W", null, hideNsfw: false, CancellationToken.None);

        Assert.Equal("book", source.LastCall);
        Assert.False(source.LastHideNsfw);
    }

    private static RequestSearchRequest Request(IReadOnlyList<RequestMediaKind> kinds) =>
        new("query", kinds, [], HideNsfw: false);

    private static RequestSearchResult Result(RequestMediaKind kind, string externalId) =>
        new(Guid.Empty, RequestProviderKind.Plugin, kind, externalId, externalId, null, null, null, null, null,
            null, null, null, null, [], false, null, null, true);

    private sealed class FakeBookSource(object outcome) : IBookMetadataSearchSource {
        public bool Called { get; private set; }

        public Task<IReadOnlyList<RequestSearchResult>> SearchAsync(string query, bool hideNsfw, CancellationToken cancellationToken) {
            Called = true;
            return outcome is Exception ex
                ? Task.FromException<IReadOnlyList<RequestSearchResult>>(ex)
                : Task.FromResult<IReadOnlyList<RequestSearchResult>>([(RequestSearchResult)outcome]);
        }
    }

    private sealed class FakeAuthorSource(object outcome) : IAuthorMetadataSearchSource {
        public bool Called { get; private set; }

        public Task<IReadOnlyList<RequestSearchResult>> SearchAuthorsAsync(string query, bool hideNsfw, CancellationToken cancellationToken) {
            Called = true;
            return outcome is Exception ex
                ? Task.FromException<IReadOnlyList<RequestSearchResult>>(ex)
                : Task.FromResult<IReadOnlyList<RequestSearchResult>>([(RequestSearchResult)outcome]);
        }
    }

    private sealed class FakeDetailSource : IPluginRequestDetailSource {
        public string? LastCall { get; private set; }
        public bool LastHideNsfw { get; private set; }

        public Task<RequestDetailResponse?> GetBookDetailAsync(string externalId, bool hideNsfw, CancellationToken cancellationToken) {
            LastCall = "book";
            LastHideNsfw = hideNsfw;
            return Task.FromResult<RequestDetailResponse?>(null);
        }

        public Task<RequestDetailResponse?> GetAuthorDetailAsync(string externalId, bool hideNsfw, CancellationToken cancellationToken) {
            LastCall = "author";
            LastHideNsfw = hideNsfw;
            return Task.FromResult<RequestDetailResponse?>(null);
        }
    }
}
