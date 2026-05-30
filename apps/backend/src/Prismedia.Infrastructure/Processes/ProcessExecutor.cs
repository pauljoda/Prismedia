using System.Diagnostics;

namespace Prismedia.Infrastructure.Processes;

/// <summary>
/// Runs external command-line processes and captures their standard output and error streams.
/// </summary>
public class ProcessExecutor {
    /// <summary>
    /// Starts a process with explicit arguments and environment overrides.
    /// </summary>
    /// <param name="fileName">Executable name or absolute path.</param>
    /// <param name="arguments">Arguments passed without shell interpolation.</param>
    /// <param name="environment">Optional environment variables to set for the process.</param>
    /// <param name="cancellationToken">Token used to cancel process execution.</param>
    /// <returns>Exit code plus captured standard output and standard error.</returns>
    public virtual async Task<ProcessExecutionResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
        CancellationToken cancellationToken) {
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
    /// <returns>Exit code plus captured standard error.</returns>
    public virtual async Task<ProcessExecutionResult> RunToFileAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
        string outputPath,
        CancellationToken cancellationToken) {
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
}
