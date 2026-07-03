using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Settings;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// The opt-in recycle bin for files Prismedia replaces (quality upgrades). When a bin folder is
/// configured, a replaced file moves into a dated subfolder there — recoverable until the daily
/// cleanup purges entries older than the configured window. With no bin configured callers keep
/// their fallback behavior (the <c>.prismedia-bak</c> sidecar). This is a filesystem holding area
/// only; database rows remain hard-delete per the repo policy.
/// </summary>
public sealed class RecycleBin(SettingsService settings, ILogger<RecycleBin> logger) : IRecycleBin {
    public async Task<string?> TryMoveToBinAsync(string filePath, CancellationToken cancellationToken) {
        var config = await settings.GetRecycleBinSettingsAsync(cancellationToken);
        if (config.Path is null || !File.Exists(filePath)) {
            return null;
        }

        try {
            var folder = Path.Combine(config.Path, DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(folder);
            var target = Unique(Path.Combine(folder, Path.GetFileName(filePath)));
            File.Move(filePath, target);
            return target;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning(ex, "RecycleBin: could not move {Path} into the bin; the caller keeps its fallback.", filePath);
            return null;
        }
    }

    public async Task<int> CleanupAsync(CancellationToken cancellationToken) {
        var config = await settings.GetRecycleBinSettingsAsync(cancellationToken);
        if (config.Path is null || !Directory.Exists(config.Path)) {
            return 0;
        }

        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(Math.Max(config.CleanupDays, 1));
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(config.Path, "*", SearchOption.AllDirectories)) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                if (File.GetLastWriteTimeUtc(file) < cutoff.UtcDateTime) {
                    File.Delete(file);
                    removed++;
                }
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                logger.LogWarning(ex, "RecycleBin: could not delete {File} during cleanup.", file);
            }
        }

        // Drop emptied dated subfolders so the bin doesn't accumulate husks.
        foreach (var directory in Directory.EnumerateDirectories(config.Path, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length)) {
            try {
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) {
                    Directory.Delete(directory);
                }
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                logger.LogDebug(ex, "RecycleBin: could not remove empty folder {Directory}.", directory);
            }
        }

        return removed;
    }

    private static string Unique(string target) {
        if (!File.Exists(target)) {
            return target;
        }

        var directory = Path.GetDirectoryName(target) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(target);
        var extension = Path.GetExtension(target);
        for (var index = 2; ; index++) {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate)) {
                return candidate;
            }
        }
    }
}
