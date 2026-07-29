using Prismedia.Application.Playback;
using Prismedia.Application.Security;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Playback;

/// <summary>
/// EF Core activity-event store. Staged rows commit with the entity progress mutation that
/// accumulated the same interval into its playback capability.
/// </summary>
public sealed class EfEntityActivityStore(PrismediaDbContext db, ICurrentUserContext currentUser)
    : IEntityActivityStore {
    /// <inheritdoc />
    public Task StageAsync(EntityActivityAppend entry, CancellationToken cancellationToken) {
        db.EntityActivityEvents.Add(new EntityActivityEventRow {
            Id = Guid.NewGuid(),
            EntityId = entry.EntityId,
            UserId = currentUser.UserId == Guid.Empty ? null : currentUser.UserId,
            Kind = entry.Kind,
            OccurredAt = entry.OccurredAt,
            DurationSeconds = entry.DurationSeconds,
            CreatedAt = DateTimeOffset.UtcNow
        });
        return Task.CompletedTask;
    }
}
