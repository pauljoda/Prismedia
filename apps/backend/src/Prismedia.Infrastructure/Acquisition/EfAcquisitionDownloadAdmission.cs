using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Rechecks payload-start authority under the acquisition row and download-client operation locks.</summary>
public sealed class EfAcquisitionDownloadAdmission(PrismediaDbContext db) : IAcquisitionDownloadAdmission {
    /// <inheritdoc />
    public async Task ReleaseAsync(IDownloadClient client, DownloadClientConnection connection, Guid acquisitionId,
        Guid transferId, string clientItemId, CancellationToken cancellationToken) {
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        if (db.Database.IsRelational()) {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            command.CommandText = "SELECT id FROM acquisitions WHERE id = @id FOR UPDATE";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "id";
            parameter.Value = acquisitionId;
            command.Parameters.Add(parameter);
            await command.ExecuteScalarAsync(cancellationToken);
        }
        await using var lease = await DownloadClientOperationLock.AcquireAsync(db, connection.Id, cancellationToken);
        var active = await db.Acquisitions.AsNoTracking().AnyAsync(row => row.Id == acquisitionId
            && (row.Status == AcquisitionStatus.Queued || row.Status == AcquisitionStatus.Downloading
                || row.Status == AcquisitionStatus.WaitingForDownloadClient), cancellationToken);
        var owned = await db.DownloadTransfers.AsNoTracking().AnyAsync(row => row.Id == transferId
            && row.AcquisitionId == acquisitionId && row.DownloadClientConfigId == connection.Id
            && row.ClientItemId == clientItemId, cancellationToken);
        if (!active || !owned) return;
        await client.ReleasePayloadAsync(connection, clientItemId, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }
}
