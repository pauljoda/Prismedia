using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// EF-backed store for the durable acquisition activity log. Append-only: only <see cref="AddAsync"/>
/// inserts, and the acquisition FK is SetNull so entries survive their acquisition's hard delete (see the
/// model configuration). There is intentionally no update or delete method.
/// </summary>
public sealed class EfAcquisitionHistoryStore(PrismediaDbContext db) : IAcquisitionHistoryStore {
    public async Task AddAsync(AcquisitionHistoryEntry entry, CancellationToken cancellationToken) {
        db.AcquisitionHistory.Add(new AcquisitionHistoryRow {
            Id = Guid.NewGuid(),
            AcquisitionId = entry.AcquisitionId,
            EntityId = entry.EntityId,
            Kind = entry.Kind,
            Event = entry.Event,
            Title = entry.Title,
            ReleaseTitle = entry.ReleaseTitle,
            IndexerName = entry.IndexerName,
            DownloadClientName = entry.DownloadClientName,
            QualityCode = entry.QualityCode,
            FormatScore = entry.FormatScore,
            Message = entry.Message,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AcquisitionHistoryView>> ListAsync(int limit, Guid? entityId, CancellationToken cancellationToken) {
        var clamped = limit <= 0
            ? AcquisitionHistoryStoreExtensions.DefaultListLimit
            : Math.Min(limit, AcquisitionHistoryStoreExtensions.MaxListLimit);

        var query = db.AcquisitionHistory.AsNoTracking();
        if (entityId is { } id) {
            query = query.Where(row => row.EntityId == id);
        }

        var rows = await query
            .OrderByDescending(row => row.CreatedAt)
            .ThenByDescending(row => row.Id)
            .Take(clamped)
            .ToArrayAsync(cancellationToken);
        return rows.Select(ToView).ToArray();
    }

    private static AcquisitionHistoryView ToView(AcquisitionHistoryRow row) =>
        new(row.Id, row.AcquisitionId, row.EntityId, row.Kind, row.Event, row.Title, row.ReleaseTitle,
            row.IndexerName, row.DownloadClientName, row.QualityCode, row.FormatScore, row.Message, row.CreatedAt);
}
