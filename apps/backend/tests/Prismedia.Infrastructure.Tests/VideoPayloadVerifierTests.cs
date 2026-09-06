using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Tests;

public sealed class VideoPayloadVerifierTests : IDisposable {
    private readonly string path = Path.GetTempFileName();

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task OnlySuccessfulCompleteDecodingPasses(int exitCode, bool passes) {
        await File.WriteAllTextAsync(path, "downloaded payload");
        var executor = new Decoder(exitCode);
        var verifier = new VideoPayloadVerifier(executor, new MediaToolOptions(), NullLogger<VideoPayloadVerifier>.Instance);

        Assert.Equal(passes, await verifier.FindFailureAsync(path, default) is null);
        Assert.True(executor.LowPriority);
        Assert.Contains(path, executor.Arguments);
        Assert.DoesNotContain("-t", executor.Arguments);
        Assert.DoesNotContain("-ss", executor.Arguments);
    }

    [Fact]
    public async Task PayloadChangedDuringDecodingCannotPass() {
        await File.WriteAllTextAsync(path, "downloaded payload");
        var executor = new Decoder(0, () => File.AppendAllText(path, "changed"));
        var verifier = new VideoPayloadVerifier(executor, new MediaToolOptions(), NullLogger<VideoPayloadVerifier>.Instance);
        Assert.NotNull(await verifier.FindFailureAsync(path, default));
    }

    [Fact]
    public async Task CancellationDoesNotBecomeAnInvalidMediaVerdict() {
        await File.WriteAllTextAsync(path, "downloaded payload");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var verifier = new VideoPayloadVerifier(new Decoder(0), new MediaToolOptions(), NullLogger<VideoPayloadVerifier>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => verifier.FindFailureAsync(path, cancellation.Token));
    }

    [Fact]
    public async Task MissingDecoderRequiresReviewInsteadOfPassingOrDeletingThePayload() {
        await File.WriteAllTextAsync(path, "downloaded payload");
        var executor = new Decoder(0, () => throw new System.ComponentModel.Win32Exception("Decoder unavailable"));
        var verifier = new VideoPayloadVerifier(executor, new MediaToolOptions(), NullLogger<VideoPayloadVerifier>.Instance);
        Assert.NotNull(await verifier.FindFailureAsync(path, default));
        Assert.Equal("downloaded payload", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task EmptyPayloadDoesNotStartADecoder() {
        var executor = new Decoder(0);
        var verifier = new VideoPayloadVerifier(executor, new MediaToolOptions(), NullLogger<VideoPayloadVerifier>.Instance);
        Assert.NotNull(await verifier.FindFailureAsync(path, default));
        Assert.Empty(executor.Arguments);
    }

    public void Dispose() => File.Delete(path);

    private sealed class Decoder(int exitCode, Action? duringDecode = null) : ProcessExecutor {
        public bool LowPriority { get; private set; }
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public override Task<ProcessExecutionResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment, CancellationToken cancellationToken, bool lowPriority = false) {
            cancellationToken.ThrowIfCancellationRequested();
            Arguments = arguments;
            LowPriority = lowPriority;
            duringDecode?.Invoke();
            return Task.FromResult(new ProcessExecutionResult(exitCode, string.Empty, "decoder output"));
        }
    }
}
