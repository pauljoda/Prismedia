using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

public sealed partial class EfAcquisitionStore {
    /// <summary>
    /// Finds failed initial imports that still own a completed download payload. These rows have no
    /// placement checkpoint yet, but can safely retry the same bytes instead of downloading again.
    /// </summary>
    private async Task<IReadOnlySet<Guid>> ResumablePayloadAcquisitionIdsAsync(
        IReadOnlyList<AcquisitionRow> rows,
        CancellationToken cancellationToken) {
        var failedIds = rows
            .Where(row => row.Status == AcquisitionStatus.Failed && row.ImportCheckpointJson is null)
            .Select(row => row.Id)
            .ToArray();
        if (failedIds.Length == 0) {
            return new HashSet<Guid>();
        }

        return (await db.DownloadTransfers
                .AsNoTracking()
                .Where(transfer => failedIds.Contains(transfer.AcquisitionId)
                    && transfer.Progress >= 1
                    && transfer.ContentPath != null
                    && transfer.ContentPath != "")
                .Select(transfer => transfer.AcquisitionId)
                .Distinct()
                .ToArrayAsync(cancellationToken))
            .ToHashSet();
    }

    private Task<bool> HasCompletedPayloadAsync(Guid acquisitionId, CancellationToken cancellationToken) =>
        db.DownloadTransfers
            .AsNoTracking()
            .AnyAsync(transfer => transfer.AcquisitionId == acquisitionId
                && transfer.Progress >= 1
                && transfer.ContentPath != null
                && transfer.ContentPath != "", cancellationToken);
}
