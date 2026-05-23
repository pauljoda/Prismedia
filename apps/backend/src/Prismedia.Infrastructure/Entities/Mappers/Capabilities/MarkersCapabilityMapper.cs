using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Capabilities;

internal sealed class MarkersCapabilityMapper(PrismediaDbContext db) : IEntityCapabilityMapper {
    public async Task HydrateAsync(Entity entity, CancellationToken cancellationToken) {
        var rows = await db.EntityMarkers.AsNoTracking()
            .Where(r => r.EntityId == entity.Id)
            .OrderBy(r => r.Seconds)
            .ToArrayAsync(cancellationToken);
        if (rows.Length == 0) {
            return;
        }

        entity.RemoveCapability<CapabilityMarkers>();
        entity.AddCapability(new CapabilityMarkers(rows.Select(r =>
            new CapabilityMarkers.Item(r.Id, r.Title, r.Seconds, r.EndSeconds)).ToArray()));
    }

    public Task ClearAsync(Entity entity, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task PersistAsync(Entity entity, CancellationToken cancellationToken) {
        if (entity.MarkerCapability is not { } markers) {
            return;
        }

        var existing = await db.EntityMarkers
            .Where(r => r.EntityId == entity.Id)
            .ToArrayAsync(cancellationToken);
        var byId = existing.ToDictionary(r => r.Id);
        var markerIds = markers.Items.Select(m => m.Id).ToHashSet();
        var now = DateTimeOffset.UtcNow;

        foreach (var stale in existing.Where(r => !markerIds.Contains(r.Id))) {
            db.EntityMarkers.Remove(stale);
        }

        foreach (var marker in markers.Items) {
            if (byId.TryGetValue(marker.Id, out var row)) {
                row.Title = marker.Title;
                row.Seconds = marker.Seconds;
                row.EndSeconds = marker.EndSeconds;
                row.UpdatedAt = now;
                continue;
            }

            db.EntityMarkers.Add(new EntityMarkerRow {
                Id = marker.Id,
                EntityId = entity.Id,
                Title = marker.Title,
                Seconds = marker.Seconds,
                EndSeconds = marker.EndSeconds,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
    }
}
