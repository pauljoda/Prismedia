using System.Net;
using System.IO.Compression;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Prismedia.Application.Entities;

namespace Prismedia.Api.Tests;

public sealed class EntityFileEndpointTests : IDisposable {
    private static readonly Guid EntityId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"prismedia-entity-file-{Guid.NewGuid():N}");

    public EntityFileEndpointTests() {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task EntityFileEndpointStreamsRequestedRoleWithRangeSupport() {
        var filePath = Path.Combine(_tempDir, "source.jpg");
        await File.WriteAllTextAsync(filePath, "0123456789");
        using var factory = CreateFactory(new FakeEntityFileContentService(
            new EntityFileContent(EntityId, "source", filePath, "image/jpeg")));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/entities/{EntityId}/files/source");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(2, 5);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("bytes", response.Headers.AcceptRanges.Single());
        Assert.Equal("bytes 2-5/10", response.Content.Headers.ContentRange?.ToString());
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("2345", body);
    }

    [Fact]
    public async Task EntityFileEndpointSupportsHeadProbes() {
        var filePath = Path.Combine(_tempDir, "source.mp4");
        await File.WriteAllTextAsync(filePath, "0123456789");
        using var factory = CreateFactory(new FakeEntityFileContentService(
            new EntityFileContent(EntityId, "preview", filePath, "video/mp4")));
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Head,
            $"/api/entities/{EntityId}/files/preview"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(10, response.Content.Headers.ContentLength);
        Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("bytes", response.Headers.AcceptRanges.Single());
    }

    [Fact]
    public async Task EntityFileEndpointReturnsNotFoundForMissingRole() {
        using var factory = CreateFactory(new FakeEntityFileContentService(null));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/entities/{EntityId}/files/source");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EntityFileEndpointReturnsNotFoundWhenFileIsMissing() {
        var filePath = Path.Combine(_tempDir, "missing.jpg");
        using var factory = CreateFactory(new FakeEntityFileContentService(
            new EntityFileContent(EntityId, "source", filePath, "image/jpeg")));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/entities/{EntityId}/files/source");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EntityFileEndpointStreamsZipArchiveEntrySources() {
        var archivePath = Path.Combine(_tempDir, "book.cbz");
        await using (var archiveStream = File.Create(archivePath))
        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create)) {
            var entry = archive.CreateEntry("chapter/page-001.jpg");
            await using var entryStream = entry.Open();
            await using var writer = new StreamWriter(entryStream);
            await writer.WriteAsync("page image");
        }

        using var factory = CreateFactory(new FakeEntityFileContentService(
            new EntityFileContent(EntityId, "source", $"{archivePath}::chapter/page-001.jpg", "image/jpeg")));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/entities/{EntityId}/files/source");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("page image", body);
    }

    public void Dispose() {
        if (Directory.Exists(_tempDir)) {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(IEntityFileContentService contentService) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton(contentService);
                });
            });

    private sealed class FakeEntityFileContentService(EntityFileContent? content) : IEntityFileContentService {
        public Task<EntityFileContent?> GetContentAsync(
            Guid entityId,
            string role,
            CancellationToken cancellationToken) =>
            Task.FromResult(entityId == EntityId ? content : null);
    }
}
