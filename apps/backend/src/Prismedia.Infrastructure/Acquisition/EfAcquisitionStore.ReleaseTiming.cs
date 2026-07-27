using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Acquisition;

public sealed partial class EfAcquisitionStore {
    /// <inheritdoc />
    public async Task SetReleaseDateMetadataUnavailableAsync(
        Guid id,
        bool unavailable,
        string? message,
        CancellationToken cancellationToken) {
        var waitingStatuses = new[] {
            AcquisitionStatus.WaitingForRelease,
            AcquisitionStatus.ManualSearchRequired
        };
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational()) {
            var affected = await db.Acquisitions
                .Where(row => row.Id == id && waitingStatuses.Contains(row.Status))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.Status, AcquisitionStatus.WaitingForRelease)
                    .SetProperty(row => row.StatusMessage, message)
                    .SetProperty(row => row.ReleaseDateMetadataUnavailable, unavailable)
                    .SetProperty(row => row.UpdatedAt, now), cancellationToken);
            await SynchronizeTrackedAcquisitionAsync(id, affected, cancellationToken);
            return;
        }

        var row = await db.Acquisitions.FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (row is null || !waitingStatuses.Contains(row.Status)) {
            return;
        }

        row.Status = AcquisitionStatus.WaitingForRelease;
        row.StatusMessage = message;
        row.ReleaseDateMetadataUnavailable = unavailable;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
    }
}
