using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// Translates definition-owned catalog visibility declarations into cached EF query plans. The
/// same plan is reused by browse, collection, and statistics projections so hierarchy policy is
/// never rebuilt as route-specific kind branches.
/// </summary>
internal static class EntityCatalogQueryPolicy {
    private static readonly IReadOnlyDictionary<EntityCatalogSurface, IReadOnlyList<EntityCatalogQueryPlan>> Plans =
        Enum.GetValues<EntityCatalogSurface>()
            .Where(surface => surface != EntityCatalogSurface.None)
            .ToDictionary(surface => surface, BuildPlans);

    /// <summary>Gets the cached plan for one catalog surface.</summary>
    internal static IReadOnlyList<EntityCatalogQueryPlan> PlansFor(EntityCatalogSurface surface) =>
        Plans.TryGetValue(surface, out var plans)
            ? plans
            : throw new ArgumentOutOfRangeException(nameof(surface), surface, "A single defined catalog surface is required.");

    /// <summary>Gets one cached kind-specific plan for a catalog surface.</summary>
    internal static EntityCatalogQueryPlan PlanFor(EntityCatalogSurface surface, string kindCode) =>
        PlansFor(surface).Single(plan => plan.KindCode.Equals(kindCode, StringComparison.OrdinalIgnoreCase));

    /// <summary>Applies one definition-owned catalog hierarchy plan to an Entity query.</summary>
    internal static IQueryable<EntityRow> Apply(
        IQueryable<EntityRow> query,
        IQueryable<EntityRow> allEntities,
        EntityCatalogSurface surface,
        IReadOnlyCollection<string>? selectedKindCodes = null) {
        var selectedKinds = selectedKindCodes is null
            ? null
            : selectedKindCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in PlansFor(surface).Where(plan =>
                     selectedKinds is null || selectedKinds.Contains(plan.KindCode))) {
            if (plan.RequiresTopLevel) {
                var kindCode = plan.KindCode;
                query = query.Where(entity => entity.KindCode != kindCode || entity.ParentEntityId == null);
            }

            if (plan.HiddenParentKindCodes.Count > 0) {
                var kindCode = plan.KindCode;
                var hiddenParentKindCodes = plan.HiddenParentKindCodes;
                query = query.Where(entity =>
                    entity.KindCode != kindCode ||
                    entity.ParentEntityId == null ||
                    !allEntities.Any(parent =>
                        parent.Id == entity.ParentEntityId &&
                        hiddenParentKindCodes.Contains(parent.KindCode)));
            }
        }

        return query;
    }

    private static IReadOnlyList<EntityCatalogQueryPlan> BuildPlans(EntityCatalogSurface surface) =>
        EntityKindRegistry.All
            .Select(definition => new EntityCatalogQueryPlan(
                definition.Code,
                definition.CatalogVisibility.RequiresTopLevel(surface),
                Array.AsReadOnly(definition.CatalogVisibility.ParentExclusions
                    .Where(exclusion => (exclusion.Surfaces & surface) != EntityCatalogSurface.None)
                    .Select(exclusion => EntityKindRegistry.Describe(exclusion.ParentKind).Code)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray())))
            .ToArray();
}

/// <summary>One cached query shape for an Entity kind on a catalog surface.</summary>
internal sealed record EntityCatalogQueryPlan(
    string KindCode,
    bool RequiresTopLevel,
    IReadOnlyList<string> HiddenParentKindCodes);
