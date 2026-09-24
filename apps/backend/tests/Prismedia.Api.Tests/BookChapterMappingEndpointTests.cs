using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prismedia.Application.Books;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Books;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Domain.Media.Books;

namespace Prismedia.Api.Tests;

public sealed class BookChapterMappingEndpointTests {
    private static readonly Guid BookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid HiddenBookId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid VideoId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TrackId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid MarkerId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public async Task AlignmentIsBookOnlyAndSavingMappingsReturnsIt() {
        var mappings = new FakeBookChapterMappingService();
        using var factory = CreateFactory(mappings);
        using var client = factory.CreateAuthenticatedClient();

        using var alignmentResponse = await client.GetAsync($"/api/books/{BookId}/alignment");
        using var hiddenResponse = await client.GetAsync($"/api/books/{HiddenBookId}/alignment");
        using var videoResponse = await client.GetAsync($"/api/books/{VideoId}/alignment");
        using var putResponse = await client.PutAsJsonAsync(
            $"/api/books/{BookId}/chapter-mappings",
            new ReplaceBookChapterMappingsRequest([
                new BookChapterAudioMapping("Text/chapter-01.xhtml", TrackId)
            ]));

        Assert.Equal(HttpStatusCode.OK, alignmentResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, hiddenResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, videoResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        using var saved = JsonDocument.Parse(await putResponse.Content.ReadAsStringAsync());
        var row = Assert.Single(saved.RootElement.GetProperty("rows").EnumerateArray());
        Assert.Equal(AlignmentMatchState.Paired.ToCode(), row.GetProperty("matchState").GetString());
        Assert.Equal(BookChapterMappingOrigin.Manual.ToCode(), row.GetProperty("provenance").GetString());
        Assert.Equal(TrackId, row.GetProperty("audio").GetProperty("trackEntityId").GetGuid());
        var combined = saved.RootElement.GetProperty("resume").GetProperty("combined");
        Assert.Equal(AlignmentBasis.FreshStart.ToCode(), combined.GetProperty("basis").GetString());
        var link = saved.RootElement.GetProperty("link");
        Assert.Equal(BookLinkState.Linked.ToCode(), link.GetProperty("state").GetString());
        Assert.Equal(AudiobookStructure.Chaptered.ToCode(), link.GetProperty("audioStructure").GetString());
        Assert.Equal("Text/chapter-01.xhtml", Assert.Single(mappings.LastRequest!.Mappings).ReadableChapterKey);
    }

    [Fact]
    public async Task ReturnsAStableProblemForInvalidMappings() {
        var mappings = new FakeBookChapterMappingService {
            SaveResult = new BookChapterMappingSaveResult(
                BookChapterMappingSaveStatus.Invalid,
                "An audiobook file can map to only one chapter.")
        };
        using var factory = CreateFactory(mappings);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PutAsJsonAsync(
            $"/api/books/{BookId}/chapter-mappings",
            new ReplaceBookChapterMappingsRequest([]));
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApiProblemCodes.InvalidBookChapterMapping, problem!.Code);
    }

    private static WebApplicationFactory<Program> CreateFactory(IBookChapterMappingService mappings) =>
        new WebApplicationFactory<Program>()
            .WithTestAuth()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.RemoveAll<IBookChapterMappingService>();
                    services.AddSingleton(mappings);
                    services.RemoveAll<IWorkAlignmentReader>();
                    services.AddSingleton<IWorkAlignmentReader>(new FakeWorkAlignmentReader());
                    services.RemoveAll<IEntityVisibilityChecker>();
                    services.AddSingleton<IEntityVisibilityChecker>(new FakeVisibilityChecker());
                    services.RemoveAll<IEntityWriteRepository>();
                    services.AddSingleton<IEntityWriteRepository>(new FakeEntityWriteRepository());
                });
            });

    private sealed class FakeBookChapterMappingService : IBookChapterMappingService {
        public ReplaceBookChapterMappingsRequest? LastRequest { get; private set; }

        public BookChapterMappingSaveResult SaveResult { get; set; } = new(BookChapterMappingSaveStatus.Saved, null);

        public Task<BookChapterMappingsResponse?> GetAsync(Guid bookId, CancellationToken cancellationToken) =>
            Task.FromResult<BookChapterMappingsResponse?>(new BookChapterMappingsResponse([], []));

        public Task<BookChapterMappingSaveResult> ReplaceAsync(
            Guid bookId,
            ReplaceBookChapterMappingsRequest request,
            CancellationToken cancellationToken) {
            LastRequest = request;
            return Task.FromResult(SaveResult);
        }
    }

    private sealed class FakeWorkAlignmentReader : IWorkAlignmentReader {
        public Task<WorkAlignment?> LoadAsync(Guid workId, CancellationToken cancellationToken) =>
            Task.FromResult<WorkAlignment?>(new WorkAlignment(
                workId,
                hasReadableRendition: true,
                [new ReadableChapterWindow("Text/chapter-01.xhtml", "Chapter One", 0, "Text/chapter-01.xhtml", null, 0, 0.5, null)],
                new AudiobookRendition([
                    new AudioTrackSpan(TrackId, "Book", 600, [new SourceChapterMarker(MarkerId, "Chapter One", 0, 600)])
                ]),
                [new ChapterPairing("Text/chapter-01.xhtml", TrackId, MarkerId, BookChapterMappingOrigin.Manual)]));
    }

    private sealed class FakeVisibilityChecker : IEntityVisibilityChecker {
        public Task<bool> IsVisibleAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult(entityId != HiddenBookId);
    }

    private sealed class FakeEntityWriteRepository : IEntityWriteRepository {
        public Task<Entity?> FindShallowAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<Entity?>(
                id == BookId || id == HiddenBookId ? new Book(id, "Fixture Book", BookType.Novel) :
                id == VideoId ? new Video(id, "Fixture Clip") :
                null);

        public Task<Guid?> FindParentIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(null);

        public Task SaveMutableStateAsync(
            Entity entity,
            EntityMutableStateChange change,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
