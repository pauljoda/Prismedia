using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Tests;

public sealed class MediaProbeServiceTests {
    [Fact]
    public async Task ProbeVideoCapturesHdrAndDolbyVisionStreamMetadata() {
        var process = new JsonProcessExecutor("""
            {
              "format": { "duration": "42", "size": "123456", "bit_rate": "8000000", "format_name": "matroska" },
              "streams": [
                {
                  "index": 0,
                  "codec_type": "video",
                  "codec_name": "hevc",
                  "pix_fmt": "yuv420p10le",
                  "width": 3840,
                  "height": 2160,
                  "avg_frame_rate": "24000/1001",
                  "bit_rate": "7000000",
                  "color_range": "tv",
                  "color_space": "bt2020nc",
                  "color_transfer": "smpte2084",
                  "color_primaries": "bt2020",
                  "side_data_list": [
                    {
                      "side_data_type": "DOVI configuration record",
                      "dv_profile": 8,
                      "dv_level": 6,
                      "rpu_present_flag": 1,
                      "el_present_flag": 0,
                      "bl_present_flag": 1,
                      "dv_bl_signal_compatibility_id": 1
                    },
                    {
                      "side_data_type": "HDR Dynamic Metadata SMPTE2094-40 (HDR10+)"
                    }
                  ],
                  "disposition": { "default": 1, "forced": 0 }
                }
              ]
            }
            """);
        var service = new MediaProbeService(process);

        var result = await service.ProbeVideoAsync("/media/movie.mkv", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("stream_side_data=side_data_type,dv_profile", string.Join(' ', process.LastArguments));
        var video = Assert.Single(result.Streams!);
        Assert.Equal("yuv420p10le", video.PixelFormat);
        Assert.Equal(10, video.BitDepth);
        Assert.Equal("bt2020nc", video.ColorSpace);
        Assert.Equal("smpte2084", video.ColorTransfer);
        Assert.Equal("bt2020", video.ColorPrimaries);
        Assert.Equal(8, video.DvProfile);
        Assert.Equal(6, video.DvLevel);
        Assert.True(video.RpuPresentFlag);
        Assert.False(video.ElPresentFlag);
        Assert.True(video.BlPresentFlag);
        Assert.Equal(1, video.DvBlSignalCompatibilityId);
        Assert.True(video.Hdr10PlusPresentFlag);
    }

    [Fact]
    public async Task ProbeVideoUsesConfiguredCompanionFfprobePath() {
        var process = new JsonProcessExecutor("""
            {
              "format": { "duration": "42", "format_name": "matroska" },
              "streams": []
            }
            """);
        var service = new MediaProbeService(process, new MediaToolOptions("/usr/lib/jellyfin-ffmpeg/ffmpeg"));

        await service.ProbeVideoAsync("/media/movie.mkv", CancellationToken.None);

        Assert.Equal("/usr/lib/jellyfin-ffmpeg/ffprobe", process.LastFileName);
    }

    [Fact]
    public async Task ProbeVideoClassifiesHlgWithoutDolbyVisionSideData() {
        var service = new MediaProbeService(new JsonProcessExecutor("""
            {
              "format": { "duration": "42", "format_name": "matroska" },
              "streams": [
                {
                  "index": 0,
                  "codec_type": "video",
                  "codec_name": "hevc",
                  "pix_fmt": "yuv420p10le",
                  "width": 1920,
                  "height": 1080,
                  "avg_frame_rate": "24/1",
                  "color_space": "bt2020nc",
                  "color_transfer": "arib-std-b67",
                  "color_primaries": "bt2020",
                  "disposition": { "default": 1, "forced": 0 }
                }
              ]
            }
            """));

        var result = await service.ProbeVideoAsync("/media/hlg.mkv", CancellationToken.None);

        Assert.NotNull(result);
        var video = Assert.Single(result.Streams!);
        Assert.Equal("arib-std-b67", video.ColorTransfer);
        Assert.Equal("bt2020", video.ColorPrimaries);
        Assert.Null(video.DvProfile);
        Assert.False(video.Hdr10PlusPresentFlag);
    }

    private sealed class JsonProcessExecutor : ProcessExecutor {
        private readonly string _json;

        public JsonProcessExecutor(string json) {
            _json = json;
        }

        public IReadOnlyList<string> LastArguments { get; private set; } = [];
        public string? LastFileName { get; private set; }

        public override Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken) {
            LastFileName = fileName;
            LastArguments = arguments;
            return Task.FromResult(new ProcessExecutionResult(0, _json, string.Empty));
        }
    }
}
