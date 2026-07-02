using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Imports a completed acquisition by dispatching to the media kind's <see cref="IAcquisitionImportEngine"/>
/// (books, movies, music — each owns its planning, placement, hint, and scan chaining). A kind with no
/// registered engine stays Downloaded (files intact in the client) with an honest status instead of being
/// pushed through the wrong pipeline.
/// </summary>
public sealed class AcquisitionImportJobHandler(
    IAcquisitionStore acquisitions,
    IAcquisitionImportEngineFactory engines,
    ILogger<AcquisitionImportJobHandler> logger) : IJobHandler {
    public JobType Type => JobType.AcquisitionImport;

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var payload = AcquisitionJobPayload.Parse(context.Job.PayloadJson);
        var import = await acquisitions.GetImportContextAsync(payload.AcquisitionId, cancellationToken);
        if (import is null) {
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
            throw;
        }
    }
}
