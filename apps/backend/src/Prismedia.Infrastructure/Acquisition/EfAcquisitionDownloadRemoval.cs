using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Revalidates exact download ownership under the same client-wide lock used by remote adds.</summary>
public sealed class EfAcquisitionDownloadRemoval(PrismediaDbContext db, EfCompletedDownloadPayloadCleanup? completedPayloads = null) : IAcquisitionDownloadRemoval {
    private static readonly AcquisitionStatus[] ReleasedStatuses = [
        AcquisitionStatus.Imported, AcquisitionStatus.Failed, AcquisitionStatus.Cancelled, AcquisitionStatus.Stopping
    ];

    /// <inheritdoc />
    public async Task RemoveAsync(IDownloadClient client, DownloadClientConnection connection, Guid? acquisitionId,
        string clientItemId, bool deleteData, CancellationToken cancellationToken, Guid? transferId = null) {
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        // Match Add's acquisition-row -> client-lock ordering so teardown and queue compensation cannot deadlock.
        if (db.Database.IsRelational() && acquisitionId is { } ownerId) {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            command.CommandText = "SELECT id FROM acquisitions WHERE id = @id FOR UPDATE";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "id";
            parameter.Value = ownerId;
            command.Parameters.Add(parameter);
            await command.ExecuteScalarAsync(cancellationToken);
        }
        await using var clientLease = await DownloadClientOperationLock.AcquireAsync(db, connection.Id, cancellationToken);
        var transfers = await db.DownloadTransfers.AsNoTracking()
            .Where(row => row.DownloadClientConfigId == connection.Id).ToArrayAsync(cancellationToken);
        var owned = transfers.Any(row => row.AcquisitionId == acquisitionId
            && (transferId is null || row.Id == transferId)
            && (string.Equals(row.ClientItemId, clientItemId, StringComparison.OrdinalIgnoreCase)
                || row.State == TransferOwnershipState.Adding.ToCode()));
        var detached = await db.DetachedDownloadCleanups.AsNoTracking().AnyAsync(row =>
            row.SourceAcquisitionId == acquisitionId && row.DownloadClientConfigId == connection.Id
                && row.ClientItemId == clientItemId, cancellationToken);
        if (!owned && !detached) {
            throw new IOException("The download's recorded owner changed before cleanup; its data was preserved.");
        }
        var other = transfers.Where(row => row.AcquisitionId != acquisitionId
            && string.Equals(row.ClientItemId, clientItemId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (other.Length > 0) {
            var ownerIds = other.Select(row => row.AcquisitionId).ToArray();
            var states = await db.Acquisitions.AsNoTracking().Where(row => ownerIds.Contains(row.Id))
                .ToDictionaryAsync(row => row.Id, row => row.Status, cancellationToken);
            if (other.Any(row => !states.TryGetValue(row.AcquisitionId, out var status) || !ReleasedStatuses.Contains(status))) {
                throw new IOException("Another active acquisition still needs this download; its data was preserved.");
            }
            var seeding = other.Where(row => states.GetValueOrDefault(row.AcquisitionId) == AcquisitionStatus.Imported
                && row.SeedingSince is not null && (row.SeedGoalRatio is not null || row.SeedGoalTimeMinutes is not null)).ToArray();
            if (seeding.Length > 0) {
                var properties = await client.GetPropertiesAsync(connection, clientItemId, cancellationToken);
                if (properties is not null && seeding.Any(row =>
                    !(row.SeedGoalRatio is { } ratio && properties.Ratio >= ratio)
                    && !(row.SeedGoalTimeMinutes is { } minutes &&
                        (properties.SeedingTimeSeconds is { } seconds ? seconds >= minutes * 60L
                            : DateTimeOffset.UtcNow - row.SeedingSince!.Value >= TimeSpan.FromMinutes(minutes))))) {
                    throw new IOException("Another acquisition's seeding goal is still pending; the shared download was preserved.");
                }
            }
        }
        if (deleteData && !client.DeletesCompletedPayload) {
            var cleanup = completedPayloads ?? throw new InvalidOperationException("Completed payload cleanup is not configured.");
            await cleanup.DeleteAsync(client, connection, acquisitionId, clientItemId,
                await client.GetItemAsync(connection, clientItemId, cancellationToken), cancellationToken);
        }
        await client.RemoveAsync(connection, clientItemId, deleteData, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }
}
