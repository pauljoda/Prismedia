using Prismedia.Domain.Capabilities;
using ContractCapability = Prismedia.Contracts.Entities.EntityCapability;

namespace Prismedia.Domain.Entities;

/// <summary>
/// Canonical, discoverable definition of one <see cref="EntityKind"/>. A definition owns the
/// stable database/API code and every domain-level fact shared by consumers of that kind. Concrete
/// definitions may also implement opt-in facet interfaces, allowing new behavior to join through
/// discovery instead of edits to central registration lists.
/// </summary>
public abstract class EntityKindDefinition {
    /// <summary>Shared media-structure ordering for kinds without a specific position policy.</summary>
    public static IReadOnlyList<string> DefaultPositionSortOrderPrecedence { get; } = Array.AsReadOnly([
        EntityPositionCodes.Track,
        EntityPositionCodes.Page,
        EntityPositionCodes.Chapter,
        EntityPositionCodes.Volume,
        EntityPositionCodes.Sort
    ]);
    private readonly Func<IReadOnlyList<EntityCapability>> _defaultCapabilities;
    private readonly IReadOnlyList<Type> _defaultCapabilityTypes;

    /// <summary>Creates one immutable kind definition.</summary>
    protected EntityKindDefinition(
        EntityKind kind,
        string code,
        string displayName,
        string groupLabel,
        EntityKindCategory category,
        EntityStorageShape storageShape,
        EntityKindPresentation presentation,
        EntityKindNavigation? navigation,
        EntityKindSearch? search,
        EntityKindBehavior behavior,
        Type? clrType = null,
        Func<IReadOnlyList<EntityCapability>>? defaultCapabilities = null) {
        Kind = kind;
        Code = RequireText(code, nameof(code));
        DisplayName = RequireText(displayName, nameof(displayName));
        GroupLabel = RequireText(groupLabel, nameof(groupLabel));
        Category = category;
        StorageShape = storageShape;
        Presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        Navigation = navigation;
        Search = search;
        Behavior = behavior ?? throw new ArgumentNullException(nameof(behavior));
        ClrType = clrType;
        _defaultCapabilities = defaultCapabilities ?? EmptyCapabilities;
        var firstCapabilities = ValidateDefaultCapabilities(_defaultCapabilities(), "declared");
        var secondCapabilities = ValidateDefaultCapabilities(_defaultCapabilities(), "declared");
        var firstTypes = firstCapabilities.Select(capability => capability.GetType()).ToArray();
        var secondTypes = secondCapabilities.Select(capability => capability.GetType()).ToArray();
        if (!firstTypes.SequenceEqual(secondTypes)) {
            throw new InvalidOperationException(
                $"Entity kind '{Code}' default capability factory returned inconsistent type sequences.");
        }

        if (firstCapabilities.Any(first => secondCapabilities.Any(second => ReferenceEquals(first, second)))) {
            throw new InvalidOperationException(
                $"Entity kind '{Code}' default capability factory must return fresh instances.");
        }

        _defaultCapabilityTypes = Array.AsReadOnly(firstTypes);
    }

    /// <summary>Typed domain identity represented by this definition.</summary>
    public EntityKind Kind { get; }

    /// <summary>Stable database and API code.</summary>
    public string Code { get; }

    /// <summary>Human-readable singular display name.</summary>
    public string DisplayName { get; }

    /// <summary>Plural display label used when grouping entities by kind.</summary>
    public string GroupLabel { get; }

    /// <summary>Broad category used by metadata rows and shared policies.</summary>
    public EntityKindCategory Category { get; }

    /// <summary>Filesystem storage shape used by scan and organize rules.</summary>
    public EntityStorageShape StorageShape { get; }

    /// <summary>Shared platform-neutral presentation metadata for this kind.</summary>
    public EntityKindPresentation Presentation { get; }

    /// <summary>
    /// Cross-client navigation contract, or null when the kind has no independently reachable UI.
    /// </summary>
    public EntityKindNavigation? Navigation { get; }

    /// <summary>Global-search exposure, or null when the kind is intentionally not searchable.</summary>
    public EntityKindSearch? Search { get; }

    /// <summary>Concrete domain entity type, or null for a protocol-only kind.</summary>
    public Type? ClrType { get; }

    /// <summary>Complete opt-in behavior contract supplied by this definition.</summary>
    public EntityKindBehavior Behavior { get; }

    /// <summary>Identification and provider-compatibility behavior owned by this kind.</summary>
    public EntityIdentificationPolicy Identification => Behavior.Identification;

    /// <summary>
    /// Whether a plugin payload addressed to <paramref name="pluginKind"/> may represent this kind.
    /// Exact kinds always match; definitions may opt into one compatible provider fallback kind.
    /// </summary>
    public bool AcceptsPluginKind(EntityKind pluginKind) =>
        pluginKind == Kind || Identification.PluginFallbackKind == pluginKind;

    /// <summary>Whether this kind can safely root managed file deletion.</summary>
    public bool SupportsFileDeletion => Behavior.SupportsFileDeletion;

    /// <summary>Whether users may create and delete this kind directly through entity routes.</summary>
    public bool SupportsManualManagement => Behavior.SupportsManualManagement;

    /// <summary>Whether an inactive derived shell should be removed after its last child disappears.</summary>
    public bool PrunesWhenEmpty => Behavior.PrunesWhenEmpty;

    /// <summary>Browser upload and reviewed-replacement behavior owned by this kind.</summary>
    public EntityManualAcquisitionPolicy ManualAcquisition => Behavior.ManualAcquisition;

    /// <summary>Derived-media processing behavior owned by this kind.</summary>
    public EntityProcessingPolicy Processing => Behavior.Processing;

    /// <summary>Quality ladder used to rank acquisition releases for this kind.</summary>
    public EntityMediaQualityFamily MediaQualityFamily => Behavior.MediaQualityFamily;

    /// <summary>Whether one owned media file can be replaced atomically during an upgrade.</summary>
    public bool SupportsAtomicMediaUpgrade => Behavior.SupportsAtomicMediaUpgrade;

    /// <summary>Completion/filter behavior shared by persistence and clients.</summary>
    public EntityEngagementPolicy Engagement => Behavior.Engagement;

    /// <summary>List hierarchy and aggregate visibility behavior owned by this kind.</summary>
    public EntityBrowsePolicy Browse => Behavior.Browse;

    /// <summary>Library-root visibility topology owned by this kind.</summary>
    public EntityLibraryVisibilityPolicy LibraryVisibility => Behavior.LibraryVisibility;

    /// <summary>
    /// Declared structural topology for this kind. Policies are declarative in this cutover and
    /// intentionally do not yet constrain legacy relationship hydration or mutation.
    /// </summary>
    public virtual EntityStructurePolicy StructurePolicy => EntityStructurePolicy.Unspecified;

    /// <summary>Concrete domain-capability types supplied for newly constructed entities.</summary>
    public IReadOnlyList<Type> DefaultCapabilityTypes => _defaultCapabilityTypes;

    /// <summary>Whether this kind supplies a default capability of the requested concrete type.</summary>
    /// <typeparam name="TCapability">Concrete domain capability type.</typeparam>
    public bool SupportsDefaultCapability<TCapability>()
        where TCapability : EntityCapability =>
        _defaultCapabilityTypes.Contains(typeof(TCapability));

    /// <summary>
    /// Canonical position-code precedence used to derive a structural sort order. Kinds without
    /// an override use the shared media-structure ordering.
    /// </summary>
    public virtual IReadOnlyList<string> PositionSortOrderPrecedence => DefaultPositionSortOrderPrecedence;

    /// <summary>
    /// Whether metadata relationships applied to this kind belong directly to it. When false,
    /// metadata import scopes relationships to the nearest structural ancestor with this trait.
    /// </summary>
    public virtual bool OwnsMetadataRelationships => false;

    /// <summary>
    /// Request workflow entries owned by this kind. Keeping them beside the kind definition makes
    /// request discovery automatic and permits multiple renditions to target the same Entity kind.
    /// </summary>
    public virtual IReadOnlyList<RequestKindDescriptor> RequestKinds => [];

    /// <summary>
    /// Acquisition-profile policy for this kind, or null when the kind cannot own profiles. The
    /// policy keeps the user-facing profile vocabulary and release/naming behavior beside the
    /// entity kind rather than in application or client-side kind maps.
    /// </summary>
    public virtual AcquisitionProfileDefinition? AcquisitionProfile => null;

    /// <summary>
    /// Immutable document-capability types projected directly by this definition. Shared
    /// cross-kind capabilities are projected generically from the Entity root and attached
    /// domain capabilities.
    /// </summary>
    public virtual IReadOnlyList<Type> ProjectedCapabilityTypes => [];

    /// <summary>
    /// Compact descendant counts shown on this kind's thumbnails. Each metric names the descendant
    /// kind, maximum structural depth, and semantic icon once beside the Entity-kind definition.
    /// </summary>
    public virtual IReadOnlyList<EntityStructuralCountDefinition> StructuralThumbnailCounts => [];

    /// <summary>
    /// Creates fresh default domain capabilities for a newly constructed entity of this kind.
    /// Duplicate capability types are rejected so every capability remains unambiguous.
    /// </summary>
    public IReadOnlyList<EntityCapability> CreateDefaultCapabilities() {
        var capabilities = ValidateDefaultCapabilities(_defaultCapabilities(), "returned");
        var actualTypes = capabilities.Select(capability => capability.GetType()).ToArray();
        if (!_defaultCapabilityTypes.SequenceEqual(actualTypes)) {
            throw new InvalidOperationException(
                $"Entity kind '{Code}' declared [{string.Join(", ", _defaultCapabilityTypes.Select(type => type.Name))}] " +
                $"but returned [{string.Join(", ", actualTypes.Select(type => type.Name))}].");
        }

        return capabilities;
    }

    /// <summary>
    /// Projects the immutable, kind-specific document capabilities owned by this definition.
    /// Declared and emitted capability types must match exactly, making projection completeness a
    /// checked part of the kind contract rather than a separate registration convention.
    /// </summary>
    /// <param name="entity">Concrete entity governed by this definition.</param>
    /// <param name="context">Caller facts permitted in a document projection.</param>
    public IReadOnlyList<ContractCapability> ProjectCapabilities(
        Entity entity,
        EntityKindProjectionContext context) {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(context);
        if (!ReferenceEquals(entity.Definition, this)) {
            throw new ArgumentException(
                $"Entity '{entity.Id}' is governed by '{entity.Definition.Code}', not '{Code}'.",
                nameof(entity));
        }

        var capabilities = ProjectCapabilitiesCore(entity, context) ??
            throw new InvalidOperationException($"Entity kind '{Code}' returned a null document-capability collection.");
        var actualTypes = capabilities.Select(capability => capability.GetType()).ToArray();
        if (!ProjectedCapabilityTypes.SequenceEqual(actualTypes)) {
            throw new InvalidOperationException(
                $"Entity kind '{Code}' declared [{string.Join(", ", ProjectedCapabilityTypes.Select(type => type.Name))}] " +
                $"but projected [{string.Join(", ", actualTypes.Select(type => type.Name))}].");
        }

        return capabilities;
    }

    /// <summary>Implementation hook for a concrete definition's document projection.</summary>
    protected virtual IReadOnlyList<ContractCapability> ProjectCapabilitiesCore(
        Entity entity,
        EntityKindProjectionContext context) => [];

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Entity kind definition text cannot be empty.", parameterName)
            : value.Trim();

    private static IReadOnlyList<EntityCapability> EmptyCapabilities() => [];

    private IReadOnlyList<EntityCapability> ValidateDefaultCapabilities(
        IReadOnlyList<EntityCapability>? capabilities,
        string source) {
        if (capabilities is null) {
            throw new InvalidOperationException($"Entity kind '{Code}' returned a null {source} default-capability collection.");
        }

        if (capabilities.Any(capability => capability is null)) {
            throw new InvalidOperationException($"Entity kind '{Code}' returned a null default capability.");
        }

        var duplicate = capabilities
            .GroupBy(capability => capability.GetType())
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) {
            throw new InvalidOperationException(
                $"Entity kind '{Code}' declares default capability '{duplicate.Key.Name}' more than once.");
        }

        return capabilities;
    }
}

/// <summary>Definition base that binds a kind to exactly one concrete domain entity type.</summary>
/// <typeparam name="TEntity">Concrete domain entity owned by the definition.</typeparam>
public abstract class EntityKindDefinition<TEntity> : EntityKindDefinition
    where TEntity : Entity {
    /// <summary>Creates a typed entity-kind definition.</summary>
    protected EntityKindDefinition(
        EntityKind kind,
        string code,
        string displayName,
        string groupLabel,
        EntityKindCategory category,
        EntityStorageShape storageShape,
        EntityKindPresentation presentation,
        EntityKindNavigation? navigation,
        EntityKindSearch? search,
        EntityKindBehavior behavior,
        Func<IReadOnlyList<EntityCapability>>? defaultCapabilities = null)
        : base(
            kind,
            code,
            displayName,
            groupLabel,
            category,
            storageShape,
            presentation,
            navigation,
            search,
            behavior,
            typeof(TEntity),
            defaultCapabilities) {
    }

    /// <inheritdoc />
    protected sealed override IReadOnlyList<ContractCapability> ProjectCapabilitiesCore(
        Entity entity,
        EntityKindProjectionContext context) =>
        ProjectCapabilities((TEntity)entity, context);

    /// <summary>Projects kind-specific document capabilities from the strongly typed entity.</summary>
    protected virtual IReadOnlyList<ContractCapability> ProjectCapabilities(
        TEntity entity,
        EntityKindProjectionContext context) => [];
}

/// <summary>Caller-scoped facts available to pure Entity-kind document projection.</summary>
/// <param name="CurrentUserId">Current caller identity, or null outside an authenticated request.</param>
public sealed record EntityKindProjectionContext(Guid? CurrentUserId);

/// <summary>
/// Definition-owned thumbnail count for descendants of one kind through a bounded structural depth.
/// </summary>
/// <param name="DescendantKind">Entity kind counted below the thumbnail root.</param>
/// <param name="MaximumDepth">Inclusive parent-link depth to aggregate.</param>
/// <param name="Icon">Stable compact-thumbnail icon code.</param>
public sealed record EntityStructuralCountDefinition {
    /// <summary>Deepest structural path supported by the shared thumbnail aggregate.</summary>
    public const int MaximumSupportedDepth = 3;

    /// <summary>Creates one validated structural thumbnail count.</summary>
    public EntityStructuralCountDefinition(EntityKind descendantKind, int maximumDepth, string icon) {
        if (maximumDepth is <= 0 or > MaximumSupportedDepth) {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDepth),
                $"Structural count depth must be between 1 and {MaximumSupportedDepth}.");
        }

        DescendantKind = descendantKind;
        MaximumDepth = maximumDepth;
        Icon = string.IsNullOrWhiteSpace(icon)
            ? throw new ArgumentException("Structural count icon cannot be empty.", nameof(icon))
            : icon.Trim();
    }

    /// <summary>Entity kind counted below the thumbnail root.</summary>
    public EntityKind DescendantKind { get; }

    /// <summary>Inclusive parent-link depth to aggregate.</summary>
    public int MaximumDepth { get; }

    /// <summary>Stable compact-thumbnail icon code.</summary>
    public string Icon { get; }
}

/// <summary>Validated policy for one Entity kind that owns acquisition profiles.</summary>
/// <param name="Label">User-facing profile label.</param>
/// <param name="DisplayOrder">Stable settings-display order among acquisition profiles.</param>
/// <param name="LibraryRootMediaCapability">Library-root capability required by the profile.</param>
/// <param name="SupportedReleaseDateTypes">Ordered release milestones that may gate automatic search.</param>
/// <param name="DefaultNamingTemplate">Default path template stored for a new profile.</param>
/// <param name="NamingHint">User-facing token and layout guidance for the template.</param>
/// <param name="NamingFamily">Application renderer and validator family for the template.</param>
public sealed record AcquisitionProfileDefinition {
    /// <summary>Validates immutable acquisition-profile policy owned by an Entity kind definition.</summary>
    public AcquisitionProfileDefinition(
        string label,
        int displayOrder,
        LibraryRootMediaCapability libraryRootMediaCapability,
        IReadOnlyList<EntityDateType> supportedReleaseDateTypes,
        string defaultNamingTemplate,
        string namingHint,
        AcquisitionNamingFamily namingFamily) {
        Label = RequireText(label, nameof(label));
        DisplayOrder = displayOrder < 0
            ? throw new ArgumentOutOfRangeException(nameof(displayOrder), "Acquisition profile display order cannot be negative.")
            : displayOrder;
        LibraryRootMediaCapability = libraryRootMediaCapability;
        SupportedReleaseDateTypes = RequireDates(supportedReleaseDateTypes);
        DefaultNamingTemplate = RequireText(defaultNamingTemplate, nameof(defaultNamingTemplate));
        NamingHint = RequireText(namingHint, nameof(namingHint));
        NamingFamily = namingFamily;
    }

    /// <summary>User-facing profile label.</summary>
    public string Label { get; }

    /// <summary>Stable settings-display order among acquisition profiles.</summary>
    public int DisplayOrder { get; }

    /// <summary>Library-root capability required by the profile.</summary>
    public LibraryRootMediaCapability LibraryRootMediaCapability { get; }

    /// <summary>Ordered release milestones that may gate automatic search.</summary>
    public IReadOnlyList<EntityDateType> SupportedReleaseDateTypes { get; }

    /// <summary>Default path template stored for a new profile.</summary>
    public string DefaultNamingTemplate { get; }

    /// <summary>User-facing token and layout guidance for the template.</summary>
    public string NamingHint { get; }

    /// <summary>Application renderer and validator family for the template.</summary>
    public AcquisitionNamingFamily NamingFamily { get; }

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Acquisition profile definition text cannot be empty.", parameterName)
            : value.Trim();

    private static IReadOnlyList<EntityDateType> RequireDates(IReadOnlyList<EntityDateType> value) {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Count == 0) {
            throw new ArgumentException("An acquisition profile must support at least one release date type.", nameof(value));
        }

        if (value.Distinct().Count() != value.Count) {
            throw new ArgumentException("An acquisition profile cannot repeat release date types.", nameof(value));
        }

        return Array.AsReadOnly(value.ToArray());
    }
}

/// <summary>Root fields available when a kind requires no kind-specific persistence data.</summary>
/// <param name="Id">Stable entity identifier.</param>
/// <param name="Title">Primary display title.</param>
/// <param name="ParentEntityId">Optional structural parent identifier.</param>
/// <param name="SortOrder">Optional structural order under the parent.</param>
public sealed record EntityRootData(Guid Id, string Title, Guid? ParentEntityId, int? SortOrder);

/// <summary>
/// Opt-in definition facet for kinds that can be rehydrated from the shared entity root alone.
/// Kinds with additional persisted state instead provide a discovered infrastructure mapper.
/// </summary>
public interface IEntityRootFactory {
    /// <summary>Definition that owns this construction facet.</summary>
    EntityKindDefinition Definition { get; }

    /// <summary>Constructs the concrete entity from shared root fields.</summary>
    Entity Create(EntityRootData root);
}

/// <summary>
/// Optional definition facet for kinds that constrain which entity kinds they may directly contain.
/// </summary>
public interface IEntityContainmentPolicy {
    /// <summary>Entity kinds accepted as direct members.</summary>
    IReadOnlyList<EntityKind> ContainableKinds { get; }

    /// <summary>Returns whether the supplied kind may be contained directly.</summary>
    bool CanContain(EntityKind kind);
}

/// <summary>
/// Definition base for entities whose complete construction requires only shared root fields.
/// </summary>
/// <typeparam name="TEntity">Concrete constructed entity type.</typeparam>
public abstract class RootEntityKindDefinition<TEntity> : EntityKindDefinition<TEntity>, IEntityRootFactory
    where TEntity : Entity {
    private readonly Func<EntityRootData, TEntity> _factory;

    /// <summary>Creates a typed root-constructable definition.</summary>
    protected RootEntityKindDefinition(
        EntityKind kind,
        string code,
        string displayName,
        string groupLabel,
        EntityKindCategory category,
        EntityStorageShape storageShape,
        EntityKindPresentation presentation,
        EntityKindNavigation? navigation,
        EntityKindSearch? search,
        Func<EntityRootData, TEntity> factory,
        EntityKindBehavior behavior,
        Func<IReadOnlyList<EntityCapability>>? defaultCapabilities = null)
        : base(
            kind,
            code,
            displayName,
            groupLabel,
            category,
            storageShape,
            presentation,
            navigation,
            search,
            behavior,
            defaultCapabilities) {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <inheritdoc />
    public EntityKindDefinition Definition => this;

    /// <summary>Constructs the typed entity from shared root fields.</summary>
    public TEntity Create(EntityRootData root) {
        ArgumentNullException.ThrowIfNull(root);
        var entity = _factory(root);
        if (entity.Kind != Kind) {
            throw new InvalidOperationException(
                $"Entity kind '{Code}' constructed '{entity.Kind}' instead of '{Kind}'.");
        }

        return entity;
    }

    Entity IEntityRootFactory.Create(EntityRootData root) => Create(root);
}

/// <summary>Acquisition-quality ladder used to rank releases for an Entity kind.</summary>
public enum EntityMediaQualityFamily {
    /// <summary>The kind does not use the shared audio/video quality ladders.</summary>
    [Code("none")]
    None,

    /// <summary>The kind uses source-and-resolution video quality.</summary>
    [Code("video")]
    Video,

    /// <summary>The kind uses codec-tier audio quality.</summary>
    [Code("audio")]
    Audio
}

/// <summary>Broad category for an entity kind when infrastructure needs seed metadata.</summary>
public enum EntityKindCategory {
    /// <summary>Playable or viewable media.</summary>
    Media,

    /// <summary>Taxonomy or organization entity.</summary>
    Taxonomy,

    /// <summary>User-curated grouping entity.</summary>
    Collection
}
