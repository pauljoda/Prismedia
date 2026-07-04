using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Imports a completed acquisition by dispatching to the media kind's <see cref="IAcquisitionImportEngine"/>
/// (books, movies, music — each owns its planning, placement, hint, and scan chaining). A payload carrying
/// an executable or dangerous file is held for manual review before any engine runs. A kind with no
/// registered engine stays Downloaded (files intact in the client) with an honest status instead of being
/// pushed through the wrong pipeline.
/// </summary>
public sealed class AcquisitionImportJobHandler(
    IAcquisitionStore acquisitions,
    IAcquisitionImportEngineFactory engines,
    IDownloadPayloadReader payloads,
    IAcquisitionHistoryStore history,
    ILogger<AcquisitionImportJobHandler> logger) : IJobHandler {
    public JobType Type => JobType.AcquisitionImport;

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var payload = AcquisitionJobPayload.Parse(context.Job.PayloadJson);
        var import = await acquisitions.GetImportContextAsync(payload.AcquisitionId, cancellationToken);
        if (import is null) {
            return;
        }

        // The dangerous-file hold runs before ANY engine: a release whose payload carries an executable
        // (the classic fake-release .scr) is never imported automatically and never silently skipped —
        // it waits, visibly, for the user to review, blocklist, or import manually.
        if (!string.IsNullOrWhiteSpace(import.ContentPath)
            && payloads.Read(import.ContentPath) is { } downloadPayload
            && DangerousFileDetection.FindDangerousFile(downloadPayload.Files.Select(file => file.RelativePath)) is { } dangerous) {
            logger.LogWarning("AcquisitionImport: dangerous file {File} held for acquisition {Id}", dangerous, payload.AcquisitionId);
            var holdMessage = $"The download contains a potentially dangerous file (\"{Path.GetFileName(dangerous)}\") and was not imported. Review it, or block this release and search again.";
            await acquisitions.SetStatusAsync(payload.AcquisitionId, AcquisitionStatus.ManualImportRequired, holdMessage, cancellationToken);
            await RecordImportFailedAsync(import, holdMessage, cancellationToken);
            return;
        }

        var engine = engines.Find(import.Kind);
        if (engine is null) {
            await acquisitions.SetStatusAsync(
                payload.AcquisitionId,
                AcquisitionStatus.Downloaded,
                $"Downloaded. Automatic import for {import.Kind.ToCode()} acquisitions isn't available yet — the files remain in the download client.",
                cancellationToken);
            return;
        }

        await acquisitions.SetStatusAsync(payload.AcquisitionId, AcquisitionStatus.Importing, null, cancellationToken);

        try {
            await engine.ImportAsync(context, import, cancellationToken);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logger.LogWarning(ex, "AcquisitionImport: failed for acquisition {Id}", payload.AcquisitionId);
            await acquisitions.SetStatusAsync(payload.AcquisitionId, AcquisitionStatus.Failed, $"Import failed: {ex.Message}", CancellationToken.None);
            await RecordImportFailedAsync(import, $"Import failed: {ex.Message}", CancellationToken.None);
            throw;
        }
    }

    /// <summary>Records a durable ImportFailed event (a manual-import hold or an import exception) against the acquisition. Best-effort.</summary>
    private Task RecordImportFailedAsync(AcquisitionImportContext import, string message, CancellationToken cancellationToken) =>
        history.SafeAddAsync(logger, new AcquisitionHistoryEntry(
            import.Id,
            EntityId: null,
            import.Kind,
            AcquisitionHistoryEvent.ImportFailed,
            import.Title,
            Message: message),
            cancellationToken);
}
