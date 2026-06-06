using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Prismedia.Application.Entities;
using Prismedia.Application.Videos;

namespace Prismedia.Api.Tests;

public sealed class VideoStreamEndpointTests : IDisposable {
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"prismedia-stream-{Guid.NewGuid():N}");

    public VideoStreamEndpointTests() {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task StreamEndpointSupportsByteRangeRequests() {
        var filePath = Path.Combine(_tempDir, "source.mp4");
        await File.WriteAllTextAsync(filePath, "0123456789");
        using var factory = CreateFactory(new FakeVideoSourceService(
            new VideoSourceFile(FakeVideoSourceService.VideoId, filePath, "video/mp4", true)));
        using var client = factory.CreateAuthenticatedClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/Videos/{FakeVideoSourceService.VideoId}/stream");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(2, 5);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("bytes", response.Headers.AcceptRanges.Single());
        Assert.Equal("bytes 2-5/10", response.Content.Headers.ContentRange?.ToString());
        Assert.Equal("2345", body);
    }

    [Fact]
    public async Task StreamEndpointSupportsHeadProbes() {
        var filePath = Path.Combine(_tempDir, "source.mp4");
        await File.WriteAllTextAsync(filePath, "0123456789");
        using var factory = CreateFactory(new FakeVideoSourceService(
            new VideoSourceFile(FakeVideoSourceService.VideoId, filePath, "video/mp4", true)));
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Head,
            $"/Videos/{FakeVideoSourceService.VideoId}/stream"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(10, response.Content.Headers.ContentLength);
        Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("bytes", response.Headers.AcceptRanges.Single());
    }

    [Fact]
    public async Task StreamEndpointServesNonBrowserDirectPlayableSourcesForJellyfinClients() {
        var filePath = Path.Combine(_tempDir, "source.mkv");
        await File.WriteAllTextAsync(filePath, "0123456789");
        using var factory = CreateFactory(new FakeVideoSourceService(
            new VideoSourceFile(FakeVideoSourceService.VideoId, filePath, "video/x-matroska", false)));
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync($"/Videos/{FakeVideoSourceService.VideoId}/stream");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("video/x-matroska", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("0123456789", body);
    }

    [Fact]
    public async Task StreamEndpointAcceptsJellyfinContainerSuffix() {
        var filePath = Path.Combine(_tempDir, "source.mp4");
        await File.WriteAllTextAsync(filePath, "0123456789");
        using var factory = CreateFactory(new FakeVideoSourceService(
            new VideoSourceFile(FakeVideoSourceService.VideoId, filePath, "video/mp4", true)));
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync($"/Videos/{FakeVideoSourceService.VideoId}/stream.mp4");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("0123456789", body);
    }

    public void Dispose() {
        if (Directory.Exists(_tempDir)) {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(IVideoSourceService sourceService) {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton(sourceService);
                    services.AddSingleton<IEntityReadService, TestAuth.VisibleEntityReadService>();
                });
            })
            .WithTestAuth();
    }

    private sealed class FakeVideoSourceService : IVideoSourceService {
        public static readonly Guid VideoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private readonly VideoSourceFile? _source;

        public FakeVideoSourceService(VideoSourceFile? source) {
            _source = source;
        }

        public Task<VideoSourceFile?> GetSourceAsync(Guid id, CancellationToken cancellationToken) {
            return Task.FromResult(id == VideoId ? _source : null);
        }
    }

}
