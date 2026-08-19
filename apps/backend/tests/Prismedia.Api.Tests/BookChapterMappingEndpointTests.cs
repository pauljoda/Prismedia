using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prismedia.Application.Books;
using Prismedia.Contracts.Books;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Tests;

public sealed class BookChapterMappingEndpointTests {
    [Fact]
    public async Task GetsAndReplacesTheSharedBookChapterMap() {
        var bookId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var initial = new BookChapterMappingsResponse([
            new BookChapterAudioMapping("Text/prologue.xhtml", trackId)
        ]);
        var service = new FakeBookChapterMappingService(bookId, initial);
        using var factory = CreateFactory(service);
        using var client = factory.CreateAuthenticatedClient();

        using var getResponse = await client.GetAsync($"/api/books/{bookId}/chapter-mappings");
        var getBody = await getResponse.Content.ReadFromJsonAsync<BookChapterMappingsResponse>();
        var replacement = new ReplaceBookChapterMappingsRequest([
            new BookChapterAudioMapping("Text/chapter-01.xhtml", trackId)
        ]);
        using var putResponse = await client.PutAsJsonAsync(
            $"/api/books/{bookId}/chapter-mappings",
            replacement);
        var putBody = await putResponse.Content.ReadFromJsonAsync<BookChapterMappingsResponse>();

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal("Text/prologue.xhtml", Assert.Single(getBody!.Mappings).ReadableChapterKey);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        Assert.Equal("Text/chapter-01.xhtml", Assert.Single(putBody!.Mappings).ReadableChapterKey);
        var savedMapping = Assert.Single(service.LastRequest!.Mappings);
        Assert.Equal("Text/chapter-01.xhtml", savedMapping.ReadableChapterKey);
        Assert.Equal(trackId, savedMapping.AudioTrackId);
    }

    [Fact]
    public async Task ReturnsAStableProblemForInvalidMappings() {
        var bookId = Guid.NewGuid();
        var service = new FakeBookChapterMappingService(bookId, new BookChapterMappingsResponse([])) {
            SaveResult = new BookChapterMappingSaveResult(
                BookChapterMappingSaveStatus.Invalid,
                null,
                "An audiobook file can map to only one chapter.")
        };
        using var factory = CreateFactory(service);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PutAsJsonAsync(
            $"/api/books/{bookId}/chapter-mappings",
            new ReplaceBookChapterMappingsRequest([]));
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApiProblemCodes.InvalidBookChapterMapping, problem!.Code);
    }

    private static WebApplicationFactory<Program> CreateFactory(IBookChapterMappingService service) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.RemoveAll<IBookChapterMappingService>();
                    services.AddSingleton(service);
                });
            })
            .WithTestAuth();

    private sealed class FakeBookChapterMappingService(
        Guid bookId,
        BookChapterMappingsResponse response) : IBookChapterMappingService {
        public ReplaceBookChapterMappingsRequest? LastRequest { get; private set; }

        public BookChapterMappingSaveResult SaveResult { get; set; } = new(
            BookChapterMappingSaveStatus.Saved,
            response,
            null);

        public Task<BookChapterMappingsResponse?> GetAsync(
            Guid requestedBookId,
            CancellationToken cancellationToken) =>
            Task.FromResult<BookChapterMappingsResponse?>(requestedBookId == bookId ? response : null);

        public Task<BookChapterMappingSaveResult> ReplaceAsync(
            Guid requestedBookId,
            ReplaceBookChapterMappingsRequest request,
            CancellationToken cancellationToken) {
            LastRequest = request;
            var result = SaveResult.Status == BookChapterMappingSaveStatus.Saved
                ? SaveResult with { Response = new BookChapterMappingsResponse(request.Mappings) }
                : SaveResult;
            return Task.FromResult(result);
        }
    }
}
