using System.Text.Json;
using Prismedia.Application.Videos;
using Prismedia.Infrastructure.Videos;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Playback;
using Prismedia.Contracts.Security;
using Prismedia.Domain.Entities;
using Prismedia.Contracts.Settings;

namespace Prismedia.Infrastructure.Tests;

public sealed class VideoPlaybackPlanServiceTests {
    [Fact]
    public async Task VideoPlaybackPlanExposesAllAudioStreamsAndSelectsRequestedTrack() {
        var videoId = Guid.Parse("12121212-1212-1212-1212-121212121212");
        var service = new VideoPlaybackPlanService(
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                "/media/movie.mkv",
                "video/x-matroska",
                false,
                DurationSeconds: 60,
                Width: 1920,
                Height: 1080,
                Streams:
                [
                    new(0, "Video", "h264", null, "Video", 1920, 1080, 24, null, null, null, true, false),
                    new(1, "Audio", "aac", "spa", "Spanish", null, null, null, null, 48000, 2, false, false),
                    new(2, "Audio", "aac", "eng", "English", null, null, null, null, 48000, 2, true, false)
                ])),
            new TranscodeSessionService());

        var info = await service.CreatePlanAsync(videoId, new VideoPlaybackPlanQuery {
            AudioStreamIndex = 1,
            EnableDirectPlay = true,
            EnableDirectStream = true,
            EnableTranscoding = true
        }, CancellationToken.None);

        Assert.NotNull(info);
        var source = info.Source;
        Assert.StartsWith(
            VideoPlaybackProtocol.HlsPath(videoId, VideoPlaybackProtocol.Hls.MasterPlaylist),
            source.Url);
        Assert.Contains($"{VideoPlaybackProtocol.AudioStreamIndexQuery}=1", source.Url);
        var audioStreams = source.Streams.Where(stream => stream.Type == "Audio").ToList();
        Assert.Equal(2, audioStreams.Count);
        Assert.True(audioStreams.Single(stream => stream.Index == 1).IsDefault);
        Assert.False(audioStreams.Single(stream => stream.Index == 2).IsDefault);
    }

    [Fact]
    public async Task VideoPlaybackPlanSelectsPreferredAudioLanguageBeforeContainerDefault() {
        var videoId = Guid.Parse("34343434-3434-3434-3434-343434343434");
        var service = new VideoPlaybackPlanService(
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                "/media/movie.mkv",
                "video/x-matroska",
                false,
                DurationSeconds: 60,
                Width: 1920,
                Height: 1080,
                Streams:
                [
                    new(0, "Video", "h264", null, "Video", 1920, 1080, 24, null, null, null, true, false),
                    new(1, "Audio", "aac", "spa", "Spanish", null, null, null, null, 48000, 2, true, false),
                    new(2, "Audio", "aac", null, "English - Stereo", null, null, null, null, 48000, 2, false, false)
                ])),
            new TranscodeSessionService(),
            new SettingsService(new FakeSettingsPersistence("en,eng,en-US")));

        var info = await service.CreatePlanAsync(videoId, new VideoPlaybackPlanQuery {
            EnableDirectPlay = true,
            EnableDirectStream = true,
            EnableTranscoding = true
        }, CancellationToken.None);

        Assert.NotNull(info);
        var source = info.Source;
        Assert.Contains($"{VideoPlaybackProtocol.AudioStreamIndexQuery}=2", source.Url);
        var audioStreams = source.Streams.Where(stream => stream.Type == "Audio").ToList();
        Assert.False(audioStreams.Single(stream => stream.Index == 1).IsDefault);
        Assert.True(audioStreams.Single(stream => stream.Index == 2).IsDefault);
    }

    [Fact]
    public async Task VideoPlaybackPlanReportsAacRemuxAudioAsDirectWhenPacketsAreCopied() {
        var videoId = Guid.Parse("23232323-2323-2323-2323-232323232323");
        var service = new VideoPlaybackPlanService(
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                "/media/movie.mkv",
                "video/x-matroska",
                false,
                DurationSeconds: 60,
                Container: "matroska",
                VideoCodec: "h264",
                AudioCodec: "aac",
                Streams:
                [
                    new(0, "Video", "h264", null, "Video", 1920, 1080, 24, null, null, null, true, false),
                    new(1, "Audio", "aac", "eng", "English", null, null, null, null, 48000, 6, true, false)
                ])),
            new TranscodeSessionService());

        var info = await service.CreatePlanAsync(videoId, new VideoPlaybackPlanQuery {
            EnableDirectPlay = true,
            EnableDirectStream = true,
            EnableTranscoding = true,
            Profile = new VideoPlaybackProfile(
                200_000_000,
                [new VideoDirectPlayProfile(StreamKind.Video.ToCode(), "mp4", "h264", "aac")])
        }, CancellationToken.None);

        Assert.NotNull(info);
        var source = info.Source;
        Assert.StartsWith(
            VideoPlaybackProtocol.HlsPath(videoId, $"v/remux/{VideoPlaybackProtocol.Hls.StreamPlaylist}"),
            source.Url);
        Assert.True(source.Transcoding?.IsVideoDirect);
        Assert.True(source.Transcoding?.IsAudioDirect);
    }

    [Fact]
    public async Task VideoPlaybackPlanPreservesNegotiatedMultichannelAudioDuringRemux() {
        var videoId = Guid.Parse("24242424-2424-2424-2424-242424242424");
        var service = new VideoPlaybackPlanService(
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                "/media/movie.mkv",
                "video/x-matroska",
                false,
                DurationSeconds: 60,
                Container: "matroska",
                VideoCodec: "h264",
                AudioCodec: "ac3",
                Streams:
                [
                    new(0, "Video", "h264", null, "Video", 1920, 1080, 24, null, null, null, true, false),
                    new(1, "Audio", "ac3", "eng", "English", null, null, null, 640_000, 48000, 6, true, false)
                ])),
            new TranscodeSessionService());

        var info = await service.CreatePlanAsync(videoId, new VideoPlaybackPlanQuery {
            EnableDirectPlay = true,
            EnableDirectStream = true,
            EnableTranscoding = true,
            Profile = new VideoPlaybackProfile(
                200_000_000,
                [new VideoDirectPlayProfile(StreamKind.Video.ToCode(), "mp4", "h264", "ac3")])
        }, CancellationToken.None);

        Assert.NotNull(info);
        var source = info.Source;
        Assert.StartsWith(
            VideoPlaybackProtocol.HlsPath(videoId, $"v/remux/{VideoPlaybackProtocol.Hls.StreamPlaylist}"),
            source.Url);
        Assert.Contains($"{VideoPlaybackProtocol.CopyAudioQuery}=true", source.Url);
        Assert.Equal("ac3", source.Transcoding?.AudioCodec);
        Assert.True(source.Transcoding?.IsVideoDirect);
        Assert.True(source.Transcoding?.IsAudioDirect);
    }

    [Fact]
    public async Task VideoPlaybackPlanDisablesDirectPlayForHdrUnlessClientAdvertisesRangeSupport() {
        var videoId = Guid.Parse("45454545-4545-4545-4545-454545454545");
        var service = new VideoPlaybackPlanService(
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                "/media/movie.mp4",
                "video/mp4",
                true,
                DurationSeconds: 60,
                Width: 3840,
                Height: 2160,
                Streams:
                [
                    new(0, "Video", "hevc", null, "Video", 3840, 2160, 24, null, null, null, true, false) {
                        ColorTransfer = "smpte2084",
                        ColorPrimaries = "bt2020",
                        ColorSpace = "bt2020nc"
                    }
                ])),
            new TranscodeSessionService());

        var defaultInfo = await service.CreatePlanAsync(videoId, new VideoPlaybackPlanQuery {
            EnableDirectPlay = true,
            EnableTranscoding = true
        }, CancellationToken.None);

        var hdrInfo = await service.CreatePlanAsync(videoId, new VideoPlaybackPlanQuery {
            EnableDirectPlay = true,
            EnableTranscoding = true,
            SupportedVideoRangeTypes = ["HDR10"]
        }, CancellationToken.None);

        Assert.NotNull(defaultInfo);
        var defaultSource = defaultInfo.Source;
        Assert.Equal(VideoPlaybackMethod.Transcode, defaultSource.Method);
        Assert.StartsWith(
            VideoPlaybackProtocol.HlsPath(videoId, VideoPlaybackProtocol.Hls.MasterPlaylist),
            defaultSource.Url);
        Assert.Equal("HDR", Assert.Single(defaultSource.Streams).VideoRange);
        Assert.Equal("HDR10", Assert.Single(defaultSource.Streams).VideoRangeType);

        Assert.NotNull(hdrInfo);
        var hdrSource = hdrInfo.Source;
        Assert.Equal(VideoPlaybackMethod.Direct, hdrSource.Method);
        Assert.StartsWith(VideoPlaybackProtocol.SourcePath(videoId), hdrSource.Url);
    }

    [Fact]
    public async Task VideoPlaybackPlanAddsAccessTokenForHeaderlessMediaRequests() {
        var videoId = Guid.Parse("56565656-5656-5656-5656-565656565656");
        var service = new VideoPlaybackPlanService(
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                "/media/movie.mkv",
                "video/x-matroska",
                false,
                DurationSeconds: 60,
                Width: 1920,
                Height: 1080,
                Streams:
                [
                    new(0, "Video", "h264", null, "Video", 1920, 1080, 24, null, null, null, true, false)
                ])),
            new TranscodeSessionService());

        var info = await service.CreatePlanAsync(videoId, new VideoPlaybackPlanQuery {
            EnableDirectPlay = true,
            EnableTranscoding = true,
            AccessToken = "session/token+value"
        }, CancellationToken.None);

        Assert.NotNull(info);
        var source = info.Source;
        Assert.Contains($"{ApiAuthenticationProtocol.AccessTokenQuery}=session%2Ftoken%2Bvalue", source.Url);
    }

    [Fact]
    public async Task VideoPlaybackPlanDoesNotAdvertiseHlsWhenSourceDurationIsMissing() {
        var videoId = Guid.Parse("89898989-8989-8989-8989-898989898989");
        var service = new VideoPlaybackPlanService(
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                "/media/new-import.mkv",
                "video/x-matroska",
                false,
                DurationSeconds: null,
                Width: 1920,
                Height: 1080,
                Container: "matroska",
                VideoCodec: "h264",
                AudioCodec: "aac",
                Streams:
                [
                    new(0, "Video", "h264", null, "Video", 1920, 1080, 24, null, null, null, true, false),
                    new(1, "Audio", "aac", "eng", "English", null, null, null, null, 48000, 2, true, false)
                ])),
            new TranscodeSessionService());

        var info = await service.CreatePlanAsync(videoId, new VideoPlaybackPlanQuery {
            EnableDirectPlay = true,
            EnableDirectStream = true,
            EnableTranscoding = true
        }, CancellationToken.None);

        Assert.Null(info);
    }

    [Fact]
    public async Task VideoPlaybackPlanDirectPlaysMkvWhenClientProfileDeclaresCodecSupport() {
        var videoId = Guid.Parse("67676767-6767-6767-6767-676767676767");
        var service = new VideoPlaybackPlanService(
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                "/media/dolby-vision.mkv",
                "video/x-matroska",
                // Extension heuristic says "not directly playable"; the device profile overrides it.
                DirectPlayable: false,
                DurationSeconds: 60,
                Width: 3840,
                Height: 2160,
                Container: "matroska",
                VideoCodec: "hevc",
                AudioCodec: "eac3",
                Streams:
                [
                    new(0, "Video", "hevc", null, "Video", 3840, 2160, 24, null, null, null, true, false) {
                        DvProfile = 8,
                        RpuPresentFlag = true,
                        ColorTransfer = "smpte2084",
                        ColorPrimaries = "bt2020"
                    },
                    new(1, "Audio", "eac3", "eng", "English", null, null, null, null, 48000, 6, true, false)
                ])),
            new TranscodeSessionService());

        var info = await service.CreatePlanAsync(videoId, new VideoPlaybackPlanQuery {
            EnableDirectPlay = true,
            EnableDirectStream = true,
            EnableTranscoding = true,
            SupportedVideoRangeTypes = ["DOVI", "DOVIWithHDR10", "HDR10"],
            Profile = new VideoPlaybackProfile(
                200_000_000,
                [new VideoDirectPlayProfile(StreamKind.Video.ToCode(), "mkv,mp4,ts", "hevc,h264", "eac3,ac3,aac")])
        }, CancellationToken.None);

        Assert.NotNull(info);
        var source = info.Source;
        Assert.Equal(VideoPlaybackMethod.Direct, source.Method);
        Assert.StartsWith(VideoPlaybackProtocol.SourcePath(videoId), source.Url);
        Assert.Null(source.Transcoding);
    }

    [Fact]
    public async Task VideoPlaybackPlanDirectPlaysMalformedHdrForClientToneMappingRenderer() {
        var videoId = Guid.Parse("68686868-6868-6868-6868-686868686868");
        var service = new VideoPlaybackPlanService(
            new FakeVideoSourceService(new VideoSourceFile(
                videoId,
                "/media/malformed-hdr.mkv",
                "video/x-matroska",
                DirectPlayable: false,
                DurationSeconds: 60,
                Width: 1920,
                Height: 1080,
                Container: "matroska",
                VideoCodec: "hevc",
                AudioCodec: "eac3",
                Streams:
                [
                    new(0, "Video", "hevc", null, "Video", 1920, 1080, 24, null, null, null, true, false) {
                        BitDepth = 8,
                        ColorTransfer = "smpte2084",
                        ColorPrimaries = "bt2020"
                    },
                    new(1, "Audio", "eac3", "eng", "English", null, null, null, null, 48000, 6, true, false)
                ])),
            new TranscodeSessionService());

        var info = await service.CreatePlanAsync(videoId, new VideoPlaybackPlanQuery {
            EnableDirectPlay = true,
            EnableDirectStream = true,
            EnableTranscoding = true,
            EnableClientToneMapping = true,
            SupportedVideoRangeTypes = ["HDR10"],
            Profile = new VideoPlaybackProfile(
                200_000_000,
                [new VideoDirectPlayProfile(StreamKind.Video.ToCode(), "mp4", "hevc", "eac3,ac3,aac")])
        }, CancellationToken.None);

        Assert.NotNull(info);
        var source = info.Source;
        Assert.Equal(VideoPlaybackMethod.Direct, source.Method);
        Assert.StartsWith(VideoPlaybackProtocol.SourcePath(videoId), source.Url);
        Assert.Null(source.Transcoding);
    }

    private sealed class FakeVideoSourceService : IVideoSourceService {
        private readonly VideoSourceFile _source;

        public FakeVideoSourceService(VideoSourceFile source) {
            _source = source;
        }

        public Task<VideoSourceFile?> GetSourceAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(id == _source.EntityId ? _source : null);
    }

    private sealed class FakeSettingsPersistence : ISettingsPersistence {
        private readonly string[] _audioPreferredLanguages;

        public FakeSettingsPersistence(string audioPreferredLanguages) {
            _audioPreferredLanguages = audioPreferredLanguages.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        public Task<IReadOnlyDictionary<string, string>> LoadSettingOverridesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string> {
                    [AppSettings.Playback.AudioPreferredLanguages.Key] =
                        JsonSerializer.Serialize(_audioPreferredLanguages),
                });

        public Task SaveSettingOverrideAsync(string key, string valueJson, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveSettingOverridesAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task ReplaceSettingOverridesAsync(
            IReadOnlyDictionary<string, string> upserts,
            IReadOnlyCollection<string> deletes,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteSettingOverrideAsync(string key, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<LibraryRoot>> ListLibraryRootsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LibraryRoot>>([]);

        public Task<LibraryRoot?> GetLibraryRootAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<LibraryRoot?>(null);

        public Task<LibraryRoot> AddLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<LibraryRoot> SaveLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> DeleteLibraryRootAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

    }
}
