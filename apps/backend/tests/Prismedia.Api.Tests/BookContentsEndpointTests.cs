using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prismedia.Application.Books;
using Prismedia.Contracts.Books;

namespace Prismedia.Api.Tests;

public sealed class BookContentsEndpointTests {
    [Fact]
    public async Task ReturnsCompactBookContents() {
        var bookId = Guid.NewGuid();
        var contents = new BookContentsResponse([
            new BookContentsEntry(
                "chapter.xhtml",
                "Chapter One",
                "chapter.xhtml",
                0,
                0,
                0,
                0d,
                1d)
        ]);
        using var factory = CreateFactory(new FakeBookContentsService(bookId, contents));
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync($"/api/books/{bookId}/contents");
        var body = await response.Content.ReadFromJsonAsync<BookContentsResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("Chapter One", Assert.Single(body.Items).Title);
    }

    [Fact]
    public async Task ReturnsNotFoundWhenTheBookHasNoEpubContents() {
        var bookId = Guid.NewGuid();
        using var factory = CreateFactory(new FakeBookContentsService(bookId, null));
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync($"/api/books/{bookId}/contents");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(IBookContentsService contents) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.RemoveAll<IBookContentsService>();
                    services.AddSingleton(contents);
                });
            })
            .WithTestAuth();

    private sealed class FakeBookContentsService(Guid bookId, BookContentsResponse? response)
        : IBookContentsService {
        public Task<BookContentsResponse?> GetAsync(Guid requestedBookId, CancellationToken cancellationToken) =>
            Task.FromResult(requestedBookId == bookId ? response : null);
    }
}
