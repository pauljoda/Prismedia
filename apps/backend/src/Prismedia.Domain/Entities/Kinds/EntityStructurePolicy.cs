namespace Prismedia.Domain.Entities;

/// <summary>
/// Definition-owned declaration of the structural placements accepted by one entity kind. Parent
/// declarations are the single source of truth; the registry derives the inverse child view.
/// </summary>
public sealed record EntityStructurePolicy {
    private EntityStructurePolicy(bool allowsRoot, IReadOnlyList<EntityKind> allowedParentKinds) {
        AllowsRoot = allowsRoot;
        AllowedParentKinds = RequireDistinct(allowedParentKinds, nameof(allowedParentKinds));
    }

    /// <summary>Policy for kinds that can only appear as top-level roots.</summary>
    public static EntityStructurePolicy RootOnly { get; } = new(allowsRoot: true, []);

    /// <summary>Creates a policy for a kind that can be either a root or a child of the supplied kinds.</summary>
    public static EntityStructurePolicy RootOrChildOf(params EntityKind[] parentKinds) =>
        new(allowsRoot: true, RequireParents(parentKinds));

    /// <summary>Creates a policy for a kind that must be a child of one of the supplied kinds.</summary>
    public static EntityStructurePolicy ChildOf(params EntityKind[] parentKinds) =>
        new(allowsRoot: false, RequireParents(parentKinds));

    /// <summary>Whether an entity of this kind may exist without a structural parent.</summary>
    public bool AllowsRoot { get; }

    /// <summary>Whether entities of this kind must have one structural parent.</summary>
    public bool RequiresParent => !AllowsRoot;

    /// <summary>Direct parent kinds permitted when this entity is structurally nested.</summary>
    public IReadOnlyList<EntityKind> AllowedParentKinds { get; }

    /// <summary>Whether the supplied kind can be this entity's direct structural parent.</summary>
    public bool AllowsParent(EntityKind kind) => AllowedParentKinds.Contains(kind);

    private static IReadOnlyList<EntityKind> RequireParents(IReadOnlyList<EntityKind>? kinds) {
        ArgumentNullException.ThrowIfNull(kinds);
        if (kinds.Count == 0) {
            throw new ArgumentException("A child placement policy must name at least one allowed parent.", nameof(kinds));
        }

        return kinds;
    }

    private static IReadOnlyList<EntityKind> RequireDistinct(
        IReadOnlyList<EntityKind>? kinds,
        string parameterName) {
        ArgumentNullException.ThrowIfNull(kinds, parameterName);
        if (kinds.Count != kinds.Distinct().Count()) {
            throw new ArgumentException("Entity structure kinds cannot contain duplicates.", parameterName);
        }

        return Array.AsReadOnly(kinds.ToArray());
    }
}
