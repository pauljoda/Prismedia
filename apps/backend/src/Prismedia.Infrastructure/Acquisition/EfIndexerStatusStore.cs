using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// EF-backed per-indexer health: consecutive failures climb the backoff ladder and open a
/// suppression window; successes step back down one level at a time so recovery is gradual.
/// </summary>
public sealed class EfIndexerStatusStore(PrismediaDbContext db) : IIndexerStatusStore {
    public async Task<IReadOnlyDictionary<Guid, IndexerHealth>> GetAllAsync(CancellationToken cancellationToken) {
        var rows = await db.IndexerStatuses.AsNoTracking().ToArrayAsync(cancellationToken);
        return rows.ToDictionary(
            row => row.IndexerConfigId,
            row => new IndexerHealth(row.IndexerConfigId, row.EscalationLevel, row.DisabledUntil, row.LastFailureMessage));
    }

    public async Task RecordFailureAsync(Guid indexerConfigId, string message, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var row = await db.IndexerStatuses.FirstOrDefaultAsync(candidate => candidate.IndexerConfigId == indexerConfigId, cancellationToken);
        if (row is null) {
            row = new IndexerStatusRow { IndexerConfigId = indexerConfigId };
            db.IndexerStatuses.Add(row);
        }

        row.EscalationLevel = Math.Min(row.EscalationLevel + 1, IndexerBackoffLadder.MaxLevel);
        row.DisabledUntil = now + IndexerBackoffLadder.For(row.EscalationLevel);
        row.LastFailureMessage = message.Length <= 1024 ? message : message[..1024];
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordSuccessAsync(Guid indexerConfigId, CancellationToken cancellationToken) {
        var row = await db.IndexerStatuses.FirstOrDefaultAsync(candidate => candidate.IndexerConfigId == indexerConfigId, cancellationToken);
        if (row is null || row.EscalationLevel == 0) {
            return;
        }

        row.EscalationLevel -= 1;
        row.DisabledUntil = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
