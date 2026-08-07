using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Tests;

public sealed class ProcessExecutorAdmissionTests : IDisposable {
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        $"prismedia-process-admission-{Guid.NewGuid():N}");

    [Fact]
    public async Task NormalPriorityFfmpegUsesUnboundedPlaybackRegistration() {
        var admission = new RecordingMediaProcessAdmission();
        var executor = new ProcessExecutor(admission);

        var result = await executor.RunAsync(
            CreateNoOpFfmpeg(),
            [],
            environment: null,
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, admission.PlaybackRegistrations);
        Assert.Equal(0, admission.BackgroundAcquisitions);
        Assert.Equal(1, admission.Releases);
    }

    [Fact]
    public async Task LowPriorityFfmpegUsesBackgroundAdmission() {
        var admission = new RecordingMediaProcessAdmission();
        var executor = new ProcessExecutor(admission);

        var result = await executor.RunAsync(
            CreateNoOpFfmpeg(),
            [],
            environment: null,
            CancellationToken.None,
            lowPriority: true);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, admission.PlaybackRegistrations);
        Assert.Equal(1, admission.BackgroundAcquisitions);
        Assert.Equal(1, admission.Releases);
    }

    public void Dispose() {
        if (Directory.Exists(_tempRoot)) {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private string CreateNoOpFfmpeg() {
        Directory.CreateDirectory(_tempRoot);
        var path = Path.Combine(_tempRoot, "ffmpeg");
        File.Copy("/usr/bin/true", path, overwrite: true);
        if (!OperatingSystem.IsWindows()) {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        return path;
    }

    private sealed class RecordingMediaProcessAdmission : IMediaProcessAdmission {
        public int PlaybackRegistrations { get; private set; }
        public int BackgroundAcquisitions { get; private set; }
        public int Releases { get; private set; }

        public ValueTask<IAsyncDisposable> RegisterPlaybackAsync(CancellationToken cancellationToken) {
            PlaybackRegistrations++;
            return ValueTask.FromResult<IAsyncDisposable>(new RecordingLease(this));
        }

        public ValueTask<IAsyncDisposable> AcquireBackgroundAsync(CancellationToken cancellationToken) {
            BackgroundAcquisitions++;
            return ValueTask.FromResult<IAsyncDisposable>(new RecordingLease(this));
        }

        private sealed class RecordingLease(RecordingMediaProcessAdmission owner) : IAsyncDisposable {
            public ValueTask DisposeAsync() {
                owner.Releases++;
                return ValueTask.CompletedTask;
            }
        }
    }
}
