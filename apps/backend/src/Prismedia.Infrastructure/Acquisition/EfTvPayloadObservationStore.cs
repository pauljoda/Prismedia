using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Request-owned payload evidence; bounded retention avoids turning transient observations into permanent blocks.</summary>
public sealed class EfTvPayloadObservationStore(PrismediaDbContext db) : ITvPayloadObservationStore {
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    /// <inheritdoc />
    public async Task RecordAsync(Guid acquisitionId, TvPayloadObservation observation, CancellationToken cancellationToken) {
        var row = await db.Acquisitions.AsNoTracking().FirstOrDefaultAsync(row => row.Id == acquisitionId, cancellationToken);
        if (row is null || string.IsNullOrWhiteSpace(row.SelectedReleaseJson)
            || JsonSerializer.Deserialize<SelectedRelease>(row.SelectedReleaseJson)?.Identity != observation.Identity) return;
        var oldJson = row.TvPayloadObservationsJson;
        var updated = Read(oldJson).Where(value => value.Identity != observation.Identity && value.ObservedAt >= DateTimeOffset.UtcNow - Retention)
            .Append(observation).OrderByDescending(value => value.ObservedAt).ToArray();
        var json = JsonSerializer.Serialize(updated);
        if (db.Database.IsRelational()) {
            await db.Acquisitions.Where(value => value.Id == acquisitionId && value.SelectedReleaseJson == row.SelectedReleaseJson
                    && value.TvPayloadObservationsJson == oldJson)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.TvPayloadObservationsJson, json), cancellationToken);
        } else if (await db.Acquisitions.FindAsync([acquisitionId], cancellationToken) is { } tracked) {
            tracked.TvPayloadObservationsJson = json;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TvPayloadObservation>> ListAsync(AcquisitionSearchInput input, CancellationToken cancellationToken) {
        var rows = await db.Acquisitions.AsNoTracking()
            .Where(row => row.TvPayloadObservationsJson != null
                && (row.Id == input.Id || (input.EntityId != null && row.EntityId == input.EntityId)))
            .OrderByDescending(row => row.CreatedAt).Take(32)
            .Select(row => row.TvPayloadObservationsJson).ToArrayAsync(cancellationToken);
        return rows.SelectMany(Read).Where(value => value.ObservedAt >= DateTimeOffset.UtcNow - Retention)
            .OrderByDescending(value => value.ObservedAt).DistinctBy(value => value.Identity).ToArray();
    }

    private static IReadOnlyList<TvPayloadObservation> Read(string? json) => string.IsNullOrWhiteSpace(json)
        ? [] : JsonSerializer.Deserialize<TvPayloadObservation[]>(json) ?? [];
}
