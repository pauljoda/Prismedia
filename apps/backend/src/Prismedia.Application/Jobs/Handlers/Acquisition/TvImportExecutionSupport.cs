using System.Security.Cryptography;
using Prismedia.Application.Acquisition;
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

    /// <summary>Returns true when every rejected owned file is the exact payload file already in place.</summary>
    public static bool AllDroppedFilesMatchPayload(
        IReadOnlyList<MergedImportItem> merged,
        DownloadPayload payload) =>
        merged.Count > 0
        && merged.All(item => item.Action == MergeFileAction.DropNotUpgrade
            && item.OwnedFilePath is { } ownedFilePath
            && FilesHaveSameContent(
                ownedFilePath,
                Path.GetFullPath(Path.Combine(payload.ContentRoot, item.SourceRelativePath))));

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
