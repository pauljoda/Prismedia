using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Cancels queued/running durable jobs by the exact acquisition id stored as their target.</summary>
public sealed class AcquisitionJobCleanup(PrismediaDbContext db) : IAcquisitionJobCleanup {
    /// <inheritdoc />
    public async Task<int> CancelAsync(Guid acquisitionId, CancellationToken cancellationToken) {
        var target = acquisitionId.ToString();
        var now = DateTimeOffset.UtcNow;
        var query = db.JobRuns.Where(job =>
            job.TargetEntityId == target
            && (job.Status == JobRunStatus.Queued || job.Status == JobRunStatus.Running));

        if (db.Database.IsRelational()) {
            return await query.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.Status, JobRunStatus.Cancelled)
                    .SetProperty(job => job.Message, "Cancelled because its acquisition was removed.")
                    .SetProperty(job => job.LockedAt, (DateTimeOffset?)null)
                    .SetProperty(job => job.LockedBy, (string?)null)
                    .SetProperty(job => job.FinishedAt, now),
                cancellationToken);
        }

        var rows = await query.ToArrayAsync(cancellationToken);
        foreach (var row in rows) {
            row.Status = JobRunStatus.Cancelled;
            row.Message = "Cancelled because its acquisition was removed.";
            row.LockedAt = null;
            row.LockedBy = null;
            row.FinishedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return rows.Length;
    }
}
