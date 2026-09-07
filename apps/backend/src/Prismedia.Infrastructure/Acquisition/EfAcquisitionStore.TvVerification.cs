using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Acquisition;

public sealed partial class EfAcquisitionStore {
    /// <inheritdoc />
    public async Task<bool> TryHoldTvImportCheckpointAsync(Guid acquisitionId, TvImportCheckpoint checkpoint,
        string message, CancellationToken cancellationToken) {
        if (!await UsesCheckpointProtocolAsync(acquisitionId, AcquisitionCheckpointProtocol.Television, cancellationToken)) return false;
        var expected = TvImportCheckpointJson.Serialize(checkpoint);
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational()) {
            var affected = await db.Acquisitions.Where(row => row.Id == acquisitionId
                    && row.Status == AcquisitionStatus.Importing && row.ImportClaimJobId == checkpoint.ClaimJobId
                    && row.ImportCheckpointJson == expected)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.Status, AcquisitionStatus.ManualImportRequired)
                    .SetProperty(row => row.StatusMessage, message)
                    .SetProperty(row => row.ImportClaimJobId, (Guid?)null)
                    .SetProperty(row => row.UpdatedAt, now), cancellationToken);
            return await SynchronizeTrackedAcquisitionAsync(acquisitionId, affected, cancellationToken);
        }
        var row = await db.Acquisitions.FirstOrDefaultAsync(row => row.Id == acquisitionId, cancellationToken);
        if (row is null || row.Status != AcquisitionStatus.Importing || row.ImportClaimJobId != checkpoint.ClaimJobId
            || row.ImportCheckpointJson != expected) return false;
        row.Status = AcquisitionStatus.ManualImportRequired;
        row.StatusMessage = message;
        row.ImportClaimJobId = null;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
