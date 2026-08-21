using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Serialization;

namespace Prismedia.Api.Tests;

public sealed class EntityReaderEndpointTests : IDisposable {
    private static readonly JsonSerializerOptions CodecJson =
        new(JsonSerializerDefaults.Web) { Converters = { new CodecJsonConverterFactory() } };

    private readonly string _pagePath = Path.Combine(
        Path.GetTempPath(),
        $"prismedia-reader-page-{Guid.NewGuid():N}.png");

    [Fact]
    public async Task ReturnsTheGenericManifestAndExactPageBytes() {
        var entityId = Guid.NewGuid();
        var bytes = new byte[] { 0x89, 0x50, 0x4e, 0x47 };
        await File.WriteAllBytesAsync(_pagePath, bytes);
        var manifest = new EntityReaderManifestResponse(
            entityId,
            PageReadingDirection.RightToLeft,
            ReaderMode.Paged,
            0,
            [new EntityReaderManifestPage(0, "image/png", 1200, 1800, PageType.FrontCover, false, "sha256:page")]);
        using var factory = CreateFactory(new FakeReaderService(entityId, manifest, new(_pagePath, "image/png")));
        using var client = factory.CreateAuthenticatedClient();

        using var manifestResponse = await client.GetAsync($"/api/entities/{entityId}/reader-manifest");
        var body = await manifestResponse.Content.ReadFromJsonAsync<EntityReaderManifestResponse>(CodecJson);
        using var pageResponse = await client.GetAsync($"/api/entities/{entityId}/reader-pages/0");

        Assert.Equal(HttpStatusCode.OK, manifestResponse.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(PageReadingDirection.RightToLeft, body.Direction);
        Assert.Equal(PageType.FrontCover, Assert.Single(body.Pages).PageType);
        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        Assert.Equal("image/png", pageResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes, await pageResponse.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task MissingManifestOrOrdinalReturnsNotFound() {
        var entityId = Guid.NewGuid();
        using var factory = CreateFactory(new FakeReaderService(entityId, null, null));
        using var client = factory.CreateAuthenticatedClient();

        using var manifest = await client.GetAsync($"/api/entities/{entityId}/reader-manifest");
        using var page = await client.GetAsync($"/api/entities/{entityId}/reader-pages/99");

        Assert.Equal(HttpStatusCode.NotFound, manifest.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
    }

    public void Dispose() {
        try {
            File.Delete(_pagePath);
        } catch {
            // Best-effort test cleanup.
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(IEntityReaderService reader) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.RemoveAll<IEntityReaderService>();
                    services.AddSingleton(reader);
                });
            })
            .WithTestAuth();

    private sealed class FakeReaderService(
        Guid entityId,
        EntityReaderManifestResponse? manifest,
        EntityReaderPageSource? page) : IEntityReaderService {
        public Task<EntityReaderManifestResponse?> GetManifestAsync(
            Guid requestedEntityId,
            CancellationToken cancellationToken) =>
            Task.FromResult(requestedEntityId == entityId ? manifest : null);

        public Task<EntityReaderPageSource?> GetPageAsync(
            Guid requestedEntityId,
            int ordinal,
            CancellationToken cancellationToken) =>
            Task.FromResult(requestedEntityId == entityId && ordinal == 0 ? page : null);
    }
}
