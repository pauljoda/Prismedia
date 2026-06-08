using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Prismedia.Application.Entities;
using Prismedia.Application.Videos;
using Prismedia.Contracts.Playback;

namespace Prismedia.Api.Tests;

public sealed class JellyfinPlaybackEndpointTests : IDisposable {
    private static readonly Guid VideoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"prismedia-jellyfin-api-{Guid.NewGuid():N}");

    public JellyfinPlaybackEndpointTests() {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task PlaybackInfoEndpointReturnsJellyfinStyleMediaSource() {
        using var factory = CreateFactory(playback: new FakePlaybackInfoService(new PlaybackInfoResponse(
            "play-session",
            [
                new MediaSourceInfo(
                    VideoId.ToString("N"),
                    "/media/movie.mkv",
                    "File",
                    "mkv",
                    1234,
                    "movie.mkv",
                    TimeSpan.FromMinutes(5).Ticks,
                    SupportsDirectPlay: false,
                    SupportsDirectStream: false,
                    SupportsTranscoding: true,
                    TranscodingUrl: $"/Videos/{VideoId}/master.m3u8?PlaySessionId=play-session",
                    TranscodingSubProtocol: "hls",
                    TranscodingContainer: "ts",
                    MediaStreams:
                    [
                        new MediaStreamInfo(0, "Video", "hevc", null, "Video", 1920, 1080, 23.976, null, null, null, IsDefault: true)
                    ],
                    TranscodingInfo: new TranscodingInfo("ts", "h264", "aac", "hls", IsVideoDirect: false, IsAudioDirect: false))
            ])));
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync($"/Items/{VideoId}/PlaybackInfo", new PlaybackInfoRequest {
            EnableDirectPlay = true,
            EnableTranscoding = true
        });
        var body = await response.Content.ReadFromJsonAsync<PlaybackInfoResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("play-session", body.PlaySessionId);
        Assert.False(body.MediaSources.Single().SupportsDirectPlay);
        Assert.Equal("hls", body.MediaSources.Single().TranscodingSubProtocol);
        Assert.StartsWith($"/Videos/{VideoId}/master.m3u8", body.MediaSources.Single().TranscodingUrl);
    }

    [Fact]
    public async Task MasterPlaylistEndpointMapsToMasterHlsAsset() {
        var path = Path.Combine(_tempDir, "master.m3u8");
        await File.WriteAllTextAsync(path, "#EXTM3U\n");
        var hls = new RecordingHlsAssetService(new HlsAsset(path, "application/vnd.apple.mpegurl", "public, max-age=60"));
        using var factory = CreateFactory(hls: hls);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync($"/Videos/{VideoId}/master.m3u8");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("master.m3u8", hls.LastAssetPath);
        Assert.Equal("#EXTM3U\n", body);
    }

    [Fact]
    public async Task HlsSegmentEndpointMapsJellyfinRouteToVariantAsset() {
        var path = Path.Combine(_tempDir, "seg_00000.ts");
        await File.WriteAllTextAsync(path, "segment");
        var hls = new RecordingHlsAssetService(new HlsAsset(path, "video/mp2t", "public, max-age=31536000, immutable"));
        using var factory = CreateFactory(hls: hls);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync($"/Videos/{VideoId}/hls/720p/seg_00000.ts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("v/720p/seg_00000.ts", hls.LastAssetPath);
    }

    [Fact]
    public async Task HlsVariantEndpointMapsJellyfinRouteToVariantPlaylist() {
        var path = Path.Combine(_tempDir, "index.m3u8");
        await File.WriteAllTextAsync(path, "#EXTM3U\n");
        var hls = new RecordingHlsAssetService(new HlsAsset(path, "application/vnd.apple.mpegurl", "public, max-age=60"));
        using var factory = CreateFactory(hls: hls);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync($"/Videos/{VideoId}/hls/720p/stream.m3u8");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("v/720p/stream.m3u8", hls.LastAssetPath);
    }

    [Fact]
    public async Task HlsVariantEndpointPassesAudioStreamSelection() {
        var path = Path.Combine(_tempDir, "index.m3u8");
        await File.WriteAllTextAsync(path, "#EXTM3U\n");
        var hls = new RecordingHlsAssetService(new HlsAsset(path, "application/vnd.apple.mpegurl", "public, max-age=60"));
        using var factory = CreateFactory(hls: hls);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync($"/Videos/{VideoId}/hls/720p/stream.m3u8?AudioStreamIndex=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("v/720p/stream.m3u8", hls.LastAssetPath);
        Assert.Equal(2, hls.LastAudioStreamIndex);
    }

    [Fact]
    public async Task HlsVariantEndpointAcceptsMasterPlaylistRelativeUrls() {
        var path = Path.Combine(_tempDir, "index.m3u8");
        await File.WriteAllTextAsync(path, "#EXTM3U\n");
        var hls = new RecordingHlsAssetService(new HlsAsset(path, "application/vnd.apple.mpegurl", "public, max-age=60"));
        using var factory = CreateFactory(hls: hls);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync($"/Videos/{VideoId}/v/720p/index.m3u8");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("v/720p/index.m3u8", hls.LastAssetPath);
    }

    [Fact]
    public async Task TrickplayPlaylistEndpointServesImagesOnlyPlaylist() {
        using var factory = CreateFactory(trickplay: new FakeTrickplayService(
            new TrickplayPlaylist("#EXTM3U\n#EXT-X-IMAGES-ONLY\n", "public, max-age=60"),
            null));
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync($"/Videos/{VideoId}/Trickplay/320/tiles.m3u8");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.apple.mpegurl", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("#EXT-X-IMAGES-ONLY", body);
    }

    [Fact]
    public async Task SessionProgressEndpointRecordsJellyfinProgressPayload() {
        var sessions = new RecordingPlaybackSessionService();
        using var factory = CreateFactory(sessions: sessions);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync("/Sessions/Playing/Progress", new PlaybackSessionRequest {
            ItemId = VideoId,
            PlaySessionId = "play-session",
            PositionTicks = TimeSpan.FromSeconds(42).Ticks
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.NotNull(sessions.LastProgress);
        Assert.Equal(VideoId, sessions.LastProgress.ItemId);
        Assert.Equal(TimeSpan.FromSeconds(42).Ticks, sessions.LastProgress.PositionTicks);
    }

    [Fact]
    public async Task UserScopedPlayedItemEndpointRecordsInfuseWatchedToggle() {
        var sessions = new RecordingPlaybackSessionService();
        using var factory = CreateFactory(sessions: sessions);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync($"/Users/{Guid.NewGuid():N}/PlayedItems/{VideoId:N}", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(VideoId, sessions.LastMarkedPlayed);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UserScopedPlayedItemDeleteRecordsInfuseUnwatchedToggle() {
        var sessions = new RecordingPlaybackSessionService();
        using var factory = CreateFactory(sessions: sessions);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.DeleteAsync($"/Users/{Guid.NewGuid():N}/PlayedItems/{VideoId:N}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(VideoId, sessions.LastMarkedUnplayed);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    public void Dispose() {
        if (Directory.Exists(_tempDir)) {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(
        IPlaybackInfoService? playback = null,
        IHlsAssetService? hls = null,
        ITrickplayService? trickplay = null,
        IPlaybackSessionService? sessions = null,
        IVideoSourceService? sources = null,
        ITranscodeSessionService? transcodes = null) {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton(playback ?? new FakePlaybackInfoService(null));
                    services.AddSingleton(hls ?? new RecordingHlsAssetService(null));
                    services.AddSingleton(trickplay ?? new FakeTrickplayService(null, null));
                    services.AddSingleton(sessions ?? new RecordingPlaybackSessionService());
                    services.AddSingleton(sources ?? new FakeVideoSourceService(null));
                    services.AddSingleton(transcodes ?? new RecordingTranscodeSessionService());
                    services.AddSingleton<IEntityReadService, TestAuth.VisibleEntityReadService>();
                });
            })
            .WithTestAuth();
    }

    private sealed class FakePlaybackInfoService : IPlaybackInfoService {
        private readonly PlaybackInfoResult? _response;

        public FakePlaybackInfoService(PlaybackInfoResponse? response) {
            _response = response is null
                ? null
                : new PlaybackInfoResult(
                    response.PlaySessionId,
                    response.MediaSources.Select(source => new MediaSourceInfoResult(
                        source.Id,
                        source.Path,
                        source.Protocol,
                        source.Container,
                        source.Size,
                        source.Name,
                        source.RunTimeTicks,
                        source.SupportsDirectPlay,
                        source.SupportsDirectStream,
                        source.SupportsTranscoding,
                        source.TranscodingUrl,
                        source.TranscodingSubProtocol,
                        source.TranscodingContainer,
                        source.MediaStreams.Select(stream => new MediaStreamInfoResult(
                            stream.Index,
                            stream.Type,
                            stream.Codec,
                            stream.Language,
                            stream.DisplayTitle,
                            stream.Width,
                            stream.Height,
                            stream.AverageFrameRate,
                            stream.BitRate,
                            stream.SampleRate,
                            stream.Channels,
                            stream.IsDefault,
                            stream.IsForced)).ToArray(),
                        source.TranscodingInfo is null
                            ? null
                            : new TranscodingInfoResult(
                                source.TranscodingInfo.Container,
                                source.TranscodingInfo.VideoCodec,
                                source.TranscodingInfo.AudioCodec,
                                source.TranscodingInfo.Protocol,
                                source.TranscodingInfo.IsVideoDirect,
                                source.TranscodingInfo.IsAudioDirect))).ToArray(),
                    response.ErrorCode);
        }

        public Task<PlaybackInfoResult?> GetPlaybackInfoAsync(
            Guid itemId,
            PlaybackInfoQuery? request,
            CancellationToken cancellationToken) =>
            Task.FromResult(itemId == VideoId ? _response : null);
    }

    private sealed class RecordingHlsAssetService : IHlsAssetService {
        private readonly HlsAsset? _asset;

        public RecordingHlsAssetService(HlsAsset? asset) {
            _asset = asset;
        }

        public string? LastAssetPath { get; private set; }
        public int? LastAudioStreamIndex { get; private set; }

        public Task<HlsAsset?> GetAssetAsync(
            Guid id,
            string assetPath,
            int? audioStreamIndex,
            CancellationToken cancellationToken) {
            LastAssetPath = assetPath;
            LastAudioStreamIndex = audioStreamIndex;
            return Task.FromResult(id == VideoId ? _asset : null);
        }
    }

    private sealed class FakeTrickplayService : ITrickplayService {
        private readonly TrickplayPlaylist? _playlist;
        private readonly TrickplayTile? _tile;

        public FakeTrickplayService(TrickplayPlaylist? playlist, TrickplayTile? tile) {
            _playlist = playlist;
            _tile = tile;
        }

        public Task<TrickplayPlaylist?> GetPlaylistAsync(Guid itemId, int width, CancellationToken cancellationToken) =>
            Task.FromResult(itemId == VideoId ? _playlist : null);

        public Task<TrickplayTile?> GetTileAsync(Guid itemId, int width, int index, CancellationToken cancellationToken) =>
            Task.FromResult(itemId == VideoId ? _tile : null);
    }

    private sealed class RecordingPlaybackSessionService : IPlaybackSessionService {
        public PlaybackSessionCommand? LastProgress { get; private set; }
        public Guid? LastMarkedPlayed { get; private set; }
        public Guid? LastMarkedUnplayed { get; private set; }

        public Task StartAsync(PlaybackSessionCommand request, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ProgressAsync(PlaybackSessionCommand request, CancellationToken cancellationToken) {
            LastProgress = request;
            return Task.CompletedTask;
        }

        public Task PingAsync(PlaybackSessionCommand request, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(PlaybackSessionCommand request, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<UserItemDataResult?> MarkPlayedAsync(Guid itemId, CancellationToken cancellationToken) {
            LastMarkedPlayed = itemId;
            return Task.FromResult<UserItemDataResult?>(new UserItemDataResult(true));
        }

        public Task<UserItemDataResult?> MarkUnplayedAsync(Guid itemId, CancellationToken cancellationToken) {
            LastMarkedUnplayed = itemId;
            return Task.FromResult<UserItemDataResult?>(new UserItemDataResult(false));
        }
    }

    private sealed class FakeVideoSourceService : IVideoSourceService {
        private readonly VideoSourceFile? _source;

        public FakeVideoSourceService(VideoSourceFile? source) {
            _source = source;
        }

        public Task<VideoSourceFile?> GetSourceAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(id == VideoId ? _source : null);
    }

    private sealed class RecordingTranscodeSessionService : ITranscodeSessionService {
        public void Register(string playSessionId, Guid itemId) {
        }

        public void Ping(string playSessionId) {
        }

        public Task CancelAsync(string playSessionId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<int> CancelAllAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public IReadOnlySet<Guid> LiveItemIds(TimeSpan within) => new HashSet<Guid>();

        public int ReapStaleSessions(TimeSpan ttl) => 0;
    }
}
