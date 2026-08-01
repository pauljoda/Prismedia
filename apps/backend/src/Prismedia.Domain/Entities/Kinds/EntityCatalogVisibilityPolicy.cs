namespace Prismedia.Domain.Entities;

/// <summary>Catalog surfaces that may independently constrain structural hierarchy visibility.</summary>
[Flags]
public enum EntityCatalogSurface {
    /// <summary>No catalog surface.</summary>
    None = 0,

    /// <summary>Discovery and search-result surfaces.</summary>
    Discovery = 1,

    /// <summary>The kind-specific catalog browse surface.</summary>
    KindBrowse = 2,

    /// <summary>Collection membership and collection-detail surfaces.</summary>
    Collection = 4,

    /// <summary>Aggregate statistics and count surfaces.</summary>
    Statistics = 8
}

/// <summary>Excludes one structural parent relationship from selected catalog surfaces.</summary>
public sealed record EntityCatalogParentExclusion {
    /// <summary>Creates one parent-specific catalog exclusion.</summary>
    /// <param name="parentKind">The structural parent kind that suppresses this child.</param>
    /// <param name="surfaces">The catalog surfaces on which the relationship is suppressed.</param>
    public EntityCatalogParentExclusion(EntityKind parentKind, EntityCatalogSurface surfaces) {
        if (!Enum.IsDefined(parentKind)) {
            throw new ArgumentOutOfRangeException(nameof(parentKind), "Catalog exclusions require a defined Entity kind.");
        }

        EntityCatalogVisibilityPolicy.ValidateSurfaces(surfaces, nameof(surfaces), allowNone: false);
        ParentKind = parentKind;
        Surfaces = surfaces;
    }

    /// <summary>The structural parent kind that suppresses this child.</summary>
    public EntityKind ParentKind { get; }

    /// <summary>The catalog surfaces on which the parent relationship is suppressed.</summary>
    public EntityCatalogSurface Surfaces { get; }
}

/// <summary>
/// Immutable hierarchy visibility contract owned by one entity-kind definition. It distinguishes
/// top-level-only surfaces from parent-specific exclusions so catalog consumers remain declarative.
/// </summary>
public sealed record EntityCatalogVisibilityPolicy {
    private const EntityCatalogSurface AllSurfaces =
        EntityCatalogSurface.Discovery |
        EntityCatalogSurface.KindBrowse |
        EntityCatalogSurface.Collection |
        EntityCatalogSurface.Statistics;

    private readonly IReadOnlyList<EntityCatalogParentExclusion> _parentExclusions;

    /// <summary>Policy with no hierarchy restrictions on any catalog surface.</summary>
    public static EntityCatalogVisibilityPolicy Default { get; } = new();

    /// <summary>Creates one validated catalog hierarchy visibility policy.</summary>
    /// <param name="topLevelOnlySurfaces">Surfaces that include this kind only when it has no parent.</param>
    /// <param name="parentExclusions">Parent-specific exclusions for individual catalog surfaces.</param>
    public EntityCatalogVisibilityPolicy(
        EntityCatalogSurface topLevelOnlySurfaces = EntityCatalogSurface.None,
        IReadOnlyList<EntityCatalogParentExclusion>? parentExclusions = null) {
        ValidateSurfaces(topLevelOnlySurfaces, nameof(topLevelOnlySurfaces), allowNone: true);
        var exclusions = parentExclusions?.ToArray() ?? [];
        if (exclusions.Any(exclusion => exclusion is null)) {
            throw new ArgumentException("Catalog visibility policies cannot contain null parent exclusions.", nameof(parentExclusions));
        }

        foreach (var group in exclusions.GroupBy(exclusion => exclusion.ParentKind)) {
            var covered = EntityCatalogSurface.None;
            foreach (var exclusion in group) {
                if ((covered & exclusion.Surfaces) != EntityCatalogSurface.None) {
                    throw new ArgumentException(
                        "Catalog visibility policies cannot repeat or overlap parent exclusions.",
                        nameof(parentExclusions));
                }

                covered |= exclusion.Surfaces;
            }
        }

        if (exclusions.Any(exclusion => (topLevelOnlySurfaces & exclusion.Surfaces) != EntityCatalogSurface.None)) {
            throw new ArgumentException(
                "A catalog surface cannot be both top-level-only and selectively parent-excluded.",
                nameof(parentExclusions));
        }

        TopLevelOnlySurfaces = topLevelOnlySurfaces;
        _parentExclusions = Array.AsReadOnly(exclusions);
    }

    /// <summary>Surfaces that include this kind only when it has no structural parent.</summary>
    public EntityCatalogSurface TopLevelOnlySurfaces { get; }

    /// <summary>Parent-specific hierarchy exclusions for this kind.</summary>
    public IReadOnlyList<EntityCatalogParentExclusion> ParentExclusions => _parentExclusions;

    /// <summary>Checks whether a surface restricts this kind to top-level entities.</summary>
    /// <param name="surface">One or more catalog surfaces to query.</param>
    /// <returns><see langword="true"/> when every supplied surface requires a top-level entity.</returns>
    public bool RequiresTopLevel(EntityCatalogSurface surface) {
        ValidateSurfaces(surface, nameof(surface), allowNone: false);
        return (TopLevelOnlySurfaces & surface) == surface;
    }

    /// <summary>Checks whether a parent relationship is excluded from every supplied catalog surface.</summary>
    /// <param name="surface">One or more catalog surfaces to query.</param>
    /// <param name="parentKind">The structural parent kind to query.</param>
    /// <returns><see langword="true"/> when the parent relationship is excluded on every supplied surface.</returns>
    public bool ExcludesParent(EntityCatalogSurface surface, EntityKind parentKind) {
        ValidateSurfaces(surface, nameof(surface), allowNone: false);
        if (!Enum.IsDefined(parentKind)) {
            throw new ArgumentOutOfRangeException(nameof(parentKind), "Catalog exclusions require a defined Entity kind.");
        }

        var excludedSurfaces = ParentExclusions
            .Where(exclusion => exclusion.ParentKind == parentKind)
            .Aggregate(EntityCatalogSurface.None, (covered, exclusion) => covered | exclusion.Surfaces);
        return (excludedSurfaces & surface) == surface;
    }

    /// <summary>Validates that this policy can be applied to the supplied structural topology.</summary>
    /// <param name="structurePolicy">The definition-owned structural topology for the same kind.</param>
    public void ValidateFor(EntityStructurePolicy structurePolicy) {
        ArgumentNullException.ThrowIfNull(structurePolicy);
        if (TopLevelOnlySurfaces != EntityCatalogSurface.None && !structurePolicy.AllowsRoot) {
            throw new ArgumentException(
                "Top-level-only catalog visibility requires a structural policy that allows roots.",
                nameof(structurePolicy));
        }

        foreach (var exclusion in ParentExclusions) {
            if (!structurePolicy.AllowsParent(exclusion.ParentKind)) {
                throw new ArgumentException(
                    $"Catalog visibility excludes undeclared structural parent '{exclusion.ParentKind}'.",
                    nameof(structurePolicy));
            }
        }
    }

    internal static void ValidateSurfaces(
        EntityCatalogSurface surfaces,
        string parameterName,
        bool allowNone) {
        if ((surfaces & ~AllSurfaces) != EntityCatalogSurface.None) {
            throw new ArgumentOutOfRangeException(parameterName, "Catalog visibility policies require defined catalog surfaces.");
        }

        if (!allowNone && surfaces == EntityCatalogSurface.None) {
            throw new ArgumentOutOfRangeException(parameterName, "Catalog parent exclusions must select at least one surface.");
        }
    }
}
