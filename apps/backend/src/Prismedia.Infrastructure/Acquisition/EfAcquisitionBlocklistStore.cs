using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;
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

        var acquisitionIds = rows
            .Where(row => row.AcquisitionId.HasValue)
            .Select(row => row.AcquisitionId!.Value)
            .Distinct()
            .ToArray();
        var acquisitionContexts = (await db.Acquisitions
                .AsNoTracking()
                .Where(row => acquisitionIds.Contains(row.Id))
                .Select(row => new { row.Id, row.EntityId, row.Kind, row.Title })
                .ToArrayAsync(cancellationToken))
            .ToDictionary(
                row => row.Id,
                row => new BlocklistEntityContext(row.Id, row.EntityId, row.Kind, row.Title));

        // Acquisition rows may be removed while both history and the blocklist deliberately survive. The
        // append-only Blocklisted event retains the work title/kind/entity, so it is the durable fallback
        // that keeps old entries organized instead of collapsing a long-lived list into "unknown".
        var historyContexts = (await db.AcquisitionHistory
                .AsNoTracking()
                .Where(row => row.Event == AcquisitionHistoryEvent.Blocklisted)
                .OrderByDescending(row => row.CreatedAt)
                .Select(row => new {
                    row.AcquisitionId,
                    row.EntityId,
                    row.Kind,
                    row.Title,
                    row.ReleaseTitle,
                    row.IndexerName
                })
                .ToArrayAsync(cancellationToken))
            .GroupBy(row => ReleaseContextKey(row.ReleaseTitle, row.IndexerName), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => {
                    var row = group.First();
                    return new BlocklistEntityContext(row.AcquisitionId, row.EntityId, row.Kind, row.Title);
                },
                StringComparer.Ordinal);

        return rows
            .Select(row => {
                BlocklistEntityContext? context = null;
                if (row.AcquisitionId is { } acquisitionId) {
                    acquisitionContexts.TryGetValue(acquisitionId, out context);
                }
                context ??= historyContexts.GetValueOrDefault(ReleaseContextKey(row.Title, row.IndexerName));
                return new AcquisitionBlocklistEntry(
                    row.Id,
                    row.Reason,
                    row.Title,
                    row.IndexerName,
                    row.InfoHash,
                    row.AcquisitionId,
                    context?.EntityId,
                    context?.Kind,
                    context?.Title,
                    row.Message,
                    row.CreatedAt);
            })
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

    public async Task<int> ClearAsync(
        Guid? entityId,
        DateTimeOffset? createdAfter,
        CancellationToken cancellationToken) {
        var entries = await ListAsync(cancellationToken);
        var matchingIds = entries
            .Where(entry => entityId is null || entry.EntityId == entityId)
            .Where(entry => createdAfter is null || entry.CreatedAt >= createdAfter)
            .Select(entry => entry.Id)
            .ToArray();
        if (matchingIds.Length == 0) {
            return 0;
        }

        var rows = await db.AcquisitionBlocklist
            .Where(row => matchingIds.Contains(row.Id))
            .ToArrayAsync(cancellationToken);
        db.AcquisitionBlocklist.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        return rows.Length;
    }

    private static string ReleaseContextKey(string? title, string? indexerName) =>
        $"{title?.Trim().ToUpperInvariant()}\u001f{indexerName?.Trim().ToUpperInvariant()}";

    private sealed record BlocklistEntityContext(
        Guid? AcquisitionId,
        Guid? EntityId,
        EntityKind Kind,
        string Title);
}
