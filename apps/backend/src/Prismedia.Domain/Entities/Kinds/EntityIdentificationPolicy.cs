namespace Prismedia.Domain.Entities;

/// <summary>
/// Immutable identification contract owned by one Entity kind. It describes structural proposal
/// expansion, automatic-identification behavior, and compatible plugin fallback without coupling
/// the Domain to provider instances or runtime orchestration.
/// </summary>
public sealed record EntityIdentificationPolicy {
    /// <summary>Policy for a kind with no identification-specific behavior.</summary>
    public static EntityIdentificationPolicy None { get; } = new();

    /// <summary>Creates one validated identification policy.</summary>
    /// <param name="autoIdentifySelector">User-facing automatic-identification selector family.</param>
    /// <param name="enumeratesChildren">Whether identify cascades enumerate structural children.</param>
    /// <param name="pluginFallbackKind">Compatible kind offered to plugins that omit this concrete kind.</param>
    /// <param name="allowsParentedAutoIdentifyRoot">Whether a parented entity remains an independent automatic-identification root.</param>
    /// <param name="cascadeChildrenAutomatically">Whether automatic identification asks the provider to cascade into structural children.</param>
    /// <param name="usesParentExternalIdentityContext">Whether automatic identification supplies the structural parent's external identities.</param>
    /// <param name="allowsDirectReconcileChildTarget">Whether direct reconciliation may automatically identify a parented target.</param>
    /// <param name="stopsDescendantAutoIdentifyRootTraversal">
    /// Whether descendants stop at the child immediately below this kind when selecting their automatic-identification root.
    /// </param>
    /// <param name="allowsProviderMetadata">
    /// Whether this kind may be a direct or structural target of provider metadata. Scanner-derived
    /// resources can disable this without changing their ordinary Entity or capability behavior.
    /// </param>
    public EntityIdentificationPolicy(
        AutoIdentifySelectorKind? autoIdentifySelector = null,
        bool enumeratesChildren = false,
        EntityKind? pluginFallbackKind = null,
        bool allowsParentedAutoIdentifyRoot = false,
        bool? cascadeChildrenAutomatically = null,
        bool usesParentExternalIdentityContext = false,
        bool allowsDirectReconcileChildTarget = false,
        bool stopsDescendantAutoIdentifyRootTraversal = false,
        bool allowsProviderMetadata = true) {
        if (autoIdentifySelector is null &&
            (allowsParentedAutoIdentifyRoot ||
             cascadeChildrenAutomatically is not null ||
             usesParentExternalIdentityContext ||
             allowsDirectReconcileChildTarget)) {
            throw new ArgumentException(
                "Automatic-identification behavior requires an automatic-identification selector.",
                nameof(autoIdentifySelector));
        }

        if (usesParentExternalIdentityContext && !allowsParentedAutoIdentifyRoot) {
            throw new ArgumentException(
                "Parent identity context requires the kind to remain an automatic-identification root when parented.",
                nameof(usesParentExternalIdentityContext));
        }

        AutoIdentifySelector = autoIdentifySelector;
        EnumeratesChildren = enumeratesChildren;
        PluginFallbackKind = pluginFallbackKind;
        AllowsParentedAutoIdentifyRoot = allowsParentedAutoIdentifyRoot;
        CascadesChildrenAutomatically = autoIdentifySelector is not null &&
            (cascadeChildrenAutomatically ?? true);
        UsesParentExternalIdentityContext = usesParentExternalIdentityContext;
        AllowsDirectReconcileChildTarget = allowsDirectReconcileChildTarget;
        StopsDescendantAutoIdentifyRootTraversal = stopsDescendantAutoIdentifyRootTraversal;
        AllowsProviderMetadata = allowsProviderMetadata;
    }

    /// <summary>User-facing automatic-identification selector family, when supported.</summary>
    public AutoIdentifySelectorKind? AutoIdentifySelector { get; }

    /// <summary>Whether identify cascades enumerate this kind's structural children.</summary>
    public bool EnumeratesChildren { get; }

    /// <summary>Compatible kind offered to plugins that omit this concrete kind.</summary>
    public EntityKind? PluginFallbackKind { get; }

    /// <summary>Whether a parented entity remains an independent automatic-identification root.</summary>
    public bool AllowsParentedAutoIdentifyRoot { get; }

    /// <summary>Whether automatic identification asks the provider to cascade into structural children.</summary>
    public bool CascadesChildrenAutomatically { get; }

    /// <summary>Whether automatic identification supplies the structural parent's external identities.</summary>
    public bool UsesParentExternalIdentityContext { get; }

    /// <summary>Whether direct reconciliation may automatically identify a parented target.</summary>
    public bool AllowsDirectReconcileChildTarget { get; }

    /// <summary>
    /// Whether a descendant's automatic-identification root stops at the entity directly below this kind.
    /// </summary>
    public bool StopsDescendantAutoIdentifyRootTraversal { get; }

    /// <summary>Whether plugins may bind identities or apply metadata to this Entity kind.</summary>
    public bool AllowsProviderMetadata { get; }
}
