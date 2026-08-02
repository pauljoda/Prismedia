using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Security;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Entities.Mappers.Capabilities;

/// <summary>
/// Hydrates and persists the generalized consumption capability against the current user's
/// <c>user_entity_states</c> row. Without an authenticated user the capability remains empty.
/// </summary>
internal sealed class ConsumptionCapabilityMapper(PrismediaDbContext db, ICurrentUserContext currentUser) :
    IEntityCapabilityMapper,
    IEntityMutableStateMapper<CapabilityConsumption> {
    public async Task HydrateAsync(Entity entity, CancellationToken cancellationToken) {
        var userId = currentUser.UserId;
        if (userId == Guid.Empty) {
            return;
        }

        var rollupIds = await LoadRollupLeafIdsAsync(entity.Id, cancellationToken);
        var stateIds = rollupIds.Append(entity.Id).Distinct().ToArray();
        var rows = await db.UserEntityStates
            .Where(row => row.UserId == userId && stateIds.Contains(row.EntityId))
            .ToArrayAsync(cancellationToken);
        var direct = rows.SingleOrDefault(row => row.EntityId == entity.Id);
        var consumedRows = rows
            .Where(row => stateIds.Contains(row.EntityId))
            .Where(UserEntityStateColumns.HasConsumption)
            .ToArray();
        if (consumedRows.Length == 0) {
            return;
        }

        var completedAt = rollupIds.Count > 0 && rollupIds.All(id =>
            rows.Any(row => row.EntityId == id && row.CompletedAt is not null))
                ? rows.Where(row => rollupIds.Contains(row.EntityId)).Max(row => row.CompletedAt)
                : direct?.CompletedAt;

        entity.RemoveCapability<CapabilityConsumption>();
        entity.AddCapability(new CapabilityConsumption(new CapabilityConsumption.State(
            consumedRows.Sum(row => row.AccessCount),
            consumedRows.Sum(row => row.CompletionCount),
            consumedRows.Sum(row => row.SkipCount),
            TimeSpan.FromSeconds(consumedRows.Sum(row => row.ActiveSeconds)),
            TimeSpan.FromSeconds(direct?.ResumeSeconds ?? 0),
            consumedRows.Max(row => row.LastAccessedAt),
            consumedRows.Max(row => row.LastActiveAt),
            completedAt)));
    }

    public async Task PersistAsync(Entity entity, CancellationToken cancellationToken) {
        var userId = currentUser.UserId;
        if (userId == Guid.Empty || entity.ConsumptionCapability is not { Value: { } consumption }) {
            return;
        }

        if (IsEmpty(consumption)) {
            return;
        }

        var row = await UserEntityStateColumns.GetOrAddAsync(db, userId, entity.Id, cancellationToken);
        row.AccessCount = consumption.AccessCount;
        row.CompletionCount = consumption.CompletionCount;
        row.SkipCount = consumption.SkipCount;
        row.ActiveSeconds = consumption.ActiveDuration.TotalSeconds;
        row.ResumeSeconds = consumption.ResumeTime.TotalSeconds;
        row.LastAccessedAt = consumption.LastAccessedAt;
        row.LastActiveAt = consumption.LastActiveAt;
        row.CompletedAt = consumption.CompletedAt;
        row.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static bool IsEmpty(CapabilityConsumption.State consumption) =>
        consumption.AccessCount == 0 &&
        consumption.CompletionCount == 0 &&
        consumption.SkipCount == 0 &&
        consumption.ActiveDuration == TimeSpan.Zero &&
        consumption.ResumeTime == TimeSpan.Zero &&
        consumption.LastAccessedAt is null &&
        consumption.LastActiveAt is null &&
        consumption.CompletedAt is null;

    private async Task<IReadOnlyList<Guid>> LoadRollupLeafIdsAsync(
        Guid rootId,
        CancellationToken cancellationToken) {
        const int maximumDepth = 32;
        var descendants = new List<(Guid Id, Guid? ParentId, string KindCode)>();
        var parents = new[] { rootId };
        for (var depth = 0; parents.Length > 0 && depth < maximumDepth; depth++) {
            var rows = await db.Entities.AsNoTracking()
                .Where(row => row.ParentEntityId != null && parents.Contains(row.ParentEntityId.Value))
                .Select(row => new { row.Id, row.ParentEntityId, row.KindCode })
                .ToArrayAsync(cancellationToken);
            descendants.AddRange(rows.Select(row => (row.Id, row.ParentEntityId, row.KindCode)));
            parents = rows.Select(row => row.Id).ToArray();
        }

        var consumable = descendants
            .Where(row => EntityKindRegistry.Describe(EntityKindRegistry.Require(row.KindCode))
                .SupportsDefaultCapability<CapabilityConsumption>())
            .ToArray();
        var consumableParents = consumable
            .Where(row => row.ParentId is not null)
            .Select(row => row.ParentId!.Value)
            .ToHashSet();
        return consumable
            .Where(row => !consumableParents.Contains(row.Id))
            .Select(row => row.Id)
            .ToArray();
    }
}
