using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>EF-backed store for the acquisition blocklist (release identities refused for future grabs).</summary>
public sealed class EfAcquisitionBlocklistStore(PrismediaDbContext db) : IAcquisitionBlocklistStore {
    public async Task<IReadOnlySet<string>> GetIdentitiesAsync(CancellationToken cancellationToken) {
        var identities = await db.AcquisitionBlocklist
            .AsNoTracking()
            .Select(row => row.Identity)
            .ToArrayAsync(cancellationToken);
        return identities.ToHashSet(StringComparer.Ordinal);
    }

    public async Task AddAsync(BlocklistAddRequest request, CancellationToken cancellationToken) {
        // Idempotent on the identity: a release already blocklisted keeps its original reason/timestamp.
        if (await db.AcquisitionBlocklist.AnyAsync(row => row.Identity == request.Identity, cancellationToken)) {
            return;
        }

        var row = new AcquisitionBlocklistRow {
            Id = Guid.NewGuid(),
            Identity = request.Identity,
            Reason = request.Reason,
            Title = request.Title,
            IndexerName = request.IndexerName,
            InfoHash = request.InfoHash,
            AcquisitionId = request.AcquisitionId,
            Message = request.Message,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.AcquisitionBlocklist.Add(row);

        try {
            await db.SaveChangesAsync(cancellationToken);
        } catch (DbUpdateException) {
            // Lost a race against a concurrent insert of the same identity; the blocklist already holds it.
            // Detach the rejected row so it does not linger as Added in the shared scoped context and break
            // the next SaveChanges (e.g. the recovery handler's subsequent re-queue), matching the queue's
            // own duplicate-insert handling in JobQueueService.
            db.Entry(row).State = EntityState.Detached;
        }
    }

    public async Task<IReadOnlyList<AcquisitionBlocklistEntry>> ListAsync(CancellationToken cancellationToken) {
        var rows = await db.AcquisitionBlocklist
            .AsNoTracking()
            .OrderByDescending(row => row.CreatedAt)
            .ToArrayAsync(cancellationToken);
        return rows
            .Select(row => new AcquisitionBlocklistEntry(
                row.Id, row.Reason, row.Title, row.IndexerName, row.InfoHash, row.AcquisitionId, row.Message, row.CreatedAt))
            .ToArray();
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) {
        var row = await db.AcquisitionBlocklist.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (row is null) {
            return false;
        }

        db.AcquisitionBlocklist.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
