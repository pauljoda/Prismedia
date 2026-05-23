using Prismedia.Application.Videos;
using Prismedia.Infrastructure.Videos;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Settings;

namespace Prismedia.Infrastructure.Tests;

public sealed class PlaybackInfoServiceTests {
    [Fact]
    public async Task PlaybackInfoExposesAllAudioStreamsAndSelectsRequestedTrack() {
        var videoId = Guid.Parse("12121212-1212-1212-1212-121212121212");
        var service = new PlaybackInfoService(
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

        var info = await service.GetPlaybackInfoAsync(videoId, new PlaybackInfoQuery {
            AudioStreamIndex = 1,
            EnableDirectPlay = true,
            EnableDirectStream = true,
            EnableTranscoding = true
        }, CancellationToken.None);

        Assert.NotNull(info);
        var source = Assert.Single(info.MediaSources);
        Assert.StartsWith($"/Videos/{videoId:D}/master.m3u8", source.TranscodingUrl);
        Assert.Contains("AudioStreamIndex=1", source.TranscodingUrl);
        var audioStreams = source.MediaStreams.Where(stream => stream.Type == "Audio").ToList();
        Assert.Equal(2, audioStreams.Count);
        Assert.True(audioStreams.Single(stream => stream.Index == 1).IsDefault);
        Assert.False(audioStreams.Single(stream => stream.Index == 2).IsDefault);
    }

    [Fact]
    public async Task PlaybackInfoSelectsPreferredAudioLanguageBeforeContainerDefault() {
        var videoId = Guid.Parse("34343434-3434-3434-3434-343434343434");
        var service = new PlaybackInfoService(
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

        var info = await service.GetPlaybackInfoAsync(videoId, new PlaybackInfoQuery {
            EnableDirectPlay = true,
            EnableDirectStream = true,
            EnableTranscoding = true
        }, CancellationToken.None);

        Assert.NotNull(info);
        var source = Assert.Single(info.MediaSources);
        Assert.Contains("AudioStreamIndex=2", source.TranscodingUrl);
        var audioStreams = source.MediaStreams.Where(stream => stream.Type == "Audio").ToList();
        Assert.False(audioStreams.Single(stream => stream.Index == 1).IsDefault);
        Assert.True(audioStreams.Single(stream => stream.Index == 2).IsDefault);
    }

    [Fact]
    public async Task PlaybackInfoDisablesDirectPlayForHdrUnlessClientAdvertisesRangeSupport() {
        var videoId = Guid.Parse("45454545-4545-4545-4545-454545454545");
        var service = new PlaybackInfoService(
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

        var defaultInfo = await service.GetPlaybackInfoAsync(videoId, new PlaybackInfoQuery {
            EnableDirectPlay = true,
            EnableTranscoding = true
        }, CancellationToken.None);

        var hdrInfo = await service.GetPlaybackInfoAsync(videoId, new PlaybackInfoQuery {
            EnableDirectPlay = true,
            EnableTranscoding = true,
            SupportedVideoRangeTypes = ["HDR10"]
        }, CancellationToken.None);

        Assert.NotNull(defaultInfo);
        var defaultSource = Assert.Single(defaultInfo.MediaSources);
        Assert.False(defaultSource.SupportsDirectPlay);
        Assert.StartsWith($"/Videos/{videoId:D}/master.m3u8", defaultSource.TranscodingUrl);
        Assert.Equal("HDR", Assert.Single(defaultSource.MediaStreams).VideoRange);
        Assert.Equal("HDR10", Assert.Single(defaultSource.MediaStreams).VideoRangeType);

        Assert.NotNull(hdrInfo);
        var hdrSource = Assert.Single(hdrInfo.MediaSources);
        Assert.True(hdrSource.SupportsDirectPlay);
        Assert.Null(hdrSource.TranscodingUrl);
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
        private readonly string _audioPreferredLanguages;

        public FakeSettingsPersistence(string audioPreferredLanguages) {
            _audioPreferredLanguages = audioPreferredLanguages;
        }

        public Task<LibrarySettings> GetLibrarySettingsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(SampleSettings());

        public Task<LibrarySettings> SaveLibrarySettingsAsync(LibrarySettings state, CancellationToken cancellationToken) =>
            Task.FromResult(state);

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

        private LibrarySettings SampleSettings() =>
            new(
                Guid.Parse("56565656-5656-5656-5656-565656565656"),
                false,
                60,
                true,
                true,
                false,
                true,
                true,
                10,
                8,
                2,
                2,
                1,
                false,
                true,
                false,
                "en,eng",
                _audioPreferredLanguages,
                "stylized",
                1,
                88,
                1,
                "direct",
                true,
                "Software",
                "ffmpeg",
                "/dev/dri/renderD128",
                false,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch);
    }
}
