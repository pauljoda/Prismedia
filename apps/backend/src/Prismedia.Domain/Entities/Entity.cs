using Prismedia.Domain.Capabilities;

namespace Prismedia.Domain.Entities;

/// <summary>
/// Abstract root for anything Prismedia can display, organize, rate, tag, or relate to other objects.
/// </summary>
public abstract class Entity {
    private readonly List<EntityCapability> _capabilities = [];
    private readonly List<EntityLink> _links = [];
    private readonly List<EntityUrl> _urls = [];
    private readonly List<EntityExternalId> _externalIds = [];
    private readonly List<EntityFile> _entityFiles = [];

    /// <summary>
    /// Creates an entity with optional capabilities, child relationships, and non-structural relationships.
    /// </summary>
    /// <param name="id">Stable entity identifier.</param>
    /// <param name="title">Primary user-facing title.</param>
    /// <param name="capabilities">Mutable behavior modules to attach to this entity.</param>
    /// <param name="children">Structural child relationships.</param>
    /// <param name="relationships">Non-structural relationships.</param>
    /// <param name="parentEntityId">Optional structural parent identifier.</param>
    /// <param name="sortOrder">Optional structural order under the parent.</param>
    protected Entity(
        Guid id,
        string title,
        IEnumerable<EntityCapability>? capabilities = null,
        IEnumerable<Entity>? children = null,
        IEnumerable<Entity>? relationships = null,
        Guid? parentEntityId = null,
        int? sortOrder = null) {
        Id = id;
        Title = string.IsNullOrWhiteSpace(title)
            ? throw new ArgumentException("Entity title cannot be empty.", nameof(title))
            : title;
        ParentEntityId = parentEntityId;
        SortOrder = sortOrder;

        foreach (var capability in capabilities ?? CreateDefaultCapabilities()) {
            AddCapability(capability);
        }

        foreach (var child in children ?? []) {
            AddChild(child);
        }

        foreach (var relationship in relationships ?? []) {
            AddRelationship(relationship);
        }
    }

    /// <summary>Stable entity identifier.</summary>
    public Guid Id { get; }

    /// <summary>Primary user-facing title.</summary>
    public string Title { get; private set; }

    /// <summary>Closed domain kind for this concrete entity.</summary>
    public abstract EntityKind Kind { get; }

    /// <summary>Structural parent entity identifier when this entity is owned by another entity.</summary>
    public Guid? ParentEntityId { get; private set; }

    /// <summary>Optional structural order under the parent entity.</summary>
    public int? SortOrder { get; private set; }

    /// <summary>Attached capabilities in insertion order.</summary>
    public IReadOnlyList<EntityCapability> Capabilities => _capabilities;

    /// <summary>Structural child entities grouped by their concrete entity kind.</summary>
    public IReadOnlyDictionary<EntityKind, IReadOnlyList<Entity>> ChildrenByKind => GroupByKind(structural: true);

    /// <summary>Structural child entities in insertion order.</summary>
    public IReadOnlyList<Entity> ChildEntities =>
        _links.Where(link => link.Structural).Select(link => link.Entity).ToArray();

    /// <summary>Non-structural related entities grouped by their concrete entity kind.</summary>
    public IReadOnlyDictionary<EntityKind, IReadOnlyList<Entity>> RelationshipsByKind => GroupByKind(structural: false);

    /// <summary>Non-structural related entities in insertion order within each kind group.</summary>
    public IReadOnlyList<Entity> Relationships =>
        _links.Where(link => !link.Structural).Select(link => link.Entity).ToArray();

    // ── Rating ──────────────────────────────────────────────────────────

    /// <summary>Current normalized rating value (0-5), or null when unrated.</summary>
    public int? RatingValue { get; private set; }

    /// <summary>Sets the rating value, clamped to the 0-5 scale.</summary>
    public void Rate(int value) => RatingValue = Math.Clamp(value, 0, 5);

    /// <summary>Clears the current rating.</summary>
    public void ClearRating() => RatingValue = null;

    // ── Flags ──────────────────────────────────────────────────────────

    /// <summary>Favorite flag when set.</summary>
    public bool? IsFavorite { get; private set; }

    /// <summary>NSFW flag when set.</summary>
    public bool? IsNsfw { get; private set; }

    /// <summary>Organized/reviewed flag when set.</summary>
    public bool? IsOrganized { get; private set; }

    /// <summary>
    /// Wanted-placeholder flag when set: the entity was created by a request with metadata but no
    /// file yet; the acquisition import attaches the file and clears it.
    /// </summary>
    public bool? IsWanted { get; private set; }

    /// <summary>Applies a sparse flag update. Null arguments leave the corresponding flag unchanged.</summary>
    public void PatchFlags(bool? isFavorite, bool? isNsfw, bool? isOrganized) {
        if (isFavorite.HasValue) IsFavorite = isFavorite;
        if (isNsfw.HasValue) IsNsfw = isNsfw;
        if (isOrganized.HasValue) IsOrganized = isOrganized;
    }

    // ── Links ──────────────────────────────────────────────────────────

    /// <summary>User-visible URLs in insertion order.</summary>
    public IReadOnlyList<EntityUrl> Urls => _urls;

    /// <summary>Provider identities in insertion order.</summary>
    public IReadOnlyList<EntityExternalId> ExternalIds => _externalIds;

    /// <summary>Adds a user-visible URL.</summary>
    public void AddUrl(string value, string? label = null) => _urls.Add(new EntityUrl(value, label));

    /// <summary>Sets a provider identity, replacing any existing identity for the same provider.</summary>
    public void SetExternalId(string provider, string value, string? url = null) {
        _externalIds.RemoveAll(id => string.Equals(id.Provider, provider, StringComparison.Ordinal));
        _externalIds.Add(new EntityExternalId(provider, value, url));
    }

    // ── Files ──────────────────────────────────────────────────────────

    /// <summary>Files attached to this entity in insertion order.</summary>
    public IReadOnlyList<EntityFile> EntityFiles => _entityFiles;

    /// <summary>Attaches a file in the given role.</summary>
    public void AttachFile(EntityFileRole role, string path, string? mimeType = null) =>
        _entityFiles.Add(new EntityFile(role, path, mimeType));

    /// <summary>Removes every file attached in the given role.</summary>
    public bool DetachFileRole(EntityFileRole role) =>
        _entityFiles.RemoveAll(f => f.Role == role) > 0;

    // ── Hydration (infrastructure only) ────────────────────────────────

    /// <summary>Bulk-loads rating/flags/links/files from persistence. Infrastructure use only.</summary>
    public void HydrateUniversalProperties(
        int? ratingValue,
        bool? isFavorite,
        bool? isNsfw,
        bool? isOrganized,
        IEnumerable<EntityUrl>? urls,
        IEnumerable<EntityExternalId>? externalIds,
        IEnumerable<EntityFile>? files,
        bool? isWanted = null) {
        RatingValue = ratingValue;
        IsFavorite = isFavorite;
        IsNsfw = isNsfw;
        IsOrganized = isOrganized;
        IsWanted = isWanted;
        _urls.Clear();
        if (urls is not null) _urls.AddRange(urls);
        _externalIds.Clear();
        if (externalIds is not null) _externalIds.AddRange(externalIds);
        _entityFiles.Clear();
        if (files is not null) _entityFiles.AddRange(files);
    }

    /// <summary>
    /// Restores persisted structural placement when infrastructure hydrates an entity
    /// directly instead of through its parent aggregate.
    /// </summary>
    /// <param name="parentEntityId">Persisted parent entity identifier, if any.</param>
    /// <param name="sortOrder">Persisted order within the parent, if any.</param>
    public void HydrateStructuralPlacement(Guid? parentEntityId, int? sortOrder) {
        ParentEntityId = parentEntityId;
        SortOrder = sortOrder;
    }

    // ── Capabilities (optional behavior modules) ───────────────────────

    /// <summary>Description capability when attached.</summary>
    public CapabilityDescription? Description => GetCapability<CapabilityDescription>();

    /// <summary>Stats capability when attached.</summary>
    public CapabilityStats? Stats => GetCapability<CapabilityStats>();

    /// <summary>Dates capability when attached.</summary>
    public CapabilityDates? Dates => GetCapability<CapabilityDates>();

    /// <summary>Lifetime capability when attached.</summary>
    public CapabilityLifetime? Lifetime => GetCapability<CapabilityLifetime>();

    /// <summary>Technical metadata capability when attached.</summary>
    public CapabilityTechnical? Technical => GetCapability<CapabilityTechnical>();

    /// <summary>Source provenance capability when attached.</summary>
    public CapabilitySource? Source => GetCapability<CapabilitySource>();

    /// <summary>Progress capability when attached.</summary>
    public CapabilityProgress? Progress => GetCapability<CapabilityProgress>();

    /// <summary>Position capability when attached.</summary>
    public CapabilityPosition? Position => GetCapability<CapabilityPosition>();

    /// <summary>Classification capability when attached.</summary>
    public CapabilityClassification? Classification => GetCapability<CapabilityClassification>();

    /// <summary>Credits capability when attached.</summary>
    public CapabilityCredits? Credits => GetCapability<CapabilityCredits>();

    /// <summary>Marker capability when attached.</summary>
    public CapabilityMarkers? MarkerCapability => GetCapability<CapabilityMarkers>();

    /// <summary>Playback capability when attached.</summary>
    public CapabilityPlayback? PlaybackCapability => GetCapability<CapabilityPlayback>();

    /// <summary>Subtitle capability when attached.</summary>
    public CapabilitySubtitles? SubtitleCapability => GetCapability<CapabilitySubtitles>();

    /// <summary>Playback state when playback capability is attached.</summary>
    public CapabilityPlayback.State? Playback => PlaybackCapability?.Value;

    /// <summary>
    /// Updates the title while preserving entity identity.
    /// </summary>
    /// <param name="title">New non-empty title.</param>
    public void Rename(string title) {
        Title = string.IsNullOrWhiteSpace(title)
            ? throw new ArgumentException("Entity title cannot be empty.", nameof(title))
            : title;
    }

    /// <summary>
    /// Creates fresh default capabilities for this concrete entity type.
    /// Implementations must not read derived instance state because this method is called from the base constructor.
    /// </summary>
    /// <returns>Fresh capability instances for a new entity.</returns>
    protected abstract IEnumerable<EntityCapability> CreateDefaultCapabilities();

    /// <summary>
    /// Gets an attached capability by concrete type.
    /// </summary>
    /// <typeparam name="TCapability">Concrete capability type to retrieve.</typeparam>
    /// <returns>The attached capability instance, or null when missing.</returns>
    public TCapability? GetCapability<TCapability>()
        where TCapability : EntityCapability =>
        _capabilities.OfType<TCapability>().SingleOrDefault();

    /// <summary>
    /// Gets an attached capability by concrete type or throws when missing.
    /// </summary>
    /// <typeparam name="TCapability">Concrete capability type to retrieve.</typeparam>
    /// <returns>The attached capability instance.</returns>
    public TCapability RequireCapability<TCapability>()
        where TCapability : EntityCapability =>
        GetCapability<TCapability>()
            ?? throw new InvalidOperationException($"Entity '{Id}' does not have capability '{typeof(TCapability).Name}'.");

    /// <summary>
    /// Checks whether a concrete capability type is attached.
    /// </summary>
    /// <typeparam name="TCapability">Concrete capability type to check.</typeparam>
    /// <returns>True when the capability is attached; otherwise false.</returns>
    public bool HasCapability<TCapability>()
        where TCapability : EntityCapability =>
        GetCapability<TCapability>() is not null;

    /// <summary>
    /// Attaches a capability instance to this entity.
    /// </summary>
    /// <param name="capability">Capability instance to attach.</param>
    /// <exception cref="ArgumentException">Thrown when this entity already has a capability of the same concrete type.</exception>
    public void AddCapability(EntityCapability capability) {
        ArgumentNullException.ThrowIfNull(capability);
        if (_capabilities.Any(existing => existing.GetType() == capability.GetType())) {
            throw new ArgumentException($"Entity '{Id}' already has capability {capability.GetType().Name}.", nameof(capability));
        }

        _capabilities.Add(capability);
    }

    /// <summary>
    /// Removes a capability by concrete type and detaches it from this entity.
    /// </summary>
    /// <typeparam name="TCapability">Concrete capability type to remove.</typeparam>
    /// <returns>True when a capability was removed; otherwise false.</returns>
    public bool RemoveCapability<TCapability>()
        where TCapability : EntityCapability {
        var capability = GetCapability<TCapability>();
        if (capability is null) {
            return false;
        }

        return _capabilities.Remove(capability);
    }

    /// <summary>
    /// Gets the attached capability of the requested type, attaching a fresh default
    /// instance first when it is missing.
    /// </summary>
    /// <typeparam name="TCapability">Capability type to get or attach.</typeparam>
    /// <returns>The existing or newly attached capability.</returns>
    public TCapability GetOrAddCapability<TCapability>(Func<TCapability> factory)
        where TCapability : EntityCapability {
        ArgumentNullException.ThrowIfNull(factory);
        var existing = GetCapability<TCapability>();
        if (existing is not null) {
            return existing;
        }

        var created = factory();
        AddCapability(created);
        return created;
    }

    /// <summary>
    /// Adds a structural child relationship.
    /// </summary>
    /// <param name="child">Child entity.</param>
    /// <param name="sortOrder">Optional child order.</param>
    public void AddChild(Entity child, int? sortOrder = null) {
        ArgumentNullException.ThrowIfNull(child);
        if (_links.Any(link => link.Structural && link.Entity.Id == child.Id)) {
            throw new ArgumentException($"Entity '{Id}' already has child '{child.Id}'.", nameof(child));
        }

        child.ParentEntityId = Id;
        child.SortOrder = sortOrder;
        _links.Add(new EntityLink(child, Structural: true));
    }

    /// <summary>
    /// Gets structural children by concrete entity type.
    /// </summary>
    /// <typeparam name="TEntity">Concrete child type to retrieve.</typeparam>
    /// <returns>Matching child entities in insertion order.</returns>
    public IReadOnlyList<TEntity> ChildrenOf<TEntity>()
        where TEntity : Entity =>
        ChildrenOf(EntityKindRegistry.RequireType(typeof(TEntity))).OfType<TEntity>().ToArray();

    /// <summary>
    /// Gets structural children by entity kind.
    /// </summary>
    /// <param name="kind">Entity kind to retrieve.</param>
    /// <returns>Matching child entities in insertion order.</returns>
    public IReadOnlyList<Entity> ChildrenOf(EntityKind kind) =>
        _links.Where(link => link.Structural && link.Entity.Kind == kind)
            .Select(link => link.Entity)
            .ToArray();

    /// <summary>
    /// Adds a non-structural relationship.
    /// </summary>
    /// <param name="entity">Related entity.</param>
    public void AddRelationship(Entity entity) {
        ArgumentNullException.ThrowIfNull(entity);
        if (_links.Any(link => !link.Structural && link.Entity.Id == entity.Id)) {
            throw new ArgumentException($"Entity '{Id}' already has relationship '{entity.Id}'.", nameof(entity));
        }

        _links.Add(new EntityLink(entity, Structural: false));
    }

    /// <summary>
    /// Gets non-structural relationships by concrete entity type.
    /// </summary>
    /// <typeparam name="TEntity">Concrete relationship type to retrieve.</typeparam>
    /// <returns>Matching related entities in insertion order.</returns>
    public IReadOnlyList<TEntity> RelationshipsOf<TEntity>()
        where TEntity : Entity =>
        RelationshipsOf(EntityKindRegistry.RequireType(typeof(TEntity))).OfType<TEntity>().ToArray();

    /// <summary>
    /// Gets non-structural relationships by entity kind.
    /// </summary>
    /// <param name="kind">Entity kind to retrieve.</param>
    /// <returns>Matching related entities in insertion order.</returns>
    public IReadOnlyList<Entity> RelationshipsOf(EntityKind kind) =>
        _links.Where(link => !link.Structural && link.Entity.Kind == kind)
            .Select(link => link.Entity)
            .ToArray();

    private IReadOnlyDictionary<EntityKind, IReadOnlyList<Entity>> GroupByKind(bool structural) =>
        _links.Where(link => link.Structural == structural)
            .GroupBy(link => link.Entity.Kind)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<Entity>)group.Select(link => link.Entity).ToArray());

    private readonly record struct EntityLink(Entity Entity, bool Structural);
}
