namespace Prismedia.Domain.Entities;

/// <summary>
/// Definition-owned declaration of one kind's permitted structural placement. The policy is
/// validated at definition discovery but is not yet enforced by <see cref="Entity.AddChild"/>
/// so legacy persisted structures can be migrated independently.
/// </summary>
public sealed record EntityStructurePolicy {
    /// <summary>Default for kinds whose topology has not yet been declared.</summary>
    public static EntityStructurePolicy Unspecified { get; } = new(isDeclared: false, requiresParent: false, [], []);

    /// <summary>Creates one explicit structure declaration.</summary>
    public EntityStructurePolicy(
        bool requiresParent,
        IReadOnlyList<EntityKind> allowedParentKinds,
        IReadOnlyList<EntityKind> allowedChildKinds)
        : this(isDeclared: true, requiresParent, allowedParentKinds, allowedChildKinds) {
    }

    private EntityStructurePolicy(
        bool isDeclared,
        bool requiresParent,
        IReadOnlyList<EntityKind> allowedParentKinds,
        IReadOnlyList<EntityKind> allowedChildKinds) {
        IsDeclared = isDeclared;
        RequiresParent = requiresParent;
        AllowedParentKinds = RequireDistinct(allowedParentKinds, nameof(allowedParentKinds));
        AllowedChildKinds = RequireDistinct(allowedChildKinds, nameof(allowedChildKinds));
        if (requiresParent && AllowedParentKinds.Count == 0) {
            throw new ArgumentException("A required-parent policy must name at least one allowed parent.", nameof(allowedParentKinds));
        }

        if (!requiresParent && AllowedParentKinds.Count > 0) {
            throw new ArgumentException("Optional-parent policies are not supported; either require a parent or declare a root kind.", nameof(requiresParent));
        }
    }

    /// <summary>Whether this definition has opted into explicit graph validation.</summary>
    public bool IsDeclared { get; }

    /// <summary>Whether entities of this kind must have one structural parent.</summary>
    public bool RequiresParent { get; }

    /// <summary>Direct parent kinds permitted when <see cref="RequiresParent"/> is true.</summary>
    public IReadOnlyList<EntityKind> AllowedParentKinds { get; }

    /// <summary>Direct child kinds permitted below this kind.</summary>
    public IReadOnlyList<EntityKind> AllowedChildKinds { get; }

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
