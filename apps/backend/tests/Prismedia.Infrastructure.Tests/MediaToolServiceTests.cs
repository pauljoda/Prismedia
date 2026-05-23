using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Tests;

public sealed class MediaToolServiceTests {
    [Fact]
    public void ToolOptionsPreferJellyfinFfmpegWhenNoPathIsConfigured() {
        var options = MediaToolOptions.FromConfiguration(null, null, name =>
            name == "jellyfin-ffmpeg" ? "jellyfin-ffmpeg" : null);

        Assert.Equal("jellyfin-ffmpeg", options.FfmpegPath);
        Assert.Equal("jellyfin-ffprobe", options.FfprobePath);
    }

    [Fact]
    public void ToolOptionsKeepConfiguredFfmpegPath() {
        var options = MediaToolOptions.FromConfiguration("/opt/media/ffmpeg", null, _ => "jellyfin-ffmpeg");

        Assert.Equal("/opt/media/ffmpeg", options.FfmpegPath);
        Assert.Equal("/opt/media/ffprobe", options.FfprobePath);
    }

    [Fact]
    public void ToolOptionsResolveCompanionProbeForNamedFfmpegAliases() {
        Assert.Equal(
            "/Users/pauldavis/.local/bin/jellyfin-ffprobe",
            MediaToolOptions.ResolveFfprobePath("/Users/pauldavis/.local/bin/jellyfin-ffmpeg"));
        Assert.Equal("ffprobe7", MediaToolOptions.ResolveFfprobePath("ffmpeg7"));
    }

    [Fact]
    public async Task CheckReportsAvailableFfmpegAndFfprobeVersions() {
        var service = new MediaToolService(new FakeProcessExecutor(new Dictionary<string, ProcessExecutionResult> {
            ["ffmpeg"] = new(0, "ffmpeg version 7.1\nbuilt with clang", ""),
            ["ffprobe"] = new(0, "ffprobe version 7.1\nbuilt with clang", "")
        }));

        var status = await service.CheckAsync(CancellationToken.None);

        Assert.True(status.FfmpegAvailable);
        Assert.True(status.FfprobeAvailable);
        Assert.Equal("ffmpeg version 7.1", status.FfmpegVersion);
        Assert.Equal("ffprobe version 7.1", status.FfprobeVersion);
    }

    [Fact]
    public async Task CheckUsesConfiguredFfmpegAndCompanionFfprobePaths() {
        var process = new FakeProcessExecutor(new Dictionary<string, ProcessExecutionResult> {
            ["/usr/lib/jellyfin-ffmpeg/ffmpeg"] = new(0, "ffmpeg version 7.1\nbuilt with clang", ""),
            ["/usr/lib/jellyfin-ffmpeg/ffprobe"] = new(0, "ffprobe version 7.1\nbuilt with clang", "")
        });
        var service = new MediaToolService(
            process,
            new MediaToolOptions("/usr/lib/jellyfin-ffmpeg/ffmpeg"));

        await service.CheckAsync(CancellationToken.None);

        Assert.Equal(
            ["/usr/lib/jellyfin-ffmpeg/ffmpeg", "/usr/lib/jellyfin-ffmpeg/ffprobe"],
            process.FileNames);
    }

    [Fact]
    public async Task CheckHandlesMissingTools() {
        var service = new MediaToolService(new ThrowingProcessExecutor());

        var status = await service.CheckAsync(CancellationToken.None);

        Assert.False(status.FfmpegAvailable);
        Assert.False(status.FfprobeAvailable);
        Assert.Null(status.FfmpegVersion);
        Assert.Null(status.FfprobeVersion);
    }

    private sealed class FakeProcessExecutor : ProcessExecutor {
        private readonly IReadOnlyDictionary<string, ProcessExecutionResult> _results;
        public List<string> FileNames { get; } = [];

        public FakeProcessExecutor(IReadOnlyDictionary<string, ProcessExecutionResult> results) {
            _results = results;
        }

        public override Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken) {
            FileNames.Add(fileName);
            return Task.FromResult(_results[fileName]);
        }
    }

    private sealed class ThrowingProcessExecutor : ProcessExecutor {
        public override Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken) {
            throw new FileNotFoundException(fileName);
        }
    }
}
