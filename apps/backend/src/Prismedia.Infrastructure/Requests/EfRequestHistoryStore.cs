using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Requests;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Requests;

public sealed class EfRequestHistoryStore(PrismediaDbContext db) : IRequestHistoryStore {
    public async Task AddAsync(RequestHistoryAddRequest request, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        db.RequestHistory.Add(new RequestHistoryRow {
            Id = Guid.NewGuid(),
            ServiceInstanceId = request.ServiceInstanceId,
            ServiceName = request.ServiceName,
            Source = request.Source,
            Kind = request.Kind,
            ExternalId = request.ExternalId,
            Title = request.Title,
            Subtitle = request.Subtitle,
            Year = request.Year,
            PosterUrl = request.PosterUrl,
            UpstreamId = request.UpstreamId,
            Monitored = request.Monitored,
            SelectedChildIds = request.SelectedChildIds.ToArray(),
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RequestHistoryEntry>> ListAsync(int limit, CancellationToken cancellationToken) {
        var rows = await db.RequestHistory
            .AsNoTracking()
            .OrderByDescending(row => row.CreatedAt)
            .Take(limit)
            .ToArrayAsync(cancellationToken);
        return rows.Select(ToEntry).ToArray();
    }

    public async Task UpdateStatusesAsync(IReadOnlyList<RequestHistoryStatusUpdate> updates, CancellationToken cancellationToken) {
        if (updates.Count == 0) {
            return;
        }

        var ids = updates.Select(update => update.Id).ToArray();
        var rows = await db.RequestHistory.Where(row => ids.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var update in updates) {
            if (!rows.TryGetValue(update.Id, out var row)) {
                continue;
            }

            row.Status = update.Status;
            row.StatusMessage = update.StatusMessage;
            row.UpstreamId = update.UpstreamId;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) {
        var row = await db.RequestHistory.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (row is null) {
            return false;
        }

        db.RequestHistory.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static RequestHistoryEntry ToEntry(RequestHistoryRow row) =>
        new(row.Id, row.ServiceInstanceId, row.ServiceName, row.Source, row.Kind, row.ExternalId, row.Title,
            row.Subtitle, row.Year, row.PosterUrl, row.UpstreamId, row.Monitored, row.SelectedChildIds.Length,
            row.Status, row.StatusMessage, row.CreatedAt, row.UpdatedAt);
}
