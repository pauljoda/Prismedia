using System.Security.Cryptography;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Scanning;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>Shared filesystem and scan-gate safeguards for durable TV import execution.</summary>
internal static class TvImportExecutionSupport {
    public const string WaitingForScanMessage = "Waiting for the current library scan to finish.";

    /// <summary>Reports the blocking scan state before waiting for exclusive TV catalog access.</summary>
    public static async ValueTask<IAsyncDisposable> EnterScanGateAsync(
        VideoScanConcurrencyGate scanGate,
        IAcquisitionStore acquisitions,
        JobContext context,
        Guid acquisitionId,
        CancellationToken cancellationToken) {
        await acquisitions.SetStatusAsync(
            acquisitionId,
            AcquisitionStatus.Importing,
            WaitingForScanMessage,
            cancellationToken);
        await context.ReportProgressAsync(0, WaitingForScanMessage, cancellationToken);
        var lease = await scanGate.EnterAsync(cancellationToken);
        try {
            await acquisitions.SetStatusAsync(
                acquisitionId,
                AcquisitionStatus.Importing,
                null,
                cancellationToken);
            await context.ReportProgressAsync(5, "Preparing import", cancellationToken);
            return lease;
        } catch {
            await lease.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Finds payload files already present byte-for-byte in one owned file. A missing second episode
    /// owner can be repaired without replacing those bytes. Multi-file ownership conflicts have no
    /// single owned path and remain held; matching names or lengths alone never authorize adoption.
    /// </summary>
    public static IReadOnlySet<string> MatchingExistingFiles(
        IReadOnlyList<MergedImportItem> merged,
        DownloadPayload payload) =>
        merged.Where(item => item.Action is MergeFileAction.DropNotUpgrade or MergeFileAction.HoldStructuralConflict
            && item.OwnedFilePath is { } ownedFilePath
            && FilesHaveSameContent(
                ownedFilePath,
                Path.GetFullPath(Path.Combine(payload.ContentRoot, item.SourceRelativePath))))
            .Select(item => item.SourceRelativePath)
            .ToHashSet(FileSystemPathComparison.Comparer);

    /// <summary>Compares two existing files by length and SHA-256 without surfacing transient IO errors.</summary>
    public static bool FilesHaveSameContent(string firstPath, string secondPath) {
        try {
            var first = new FileInfo(firstPath);
            var second = new FileInfo(secondPath);
            if (!first.Exists || !second.Exists || first.Length != second.Length) {
                return false;
            }

            using var firstStream = first.OpenRead();
            using var secondStream = second.OpenRead();
            return SHA256.HashData(firstStream).AsSpan().SequenceEqual(SHA256.HashData(secondStream));
        } catch (IOException) {
            return false;
        } catch (UnauthorizedAccessException) {
            return false;
        }
    }
}
