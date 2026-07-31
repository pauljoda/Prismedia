namespace Prismedia.Domain.Entities;

/// <summary>
/// Immutable list/browse visibility contract owned by one Entity kind. Persistence adapters use
/// these facts to compose provider-specific queries without maintaining parallel kind lists.
/// </summary>
public sealed record EntityBrowsePolicy {
    /// <summary>Policy for a kind with ordinary list and hierarchy visibility.</summary>
    public static EntityBrowsePolicy Default { get; } = new();

    private readonly IReadOnlyList<EntityKind> _hiddenParentKinds;
    private readonly IReadOnlyList<EntityKind> _aggregateParentKinds;

    /// <summary>Creates one validated browse policy.</summary>
    /// <param name="excludesWantedByDefault">
    /// Whether wanted placeholders of this kind are omitted unless wanted state is requested.
    /// </param>
    /// <param name="requiresTopLevel">
    /// Whether a kind-specific browse shows only entities without structural parents.
    /// </param>
    /// <param name="hiddenParentKinds">
    /// Parent kinds that hide this kind from its kind-specific browse while preserving other parents.
    /// </param>
    /// <param name="aggregateParentKinds">
    /// Parent kinds whose child representation is suppressed from aggregate/search results.
    /// </param>
    public EntityBrowsePolicy(
        bool excludesWantedByDefault = false,
        bool requiresTopLevel = false,
        IReadOnlyList<EntityKind>? hiddenParentKinds = null,
        IReadOnlyList<EntityKind>? aggregateParentKinds = null) {
        var hiddenParents = ValidateKinds(hiddenParentKinds, nameof(hiddenParentKinds));
        var aggregateParents = ValidateKinds(aggregateParentKinds, nameof(aggregateParentKinds));
        if (requiresTopLevel && hiddenParents.Length > 0) {
            throw new ArgumentException(
                "A top-level-only browse cannot also declare selectively hidden parent kinds.",
                nameof(hiddenParentKinds));
        }

        ExcludesWantedByDefault = excludesWantedByDefault;
        RequiresTopLevel = requiresTopLevel;
        _hiddenParentKinds = Array.AsReadOnly(hiddenParents);
        _aggregateParentKinds = Array.AsReadOnly(aggregateParents);
    }

    /// <summary>Whether wanted placeholders are omitted unless wanted state is requested.</summary>
    public bool ExcludesWantedByDefault { get; }

    /// <summary>Whether kind-specific browsing includes only entities without a parent.</summary>
    public bool RequiresTopLevel { get; }

    /// <summary>Parent kinds that hide this kind from its kind-specific browse.</summary>
    public IReadOnlyList<EntityKind> HiddenParentKinds => _hiddenParentKinds;

    /// <summary>Parent kinds whose child representation is omitted from aggregate/search results.</summary>
    public IReadOnlyList<EntityKind> AggregateParentKinds => _aggregateParentKinds;

    private static EntityKind[] ValidateKinds(
        IReadOnlyList<EntityKind>? kinds,
        string parameterName) {
        var values = kinds?.ToArray() ?? [];
        if (values.Any(kind => !Enum.IsDefined(kind))) {
            throw new ArgumentOutOfRangeException(parameterName, "Browse policies require defined Entity kinds.");
        }

        if (values.Distinct().Count() != values.Length) {
            throw new ArgumentException("A browse policy cannot repeat parent kinds.", parameterName);
        }

        return values;
    }
}
