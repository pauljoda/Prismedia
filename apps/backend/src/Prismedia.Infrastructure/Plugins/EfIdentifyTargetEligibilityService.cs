using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>EF Core projection of the shared source-backed-subtree Identify eligibility rule.</summary>
public sealed class EfIdentifyTargetEligibilityService(
    PrismediaDbContext db,
    IEntitySourceOwnershipReader? sourceOwnership = null)
    : IIdentifyTargetEligibilityService {
    private readonly IEntitySourceOwnershipReader _sourceOwnership =
        sourceOwnership ?? new EfEntitySourceOwnershipProjection(db);

    /// <inheritdoc />
    public async Task<IdentifyTargetEligibility> EvaluateAsync(
        Guid entityId,
        CancellationToken cancellationToken) {
        var results = await EvaluateManyAsync([entityId], cancellationToken);
        return results[entityId];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, IdentifyTargetEligibility>> EvaluateManyAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken) {
        var ids = entityIds.Distinct().ToArray();
        if (ids.Length == 0) {
            return new Dictionary<Guid, IdentifyTargetEligibility>();
        }

        var rows = await db.Entities
            .AsNoTracking()
            .Where(entity => ids.Contains(entity.Id))
            .Select(entity => new { entity.Id, entity.IsWanted })
            .ToDictionaryAsync(entity => entity.Id, cancellationToken);
        var sourceBackedIds = await _sourceOwnership.ResolveAsync(ids, cancellationToken);

        return ids.ToDictionary(
            id => id,
            id => rows.TryGetValue(id, out var row)
                ? new IdentifyTargetEligibility(
                    id,
                    row.IsWanted
                        ? IdentifyTargetEligibilityStatus.Wanted
                        : sourceBackedIds.Contains(id)
                            ? IdentifyTargetEligibilityStatus.Eligible
                            : IdentifyTargetEligibilityStatus.NoSourceMedia)
                : new IdentifyTargetEligibility(id, IdentifyTargetEligibilityStatus.Missing));
    }
}
