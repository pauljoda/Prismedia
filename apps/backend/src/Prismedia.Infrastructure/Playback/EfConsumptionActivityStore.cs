using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Playback;
using Prismedia.Application.Security;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Playback;

/// <summary>EF Core store that upserts active time into one row per user/entity/mode/day.</summary>
public sealed class EfConsumptionActivityStore(PrismediaDbContext db, ICurrentUserContext currentUser)
    : IConsumptionActivityStore {
    /// <inheritdoc />
    public async Task StageAsync(ConsumptionActivityAppend entry, CancellationToken cancellationToken) {
        var userId = currentUser.UserId == Guid.Empty ? (Guid?)null : currentUser.UserId;
        var row = await db.EntityConsumptionDays.SingleOrDefaultAsync(candidate =>
            candidate.UserId == userId &&
            candidate.EntityId == entry.EntityId &&
            candidate.Kind == entry.Kind &&
            candidate.ActivityDate == entry.ActivityDate,
            cancellationToken);
        if (row is null) {
            db.EntityConsumptionDays.Add(new EntityConsumptionDayRow {
                Id = Guid.NewGuid(),
                EntityId = entry.EntityId,
                UserId = userId,
                Kind = entry.Kind,
                ActivityDate = entry.ActivityDate,
                DurationSeconds = entry.DurationSeconds,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            return;
        }

        row.DurationSeconds += entry.DurationSeconds;
        row.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
