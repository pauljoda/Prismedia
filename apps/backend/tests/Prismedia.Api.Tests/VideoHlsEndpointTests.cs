using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Prismedia.Application.Videos;

namespace Prismedia.Api.Tests;

public sealed class VideoHlsEndpointTests : IDisposable {
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"prismedia-api-hls-{Guid.NewGuid():N}");

    public VideoHlsEndpointTests() {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task HlsManifestEndpointServesMpegUrlAsset() {
        var filePath = Path.Combine(_tempDir, "master.m3u8");
        await File.WriteAllTextAsync(filePath, "#EXTM3U");
        using var factory = CreateFactory(new FakeHlsAssetService(
            new HlsAsset(filePath, "application/vnd.apple.mpegurl", "public, max-age=60")));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/Videos/{FakeHlsAssetService.VideoId}/master.m3u8");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.apple.mpegurl", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=60", response.Headers.CacheControl?.ToString());
        Assert.Equal("#EXTM3U", body);
    }

    [Fact]
    public async Task HlsAssetEndpointsSupportHeadProbes() {
        var filePath = Path.Combine(_tempDir, "seg_00000.ts");
        await File.WriteAllTextAsync(filePath, "0123456789");
        using var factory = CreateFactory(new FakeHlsAssetService(
            new HlsAsset(filePath, "video/mp2t", "public, max-age=31536000, immutable")));
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Head,
            $"/Videos/{FakeHlsAssetService.VideoId}/hls/720p/seg_00000.ts"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(10, response.Content.Headers.ContentLength);
        Assert.Equal("video/mp2t", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task HlsAssetEndpointReturnsProblemDetailsWhenMissing() {
        using var factory = CreateFactory(new FakeHlsAssetService(null));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/Videos/{FakeHlsAssetService.VideoId}/hls/720p/seg_00000.ts");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task SubtitleEndpointServesStoredSubtitleAsset() {
        var trackId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var filePath = Path.Combine(_tempDir, "track.vtt");
        await File.WriteAllTextAsync(filePath, "WEBVTT");
        using var factory = CreateFactory(
            new FakeHlsAssetService(null),
            new FakeVideoSubtitleAssetService(new VideoSubtitleAsset(filePath, "text/vtt; charset=utf-8")));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/videos/{FakeHlsAssetService.VideoId}/subtitles/{trackId}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/vtt", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("WEBVTT", body);
    }

    public void Dispose() {
        if (Directory.Exists(_tempDir)) {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(IHlsAssetService hlsAssets) {
        return CreateFactory(hlsAssets, new FakeVideoSubtitleAssetService(null));
    }

    private static WebApplicationFactory<Program> CreateFactory(
        IHlsAssetService hlsAssets,
        IVideoSubtitleAssetService subtitleAssets) {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton(hlsAssets);
                    services.AddSingleton(subtitleAssets);
                });
            });
    }

    private sealed class FakeHlsAssetService : IHlsAssetService {
        public static readonly Guid VideoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private readonly HlsAsset? _asset;

        public FakeHlsAssetService(HlsAsset? asset) {
            _asset = asset;
        }

        public Task<HlsAsset?> GetAssetAsync(
            Guid id,
            string assetPath,
            int? audioStreamIndex,
            CancellationToken cancellationToken) {
            return Task.FromResult(id == VideoId ? _asset : null);
        }
    }

    private sealed class FakeVideoSubtitleAssetService : IVideoSubtitleAssetService {
        private readonly VideoSubtitleAsset? _asset;

        public FakeVideoSubtitleAssetService(VideoSubtitleAsset? asset) {
            _asset = asset;
        }

        public Task<VideoSubtitleAsset?> GetSubtitleAsync(
            Guid videoId,
            Guid trackId,
            CancellationToken cancellationToken) {
            return Task.FromResult(videoId == FakeHlsAssetService.VideoId ? _asset : null);
        }

        public Task<VideoSubtitleAsset?> GetSubtitleSourceAsync(
            Guid videoId,
            Guid trackId,
            CancellationToken cancellationToken) {
            return Task.FromResult(videoId == FakeHlsAssetService.VideoId ? _asset : null);
        }
    }
}
