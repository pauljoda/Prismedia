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
    private readonly Func<IReadOnlyList<EntityCapability>> _defaultCapabilities;

    /// <summary>Creates one immutable kind definition.</summary>
    protected EntityKindDefinition(
        EntityKind kind,
        string code,
        string displayName,
        string groupLabel,
        EntityKindCategory category,
        EntityStorageShape storageShape,
        Type? clrType = null,
        Func<IReadOnlyList<EntityCapability>>? defaultCapabilities = null,
        bool enumeratesIdentifyChildren = false,
        bool supportsFileDeletion = false) {
        Kind = kind;
        Code = RequireText(code, nameof(code));
        DisplayName = RequireText(displayName, nameof(displayName));
        GroupLabel = RequireText(groupLabel, nameof(groupLabel));
        Category = category;
        StorageShape = storageShape;
        ClrType = clrType;
        _defaultCapabilities = defaultCapabilities ?? EmptyCapabilities;
        EnumeratesIdentifyChildren = enumeratesIdentifyChildren;
        SupportsFileDeletion = supportsFileDeletion;
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

    /// <summary>Concrete domain entity type, or null for a protocol-only kind.</summary>
    public Type? ClrType { get; }

    /// <summary>Whether identify cascades enumerate this kind's structural children.</summary>
    public bool EnumeratesIdentifyChildren { get; }

    /// <summary>Whether this kind can safely root managed file deletion.</summary>
    public bool SupportsFileDeletion { get; }

    /// <summary>
    /// Request workflow entries owned by this kind. Keeping them beside the kind definition makes
    /// request discovery automatic and permits multiple renditions to target the same Entity kind.
    /// </summary>
    public virtual IReadOnlyList<RequestKindDescriptor> RequestKinds => [];

    /// <summary>
    /// Immutable document-capability types projected directly by this definition. Shared
    /// cross-kind capabilities are projected generically from the Entity root and attached
    /// domain capabilities.
    /// </summary>
    public virtual IReadOnlyList<Type> ProjectedCapabilityTypes => [];

    /// <summary>
    /// Creates fresh default domain capabilities for a newly constructed entity of this kind.
    /// Duplicate capability types are rejected so every capability remains unambiguous.
    /// </summary>
    public IReadOnlyList<EntityCapability> CreateDefaultCapabilities() {
        var capabilities = _defaultCapabilities() ??
            throw new InvalidOperationException($"Entity kind '{Code}' returned a null default-capability collection.");
        var duplicate = capabilities
            .GroupBy(capability => capability.GetType())
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) {
            throw new InvalidOperationException(
                $"Entity kind '{Code}' declares default capability '{duplicate.Key.Name}' more than once.");
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
        Func<IReadOnlyList<EntityCapability>>? defaultCapabilities = null,
        bool enumeratesIdentifyChildren = false,
        bool supportsFileDeletion = false)
        : base(
            kind,
            code,
            displayName,
            groupLabel,
            category,
            storageShape,
            typeof(TEntity),
            defaultCapabilities,
            enumeratesIdentifyChildren,
            supportsFileDeletion) {
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
    IReadOnlySet<EntityKind> ContainableKinds { get; }

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
        Func<EntityRootData, TEntity> factory,
        Func<IReadOnlyList<EntityCapability>>? defaultCapabilities = null,
        bool enumeratesIdentifyChildren = false,
        bool supportsFileDeletion = false)
        : base(
            kind,
            code,
            displayName,
            groupLabel,
            category,
            storageShape,
            defaultCapabilities,
            enumeratesIdentifyChildren,
            supportsFileDeletion) {
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

/// <summary>Broad category for an entity kind when infrastructure needs seed metadata.</summary>
public enum EntityKindCategory {
    /// <summary>Playable or viewable media.</summary>
    Media,

    /// <summary>Taxonomy or organization entity.</summary>
    Taxonomy,

    /// <summary>User-curated grouping entity.</summary>
    Collection
}
