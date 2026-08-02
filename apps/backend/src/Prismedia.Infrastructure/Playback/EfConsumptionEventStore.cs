using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Playback;
using Prismedia.Application.Security;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Playback;

/// <summary>EF Core store for discrete consumption history.</summary>
public sealed class EfConsumptionEventStore(PrismediaDbContext db, ICurrentUserContext currentUser)
    : IConsumptionEventStore {
    /// <inheritdoc />
    public async Task<bool> ContainsSessionEventAsync(
        string sessionId,
        ConsumptionEventKind kind,
        CancellationToken cancellationToken) {
        var normalized = sessionId.Trim();
        Guid? userId = currentUser.UserId == Guid.Empty ? null : currentUser.UserId;
        return db.EntityConsumptionEvents.Local.Any(row =>
                   row.UserId == userId && row.SessionId == normalized && row.Kind == kind) ||
               await db.EntityConsumptionEvents.AsNoTracking().AnyAsync(row =>
                   row.UserId == userId && row.SessionId == normalized && row.Kind == kind,
                   cancellationToken);
    }

    /// <inheritdoc />
    public Task StageAsync(ConsumptionEventAppend entry, CancellationToken cancellationToken) {
        db.EntityConsumptionEvents.Add(new EntityConsumptionEventRow {
            Id = Guid.NewGuid(),
            EntityId = entry.EntityId,
            UserId = currentUser.UserId == Guid.Empty ? null : currentUser.UserId,
            Kind = entry.Kind,
            OccurredAt = entry.OccurredAt,
            PositionSeconds = entry.PositionSeconds,
            DurationSeconds = entry.DurationSeconds,
            SessionId = string.IsNullOrWhiteSpace(entry.SessionId) ? null : entry.SessionId.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        });

        return Task.CompletedTask;
    }
}
