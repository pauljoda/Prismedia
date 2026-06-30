using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Imports a completed acquisition: plans the move of supported book files into the target library root,
/// writes the path-keyed identify hint, enqueues a book scan, and (for move imports) removes the torrent
/// and its data so nothing is left seeding. Ambiguous payloads stop at manual-import-required instead of guessing.
/// </summary>
public sealed class AcquisitionImportJobHandler(
    IAcquisitionStore acquisitions,
    IBookAcquisitionProfileStore profiles,
    ILibraryScanRootPersistence roots,
    IAcquisitionImportPlanner planner,
    IImportFileMover mover,
    IDownloadClientConfigStore downloadClients,
    IDownloadClientFactory clients,
    ILogger<AcquisitionImportJobHandler> logger) : IJobHandler {
    public JobType Type => JobType.AcquisitionImport;

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var payload = AcquisitionJobPayload.Parse(context.Job.PayloadJson);
        var import = await acquisitions.GetImportContextAsync(payload.AcquisitionId, cancellationToken);
        if (import is null) {
            return;
        }

        await acquisitions.SetStatusAsync(payload.AcquisitionId, AcquisitionStatus.Importing, null, cancellationToken);

        try {
            var profile = await profiles.GetDefaultImportProfileAsync(cancellationToken);
            if (profile is null) {
                await Fail(payload.AcquisitionId, "No book acquisition profile is configured for import.", cancellationToken);
                return;
            }

            var root = await roots.GetLibraryRootAsync(profile.TargetLibraryRootId, cancellationToken);
            if (root is null || !root.ScanBooks) {
                await Fail(payload.AcquisitionId, "The profile's target library root is missing or not book-enabled.", cancellationToken);
                return;
            }

            if (string.IsNullOrWhiteSpace(import.ContentPath)) {
                await Fail(payload.AcquisitionId, "The completed download reported no content path.", cancellationToken);
                return;
            }

            var templateContext = new ImportTemplateContext(import.Title, import.Author, import.Year);
            var plan = await planner.PlanAsync(import.ContentPath, root.Path, profile, templateContext, cancellationToken);
            if (plan.Blocked) {
                await acquisitions.SetStatusAsync(payload.AcquisitionId, AcquisitionStatus.ManualImportRequired, BlockMessage(plan.BlockReason), cancellationToken);
                return;
            }

            await context.ReportProgressAsync(40, "Moving files", cancellationToken);
            var finalPaths = new List<string>(plan.Items.Count);
            foreach (var item in plan.Items) {
                finalPaths.Add(await mover.PlaceAsync(item, profile.ImportMode, cancellationToken));
            }

            // Capture the owned quality: format from the actual placed file (file truth, so a release that
            // claimed retail-EPUB but delivered a PDF is recorded honestly), source from the selected release
            // title (provenance is not in the bytes). Stamped onto the acquisition and carried on the hint so
            // the scan can record it on the book's detail row.
            var selected = await acquisitions.GetSelectedReleaseAsync(payload.AcquisitionId, cancellationToken);
            var ownedQuality = new BookQualityRank(
                selected is null ? BookSourceTier.Unknown : BookFormatDetection.DetectSource(selected.Title),
                BookFormatDetection.FormatTierFromExtension(finalPaths[0]));

            var hintFolder = Path.GetDirectoryName(finalPaths[0]) ?? root.Path;
            await acquisitions.WriteImportHintAsync(payload.AcquisitionId, hintFolder, import, ownedQuality, cancellationToken);
            await acquisitions.SetFinalSourcePathAsync(payload.AcquisitionId, hintFolder, cancellationToken);

            await context.ReportProgressAsync(80, "Scanning library", cancellationToken);
            await context.EnqueueIfNeededAsync(new EnqueueJobRequest(JobType.ScanBook, TargetLabel: "Imported book scan"), cancellationToken);

            if (profile.ImportMode == ImportMode.Move) {
                await RemoveTorrentAsync(import, cancellationToken);
            }

            await acquisitions.MarkImportedWithQualityAsync(payload.AcquisitionId, ownedQuality, "Imported into the library.", cancellationToken);
            await context.ReportProgressAsync(100, "Imported", cancellationToken);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logger.LogWarning(ex, "AcquisitionImport: failed for acquisition {Id}", payload.AcquisitionId);
            await acquisitions.SetStatusAsync(payload.AcquisitionId, AcquisitionStatus.Failed, $"Import failed: {ex.Message}", CancellationToken.None);
            throw;
        }
    }

    private async Task RemoveTorrentAsync(AcquisitionImportContext import, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(import.ClientItemId)) {
            return;
        }

        var client = import.DownloadClientConfigId is { } id
            ? await downloadClients.GetAsync(id, cancellationToken) ?? await downloadClients.GetDefaultAsync(cancellationToken)
            : await downloadClients.GetDefaultAsync(cancellationToken);
        if (client is null) {
            return;
        }

        try {
            var connection = new DownloadClientConnection(client.Id, client.Kind, client.BaseUrl, client.Username, client.Password, client.Category);
            await clients.Get(client.Kind).RemoveAsync(connection, import.ClientItemId, deleteData: true, cancellationToken);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            // The book is already imported; a failure cleaning up the torrent should not fail the import.
            logger.LogWarning(ex, "AcquisitionImport: failed to remove torrent for acquisition {Id}", import.Id);
        }
    }

    private Task Fail(Guid acquisitionId, string message, CancellationToken cancellationToken) =>
        acquisitions.SetStatusAsync(acquisitionId, AcquisitionStatus.Failed, message, cancellationToken);

    private static string BlockMessage(ImportBlockReason? reason) => reason switch {
        ImportBlockReason.NoSupportedPayload => "The download contains no supported book files (CBZ, ZIP, EPUB, PDF).",
        ImportBlockReason.AmbiguousMultiplePrimaries => "The download contains multiple books; import the right one manually.",
        ImportBlockReason.MixedPayload => "The download mixes a book file with comic archives; import manually.",
        _ => "The download could not be imported automatically."
    };
}
