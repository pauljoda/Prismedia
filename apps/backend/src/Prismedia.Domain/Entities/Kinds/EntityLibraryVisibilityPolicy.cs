namespace Prismedia.Domain.Entities;

/// <summary>How an Entity kind derives visibility from enabled library roots.</summary>
public enum EntityLibraryVisibilityMode {
    /// <summary>The kind has no dedicated library-root topology.</summary>
    Unscoped,

    /// <summary>The Entity row has kind-specific persistence that directly owns a library root.</summary>
    DirectRoot,

    /// <summary>The Entity inherits library visibility from a structural ancestor.</summary>
    AncestorRoot,

    /// <summary>The Entity is visible when at least one bounded descendant is in an enabled root.</summary>
    DescendantRoot
}

/// <summary>
/// Immutable library-root topology owned by one Entity kind. Infrastructure remains responsible
/// for translating the topology into provider-specific queries.
/// </summary>
public sealed record EntityLibraryVisibilityPolicy {
    private const int MaximumSupportedDepth = 3;

    /// <summary>Policy for kinds without dedicated library-root topology.</summary>
    public static EntityLibraryVisibilityPolicy Unscoped { get; } =
        new(EntityLibraryVisibilityMode.Unscoped);

    /// <summary>Policy for kinds whose own persistence row carries library-root ownership.</summary>
    public static EntityLibraryVisibilityPolicy DirectRoot { get; } =
        new(EntityLibraryVisibilityMode.DirectRoot);

    /// <summary>Policy for structural kinds that inherit root ownership from an ancestor.</summary>
    public static EntityLibraryVisibilityPolicy AncestorRoot { get; } =
        new(EntityLibraryVisibilityMode.AncestorRoot);

    private EntityLibraryVisibilityPolicy(
        EntityLibraryVisibilityMode mode,
        EntityKind? descendantKind = null,
        int maximumDepth = 0) {
        Mode = mode;
        DescendantKind = descendantKind;
        MaximumDepth = maximumDepth;
    }

    /// <summary>Creates a policy whose root ownership comes from bounded descendants.</summary>
    /// <param name="descendantKind">Root-owning descendant kind.</param>
    /// <param name="maximumDepth">Maximum structural distance to the descendant.</param>
    public static EntityLibraryVisibilityPolicy FromDescendants(
        EntityKind descendantKind,
        int maximumDepth) {
        if (!Enum.IsDefined(descendantKind)) {
            throw new ArgumentOutOfRangeException(nameof(descendantKind), descendantKind, "Undefined Entity kind.");
        }

        if (maximumDepth is < 1 or > MaximumSupportedDepth) {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDepth),
                maximumDepth,
                $"Library descendant depth must be between 1 and {MaximumSupportedDepth}.");
        }

        return new EntityLibraryVisibilityPolicy(
            EntityLibraryVisibilityMode.DescendantRoot,
            descendantKind,
            maximumDepth);
    }

    /// <summary>Library-root relationship used by the kind.</summary>
    public EntityLibraryVisibilityMode Mode { get; }

    /// <summary>Root-owning descendant kind when <see cref="Mode"/> is descendant-based.</summary>
    public EntityKind? DescendantKind { get; }

    /// <summary>Maximum descendant depth when <see cref="Mode"/> is descendant-based.</summary>
    public int MaximumDepth { get; }
}
