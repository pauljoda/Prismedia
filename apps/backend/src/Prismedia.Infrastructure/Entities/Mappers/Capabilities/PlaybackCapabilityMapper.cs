using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Capabilities;

internal sealed class PlaybackCapabilityMapper(PrismediaDbContext db) : IEntityCapabilityMapper {
    public async Task HydrateAsync(Entity entity, CancellationToken cancellationToken) {
        var row = await db.EntityPlayback.AsNoTracking()
            .FirstOrDefaultAsync(r => r.EntityId == entity.Id, cancellationToken);
        if (row is null) {
            return;
        }

        entity.RemoveCapability<CapabilityPlayback>();
        entity.AddCapability(new CapabilityPlayback(new CapabilityPlayback.State(
            row.PlayCount,
            TimeSpan.FromSeconds(row.PlayDurationSeconds),
            TimeSpan.FromSeconds(row.ResumeSeconds),
            row.LastPlayedAt,
            row.CompletedAt)));
    }

    public Task ClearAsync(Entity entity, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task PersistAsync(Entity entity, CancellationToken cancellationToken) {
        if (entity.PlaybackCapability is not { Value: { } playback }) {
            return;
        }

        var existing = await db.EntityPlayback.FindAsync([entity.Id], cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (existing is null) {
            db.EntityPlayback.Add(new EntityPlaybackRow {
                EntityId = entity.Id,
                PlayCount = playback.PlayCount,
                PlayDurationSeconds = playback.PlayDuration.TotalSeconds,
                ResumeSeconds = playback.ResumeTime.TotalSeconds,
                LastPlayedAt = playback.LastPlayedAt,
                CompletedAt = playback.CompletedAt,
                UpdatedAt = now,
            });
        } else {
            existing.PlayCount = playback.PlayCount;
            existing.PlayDurationSeconds = playback.PlayDuration.TotalSeconds;
            existing.ResumeSeconds = playback.ResumeTime.TotalSeconds;
            existing.LastPlayedAt = playback.LastPlayedAt;
            existing.CompletedAt = playback.CompletedAt;
            existing.UpdatedAt = now;
        }
    }
}
