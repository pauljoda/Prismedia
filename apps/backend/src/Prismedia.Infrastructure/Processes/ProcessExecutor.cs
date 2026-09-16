using System.Diagnostics;
using System.Text;

namespace Prismedia.Infrastructure.Processes;

/// <summary>
/// Runs external command-line processes and captures their standard output and error streams.
/// </summary>
public class ProcessExecutor(IMediaProcessAdmission? mediaAdmission = null) {
    /// <summary>
    /// Starts a process with explicit arguments and environment overrides.
    /// </summary>
    /// <param name="fileName">Executable name or absolute path.</param>
    /// <param name="arguments">Arguments passed without shell interpolation.</param>
    /// <param name="environment">Optional environment variables to set for the process.</param>
    /// <param name="cancellationToken">Token used to cancel process execution.</param>
    /// <param name="lowPriority">
    /// When true, runs the process at below-normal scheduling priority so background
    /// media generation (thumbnails, trickplay) never starves playback, the API, or scans.
    /// </param>
    /// <returns>Exit code plus captured standard output and standard error.</returns>
    public virtual Task<ProcessExecutionResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
        CancellationToken cancellationToken,
        bool lowPriority = false) =>
        RunCapturedAsync(fileName, arguments, environment, cancellationToken, new ProcessExecutionOptions(), lowPriority);

    /// <summary>Runs a child with explicit output budgets and environment inheritance policy.</summary>
    /// <param name="fileName">Executable name or absolute path.</param>
    /// <param name="arguments">Arguments passed without shell interpolation.</param>
    /// <param name="environment">Explicit environment values; the complete environment when inheritance is disabled.</param>
    /// <param name="cancellationToken">Cancellation terminates the process tree.</param>
    /// <param name="options">Capture and environment limits for this invocation.</param>
    /// <param name="lowPriority">Whether background work uses below-normal scheduling priority.</param>
    /// <returns>The exit code and complete output, or an exception if a capture limit is exceeded.</returns>
    public virtual Task<ProcessExecutionResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
        CancellationToken cancellationToken,
        ProcessExecutionOptions options,
        bool lowPriority = false) =>
        RunCapturedAsync(fileName, arguments, environment, cancellationToken, options, lowPriority);

    private async Task<ProcessExecutionResult> RunCapturedAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
        CancellationToken cancellationToken,
        ProcessExecutionOptions options,
        bool lowPriority) {
        if (options.MaxStandardOutputCharacters is < 0 || options.MaxStandardErrorCharacters is < 0) {
            throw new ArgumentOutOfRangeException(nameof(options), "Capture limits must be non-negative.");
        }
        await using var mediaLease = await AcquireMediaLeaseAsync(fileName, lowPriority, cancellationToken);
        var startInfo = new ProcessStartInfo(fileName) {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments) {
            startInfo.ArgumentList.Add(argument);
        }

        if (!options.InheritEnvironment) startInfo.Environment.Clear();
        foreach (var (key, value) in environment ?? new Dictionary<string, string>()) {
            startInfo.Environment[key] = value;
        }

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException($"Failed to start '{fileName}'.");
        TryApplyLowPriority(process, lowPriority);
        using var captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stdoutTask = CaptureAsync(process.StandardOutput, options.MaxStandardOutputCharacters, captureCancellation.Token);
        var stderrTask = CaptureAsync(process.StandardError, options.MaxStandardErrorCharacters, captureCancellation.Token);
        try {
            // Observe either stream's failure immediately: waiting for exit or WhenAll first
            // would deadlock a child whose overflowing pipe is no longer being drained.
            var pending = new List<Task> { stdoutTask, stderrTask, process.WaitForExitAsync(captureCancellation.Token) };
            while (pending.Count > 0) {
                var completed = await Task.WhenAny(pending);
                await completed;
                pending.Remove(completed);
            }

            return new ProcessExecutionResult(
                process.ExitCode,
                await stdoutTask,
                await stderrTask);
        } catch {
            await captureCancellation.CancelAsync();
            if (!process.HasExited) {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* The child exited between the check and kill. */ }
            }
            await process.WaitForExitAsync(CancellationToken.None);
            try { await Task.WhenAll(stdoutTask, stderrTask); }
            catch { /* Preserve the original process/capture failure. */ }
            throw;
        }
    }

    private static async Task<string> CaptureAsync(StreamReader reader, int? maximumCharacters, CancellationToken cancellationToken) {
        if (maximumCharacters is null) return await reader.ReadToEndAsync(cancellationToken);
        var output = new StringBuilder(Math.Min(maximumCharacters.Value, 4096));
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0) {
            if (read > maximumCharacters.Value - output.Length) throw new ProcessOutputLimitException();
            output.Append(buffer, 0, read);
        }
        return output.ToString();
    }

    /// <summary>
    /// Starts a process, writes a payload to its standard input, and captures its output.
    /// Used by scrapers that follow the Stash stdin/stdout JSON protocol.
    /// </summary>
    /// <param name="fileName">Executable name or absolute path.</param>
    /// <param name="arguments">Arguments passed without shell interpolation.</param>
    /// <param name="standardInput">Text written to the process standard input, then closed.</param>
    /// <param name="environment">Optional environment variables to set for the process.</param>
    /// <param name="workingDirectory">Optional working directory for the process.</param>
    /// <param name="cancellationToken">Token used to cancel process execution.</param>
    /// <returns>Exit code plus captured standard output and standard error.</returns>
    public virtual async Task<ProcessExecutionResult> RunWithStdinAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string standardInput,
        IReadOnlyDictionary<string, string>? environment,
        string? workingDirectory,
        CancellationToken cancellationToken) {
        var startInfo = new ProcessStartInfo(fileName) {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory)) {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var argument in arguments) {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (key, value) in environment ?? new Dictionary<string, string>()) {
            startInfo.Environment[key] = value;
        }

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException($"Failed to start '{fileName}'.");

        try {
            await process.StandardInput.WriteAsync(standardInput);
            process.StandardInput.Close();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return new ProcessExecutionResult(
                process.ExitCode,
                await stdoutTask,
                await stderrTask);
        } catch (OperationCanceledException) when (!process.HasExited) {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Starts a process and streams standard output directly to a file.
    /// </summary>
    /// <param name="fileName">Executable name or absolute path.</param>
    /// <param name="arguments">Arguments passed without shell interpolation.</param>
    /// <param name="environment">Optional environment variables to set for the process.</param>
    /// <param name="outputPath">File that receives standard output.</param>
    /// <param name="cancellationToken">Token used to cancel process execution.</param>
    /// <param name="lowPriority">
    /// When true, runs the process at below-normal scheduling priority so background
    /// media generation never starves playback, the API, or scans.
    /// </param>
    /// <returns>Exit code plus captured standard error.</returns>
    public virtual async Task<ProcessExecutionResult> RunToFileAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
        string outputPath,
        CancellationToken cancellationToken,
        bool lowPriority = false) {
        await using var mediaLease = await AcquireMediaLeaseAsync(fileName, lowPriority, cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var startInfo = new ProcessStartInfo(fileName) {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments) {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (key, value) in environment ?? new Dictionary<string, string>()) {
            startInfo.Environment[key] = value;
        }

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException($"Failed to start '{fileName}'.");
        TryApplyLowPriority(process, lowPriority);
        await using var output = File.Create(outputPath);

        try {
            var copyTask = process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await copyTask;

            return new ProcessExecutionResult(
                ExitCode: process.ExitCode,
                StandardOutput: string.Empty,
                StandardError: await stderrTask);
        } catch (OperationCanceledException) when (!process.HasExited) {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Starts a process and streams standard output directly to a caller-owned stream,
    /// e.g. an HTTP response body for live transcode delivery.
    /// </summary>
    /// <param name="fileName">Executable name or absolute path.</param>
    /// <param name="arguments">Arguments passed without shell interpolation.</param>
    /// <param name="environment">Optional environment variables to set for the process.</param>
    /// <param name="output">Destination stream that receives standard output; not disposed.</param>
    /// <param name="cancellationToken">Token used to cancel process execution.</param>
    /// <returns>Exit code plus captured standard error.</returns>
    public virtual async Task<ProcessExecutionResult> RunToStreamAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
        Stream output,
        CancellationToken cancellationToken) {
        await using var mediaLease = await AcquireMediaLeaseAsync(
            fileName,
            lowPriority: false,
            cancellationToken);
        var startInfo = new ProcessStartInfo(fileName) {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments) {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (key, value) in environment ?? new Dictionary<string, string>()) {
            startInfo.Environment[key] = value;
        }

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException($"Failed to start '{fileName}'.");

        try {
            var copyTask = process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await copyTask;

            return new ProcessExecutionResult(
                ExitCode: process.ExitCode,
                StandardOutput: string.Empty,
                StandardError: await stderrTask);
        } catch (OperationCanceledException) when (!process.HasExited) {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Best-effort downgrade of a freshly started process to below-normal scheduling
    /// priority. On Linux this maps to a positive nice value. Failures are ignored:
    /// the process may have already exited or the platform may forbid the change, in
    /// which case generation simply proceeds at normal priority.
    /// </summary>
    private static void TryApplyLowPriority(Process process, bool lowPriority) {
        if (!lowPriority) {
            return;
        }

        try {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        } catch {
            // Intentionally ignored — priority is an optimization, not a correctness requirement.
        }
    }

    private ValueTask<IAsyncDisposable> AcquireMediaLeaseAsync(
        string fileName,
        bool lowPriority,
        CancellationToken cancellationToken) {
        if (mediaAdmission is null || !IsFfmpegExecutable(fileName)) {
            return ValueTask.FromResult<IAsyncDisposable>(EmptyAsyncLease.Instance);
        }

        return lowPriority
            ? mediaAdmission.AcquireBackgroundAsync(cancellationToken)
            : mediaAdmission.RegisterPlaybackAsync(cancellationToken);
    }

    private static bool IsFfmpegExecutable(string fileName) =>
        Path.GetFileNameWithoutExtension(fileName)
            .Equals("ffmpeg", StringComparison.OrdinalIgnoreCase);

    private sealed class EmptyAsyncLease : IAsyncDisposable {
        internal static EmptyAsyncLease Instance { get; } = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
