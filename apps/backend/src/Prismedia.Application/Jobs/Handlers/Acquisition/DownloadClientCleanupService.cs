using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Owns terminal download-client cleanup. Imported hardlink/copy payloads remain only while an explicit
/// seed goal is pending; imports without a goal, moved payloads, partial payloads, and failed downloads are
/// removed with their data. A client outage leaves a durable post-transfer watch so the monitor retries
/// cleanup instead of silently abandoning the client item.
/// </summary>
public sealed class DownloadClientCleanupService(
    IAcquisitionStore acquisitions,
    IDownloadClientConfigStore downloadClients,
    IDownloadClientFactory clients,
    ILogger<DownloadClientCleanupService> logger,
    IAcquisitionUploadStorage? uploads = null) {
    /// <summary>Move removes now; hardlink/copy enters a seed-or-cleanup watch that removes immediately when no goal exists.</summary>
    public Task HandleImportedAsync(
        AcquisitionImportContext import,
        ImportMode mode,
        CancellationToken cancellationToken) =>
        HandleImportedAsync(import, mode, discardRemainingPayload: false, cancellationToken);

    /// <summary>
    /// Finishes transfer cleanup, forcing immediate data removal when the importer kept only a requested
    /// subset. A partial payload cannot remain a trustworthy seeding source after its extras are discarded.
    /// </summary>
    public async Task HandleImportedAsync(
        AcquisitionImportContext import,
        ImportMode mode,
        bool discardRemainingPayload,
        CancellationToken cancellationToken) {
        if (uploads?.Owns(import.ClientItemId) == true) {
            await uploads.DeleteAsync(import.ClientItemId!, cancellationToken);
            return;
        }
        if (mode == ImportMode.Move || discardRemainingPayload) {
            await RemoveNowOrScheduleRetryAsync(import, cancellationToken);
            return;
        }

        try {
            if (await acquisitions.MarkTransferSeedingAsync(import.Id, DateTimeOffset.UtcNow, cancellationToken)) {
                logger.LogDebug("AcquisitionImport: acquisition {Id} handed to post-transfer cleanup watch.", import.Id);
            }
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logger.LogWarning(ex, "AcquisitionImport: failed to start post-transfer watch for acquisition {Id}", import.Id);
        }
    }

    /// <summary>Removes an exact recorded client item now, scheduling a durable retry when cleanup cannot be confirmed.</summary>
    public async Task RemoveNowOrScheduleRetryAsync(
        AcquisitionImportContext import,
        CancellationToken cancellationToken) {
        if (await TryRemoveAsync(import, cancellationToken)) {
            return;
        }

        try {
            await acquisitions.MarkTransferSeedingAsync(import.Id, DateTimeOffset.UtcNow, CancellationToken.None);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning(
                ex,
                "DownloadClientCleanup: failed to schedule another cleanup attempt for acquisition {Id}",
                import.Id);
        }
    }

    private async Task<bool> TryRemoveAsync(AcquisitionImportContext import, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(import.ClientItemId)) {
            return true;
        }

        // A recorded owner is authoritative. Falling back to another configured client is only safe for
        // legacy transfers that never recorded an owner; identical hashes/ids can exist in two clients.
        var client = import.DownloadClientConfigId is { } id
            ? await downloadClients.GetAsync(id, cancellationToken)
            : await downloadClients.GetDefaultAsync(cancellationToken);
        if (client is null) {
            logger.LogWarning(
                "DownloadClientCleanup: recorded client for acquisition {Id} is unavailable; cleanup will retry.",
                import.Id);
            return false;
        }

        try {
            var connection = new DownloadClientConnection(
                client.Id,
                client.Kind,
                client.BaseUrl,
                client.Username,
                client.Password,
                client.Category,
                client.ApiKey,
                client.DownloadDirectory);
            var download = clients.Get(client.Kind);
            await download.RemoveAsync(connection, import.ClientItemId, deleteData: true, cancellationToken);
            if (await download.GetItemAsync(connection, import.ClientItemId, cancellationToken) is not null) {
                throw new IOException("The transfer is still present after the client acknowledged removal.");
            }
            return true;
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logger.LogWarning(ex, "DownloadClientCleanup: failed to remove client item for acquisition {Id}", import.Id);
            return false;
        }
    }
}
