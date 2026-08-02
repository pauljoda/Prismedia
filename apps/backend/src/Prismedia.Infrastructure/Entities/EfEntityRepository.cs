using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Prismedia.Application.Entities;
using Prismedia.Application.Plugins;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities.Mappers;
using Prismedia.Infrastructure.Entities.Mappers.Capabilities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// EF-backed repository that hydrates domain entities from row storage and persists their
/// mutable state. Implements the Application <see cref="IEntityWriteRepository"/> port so
/// Application services can mutate entities without taking a direct dependency on EF Core.
///
/// All kind-specific construction and per-capability row read/write logic lives in
/// <see cref="IEntityKindMapper"/> and <see cref="IEntityCapabilityMapper"/> implementations
/// discovered via dependency injection; this class is intentionally a coordinator over
/// those mappers and never branches on a concrete entity kind or capability itself.
/// </summary>
public sealed class EfEntityRepository : IEntityWriteRepository {
    private readonly PrismediaDbContext _db;
    private readonly Prismedia.Application.Security.ICurrentUserContext _currentUser;
    private readonly IEntityExternalIdentityStore _externalIdentities;
    private readonly IEntityProviderIdentityStore? _providerIdentities;
    private readonly IPluginIdentityRouter? _identityRouter;
    private readonly IPluginIdentityUrlResolver? _identityUrls;
    private readonly IReadOnlyDictionary<EntityKind, IEntityKindMapper> _kindMappers;
    private readonly IReadOnlyList<IEntityCapabilityMapper> _capabilityMappers;
    private readonly IReadOnlyDictionary<Type, IEntityMutableStateMapper> _mutableStateMappers;
    private readonly EntityStructurePlacementValidator _structurePlacement;

    public EfEntityRepository(
        PrismediaDbContext db,
        Prismedia.Application.Security.ICurrentUserContext currentUser,
        IEnumerable<IEntityKindMapper> kindMappers,
        IEnumerable<IEntityCapabilityMapper> capabilityMappers,
        IEntityExternalIdentityStore externalIdentities)
        : this(
            db,
            currentUser,
            kindMappers,
            capabilityMappers,
            externalIdentities,
            providerIdentities: null,
            identityRouter: null,
            identityUrls: null) { }

    /// <summary>Production constructor with plugin identity binding and URL enrichment.</summary>
    [ActivatorUtilitiesConstructor]
    public EfEntityRepository(
        PrismediaDbContext db,
        Prismedia.Application.Security.ICurrentUserContext currentUser,
        IEnumerable<IEntityKindMapper> kindMappers,
        IEnumerable<IEntityCapabilityMapper> capabilityMappers,
        IEntityExternalIdentityStore externalIdentities,
        IEntityProviderIdentityStore? providerIdentities,
        IPluginIdentityRouter? identityRouter,
        IPluginIdentityUrlResolver? identityUrls) {
        _db = db;
        _currentUser = currentUser;
        _externalIdentities = externalIdentities
            ?? throw new ArgumentNullException(nameof(externalIdentities));
        _providerIdentities = providerIdentities;
        _identityRouter = identityRouter;
        _identityUrls = identityUrls;
        _kindMappers = kindMappers.ToDictionary(mapper => mapper.Kind);
        _capabilityMappers = capabilityMappers.ToArray();
        _mutableStateMappers = _capabilityMappers
            .OfType<IEntityMutableStateMapper>()
            .ToDictionary(mapper => mapper.CapabilityType);
        _structurePlacement = new EntityStructurePlacementValidator(db);
    }

    internal EfEntityRepository(
        PrismediaDbContext db,
        Prismedia.Application.Security.ICurrentUserContext currentUser,
        IEnumerable<IEntityKindMapper> kindMappers,
        IEnumerable<IEntityCapabilityMapper> capabilityMappers)
        : this(
            db,
            currentUser,
            kindMappers,
            capabilityMappers,
            new EfEntityExternalIdentityStore(db, TimeProvider.System)) { }

    /// <inheritdoc />
    public IEntityWriteAttempt BeginAttempt() =>
        new EfEntityWriteAttempt(_db, _structurePlacement.Reset);

    /// <summary>
    /// Finds an active entity's persisted parent identifier without hydrating the domain slice.
    /// </summary>
    public async Task<Guid?> FindParentIdAsync(Guid id, CancellationToken cancellationToken) =>
        await _db.Entities.AsNoTracking()
            .Where(entity => entity.Id == id)
            .Select(entity => entity.ParentEntityId)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Finds an active entity and hydrates only its own kind detail and capabilities.
    /// Child and relationship projections that do not need full domain recursion can
    /// batch-load their thumbnail rows separately through read-optimized queries.
    /// </summary>
    public async Task<Entity?> FindShallowAsync(Guid id, CancellationToken cancellationToken) {
        _structurePlacement.Reset();
        // Keep the observed aggregate-root concurrency token tracked through the bounded save.
        var row = await _db.Entities
            .FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);
        if (row is null) {
            return null;
        }

        var entity = await ConstructEntityAsync(row, cancellationToken);
        await ValidateRowPlacementAsync(row, null, cancellationToken);
        await HydrateUniversalPropertiesAsync(entity, row, cancellationToken);
        foreach (var mapper in _capabilityMappers) {
            await mapper.HydrateAsync(entity, cancellationToken);
        }
        return entity;
    }

    /// <summary>
    /// Finds a shallow entity when it matches the requested concrete type. This is retained for
    /// focused persistence tests and callers that need a typed mutable slice; it never hydrates a
    /// structural graph.
    /// </summary>
    public async Task<TEntity?> FindAsync<TEntity>(Guid id, CancellationToken cancellationToken)
        where TEntity : Entity =>
        await FindShallowAsync(id, cancellationToken) is TEntity entity ? entity : null;

    /// <summary>Finds a required shallow Entity of the requested concrete type.</summary>
    public async Task<TEntity> RequireAsync<TEntity>(Guid id, CancellationToken cancellationToken)
        where TEntity : Entity =>
        await FindAsync<TEntity>(id, cancellationToken)
            ?? throw new InvalidOperationException($"Entity '{id}' was not found as {typeof(TEntity).Name}.");

    /// <inheritdoc />
    public async Task SaveMutableStateAsync(
        Entity entity,
        EntityMutableStateChange change,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(change);
        if (!change.HasChanges) {
            return;
        }

        try {
            await ApplyMutableStateAsync(entity, change, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        } catch (DbUpdateConcurrencyException ex) {
            // Translate EF's optimistic-concurrency failure into a persistence-agnostic conflict so
            // application services can reload and retry without referencing EF Core. The owning
            // attempt rolls back just its tracked work; never clear the request DbContext here.
            throw new EntityConcurrencyConflictException(
                $"Concurrent modification of entity '{entity.Id}'.",
                ex);
        } catch (DbUpdateException ex) when (IsConcurrentUserEntityStateInsert(ex)) {
            // A missing user-state row can be concurrently created by another request. The only
            // unique violation that has the same retry semantics as xmin is this exact composite
            // primary-key insert; other constraints remain real errors for their callers.
            throw new EntityConcurrencyConflictException(
                $"Concurrent creation of user state for entity '{entity.Id}'.",
                ex);
        } catch (DbUpdateException ex) when (IsConcurrentConsumptionInsert(ex)) {
            // Daily buckets and session access events are logical upserts. A competing request can
            // win their unique key after this unit of work read; reload and reapply against it.
            throw new EntityConcurrencyConflictException(
                $"Concurrent consumption update for entity '{entity.Id}'.",
                ex);
        }
    }

    private static bool IsConcurrentUserEntityStateInsert(DbUpdateException exception) {
        if (exception.InnerException is not PostgresException {
            SqlState: PostgresErrorCodes.UniqueViolation,
            TableName: "user_entity_states",
            ConstraintName: "PK_user_entity_states"
        }) {
            return false;
        }

        return exception.Entries.Any(entry =>
            entry.State == EntityState.Added &&
            entry.Entity is UserEntityStateRow &&
            entry.Metadata.FindPrimaryKey() is { Properties.Count: 2 } key &&
            key.Properties.Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .SequenceEqual([nameof(UserEntityStateRow.EntityId), nameof(UserEntityStateRow.UserId)]));
    }

    private static bool IsConcurrentConsumptionInsert(DbUpdateException exception) =>
        exception.InnerException is PostgresException {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_entity_consumption_days_user_id_entity_id_kind_activity_date" or
                "IX_entity_consumption_events_user_id_session_id_kind"
        };

    private async Task<Entity> ConstructEntityAsync(EntityRow row, CancellationToken cancellationToken) {
        var kind = EntityKindRegistry.Require(row.KindCode);
        if (!_kindMappers.TryGetValue(kind, out var mapper)) {
            throw new InvalidOperationException($"Entity kind '{row.KindCode}' cannot be hydrated.");
        }

        var entity = await mapper.ConstructAsync(row, cancellationToken);
        entity.HydrateStructuralPlacement(row.ParentEntityId, row.SortOrder);
        return entity;
    }

    private async Task ApplyMutableStateAsync(
        Entity entity,
        EntityMutableStateChange change,
        CancellationToken cancellationToken) {
        var row = await _db.Entities.FindAsync([entity.Id], cancellationToken);
        if (row is null) {
            throw new InvalidOperationException($"Entity '{entity.Id}' no longer exists.");
        }

        // Updating the root timestamp retains its xmin as the optimistic-concurrency gate for
        // every user-state mapper without rewriting any structural or metadata columns.
        row.UpdatedAt = DateTimeOffset.UtcNow;
        if (change.CurationFlagsChanged) {
            row.IsNsfw = entity.IsNsfw ?? row.IsNsfw;
            row.IsOrganized = entity.IsOrganized ?? row.IsOrganized;
        }

        if (change.UserOpinionChanged) {
            await UpsertUserOpinionAsync(entity, cancellationToken);
        }

        foreach (var capabilityType in change.ChangedCapabilityTypes) {
            if (!_mutableStateMappers.TryGetValue(capabilityType, out var mapper)) {
                throw new InvalidOperationException(
                    $"No mutable-state mapper is registered for capability '{capabilityType.Name}'.");
            }

            await mapper.PersistAsync(entity, cancellationToken);
        }
    }

    private async Task ValidateRowPlacementAsync(
        EntityRow row,
        EntityKind? knownParentKind,
        CancellationToken cancellationToken) {
        await _structurePlacement.ValidateAsync(
            EntityKindRegistry.Require(row.KindCode),
            row.Id,
            row.ParentEntityId,
            row.ParentEntityId,
            knownParentKind,
            cancellationToken);
    }

    /// <summary>
    /// Persists the current user's favorite/rating opinion of this entity. System saves
    /// (worker, no user) skip this entirely so per-user state is never touched by scans.
    /// </summary>
    private async Task UpsertUserOpinionAsync(Entity entity, CancellationToken cancellationToken) {
        var userId = _currentUser.UserId;
        if (userId == Guid.Empty) {
            return;
        }

        var isFavorite = entity.IsFavorite ?? false;
        var rating = entity.RatingValue;
        var state = await UserEntityStateColumns.FindAsync(_db, userId, entity.Id, cancellationToken);
        if (state is null) {
            if (!isFavorite && rating is null) {
                return;
            }

            state = new UserEntityStateRow {
                UserId = userId,
                EntityId = entity.Id,
            };
            _db.UserEntityStates.Add(state);
        }

        state.IsFavorite = isFavorite;
        state.RatingValue = rating;
        state.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private async Task HydrateUniversalPropertiesAsync(
        Entity entity,
        EntityRow row,
        CancellationToken cancellationToken) {
        var urls = await _db.EntityUrls.AsNoTracking()
            .Where(r => r.EntityId == entity.Id)
            .OrderBy(r => r.SortOrder)
            .Select(r => new EntityUrl(r.Url, r.Label))
            .ToArrayAsync(cancellationToken);
        var externalIds = await _externalIdentities.ListAsync(entity.Id, cancellationToken);
        var providerIdentity = await ResolveProviderIdentityAsync(
            entity,
            externalIds,
            cancellationToken);
        var files = await _db.EntityFiles.AsNoTracking()
            .Where(r => r.EntityId == entity.Id)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new EntityFile(r.Role, r.Path, r.MimeType))
            .ToArrayAsync(cancellationToken);

        // Favorite and rating are the current user's opinion, not entity facts.
        var userId = _currentUser.UserId;
        var state = userId == Guid.Empty
            ? null
            : await UserEntityStateColumns.FindAsync(_db, userId, entity.Id, cancellationToken);

        entity.HydrateUniversalProperties(
            state?.RatingValue,
            state?.IsFavorite ?? false,
            row.IsNsfw,
            row.IsOrganized,
            urls,
            externalIds,
            files,
            row.IsWanted,
            providerIdentity);
    }

    private async Task<EntityProviderIdentity?> ResolveProviderIdentityAsync(
        Entity entity,
        IReadOnlyList<EntityExternalId> externalIds,
        CancellationToken cancellationToken) {
        if (_providerIdentities is null || externalIds.Count == 0) {
            return null;
        }

        var binding = await _providerIdentities.GetAsync(entity.Id, cancellationToken);
        PluginIdentityRoute? route = binding is null
            ? null
            : new PluginIdentityRoute(binding.PluginId, binding.Identity);

        // Compatibility/backfill path: older Entities predate persisted bindings. Only infer when the
        // current manifest set yields exactly one route, which is also the route monitoring uses.
        if (route is null && _identityRouter is not null) {
            var routes = await _identityRouter.ResolveAsync(
                entity.Kind.ToCode(),
                IdentifyAction.LookupId,
                externalIds.Select(value => value.Identity).ToArray(),
                cancellationToken);
            route = routes.Count == 1 ? routes[0] : null;
        }

        if (route is null) {
            return null;
        }

        var association = externalIds.FirstOrDefault(value => value.Identity == route.Identity);
        if (association is null) {
            return null;
        }

        string? url = null;
        if (_identityUrls is not null) {
            url = await _identityUrls.ResolveAsync(
                entity.Kind.ToCode(),
                route,
                cancellationToken);
        }
        url ??= association.Url;

        return new EntityProviderIdentity(route.PluginId, route.Identity, url);
    }

    /// <summary>
    /// Captures request DbContext state before one capability-service attempt. Retry cleanup removes
    /// failed mutation rows and staged events without clearing unrelated work that another use case
    /// had already staged in the same request scope.
    /// </summary>
    private sealed class EfEntityWriteAttempt : IEntityWriteAttempt {
        private readonly PrismediaDbContext _db;
        private readonly Action _resetStructurePlacement;
        private readonly Dictionary<object, EntrySnapshot> _baseline;

        internal EfEntityWriteAttempt(PrismediaDbContext db, Action resetStructurePlacement) {
            _db = db;
            _resetStructurePlacement = resetStructurePlacement;
            _baseline = db.ChangeTracker.Entries()
                .ToDictionary(
                    entry => entry.Entity,
                    entry => new EntrySnapshot(
                        entry.Entity,
                        entry.State,
                        entry.CurrentValues.Clone(),
                        entry.OriginalValues.Clone(),
                        entry.Properties
                            .Where(property => property.IsModified)
                            .Select(property => property.Metadata.Name)
                            .ToHashSet(StringComparer.Ordinal)),
                    ReferenceEqualityComparer.Instance);
        }

        public Task RollbackAsync(CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            // A complete clear removes failed-insert identity-map entries before the next hydrate;
            // every pre-attempt entry is restored immediately so unrelated request work survives.
            _db.ChangeTracker.Clear();
            foreach (var snapshot in _baseline.Values) {
                var entry = _db.Entry(snapshot.Entity);
                entry.State = EntityState.Unchanged;
                entry.CurrentValues.SetValues(snapshot.CurrentValues);
                entry.OriginalValues.SetValues(snapshot.OriginalValues);
                if (snapshot.State == EntityState.Modified) {
                    // Restoring EntityState.Modified directly marks every writable property as
                    // modified. Preserve the original mask so retry cleanup cannot turn an
                    // unrelated one-column patch into a stale full-row overwrite.
                    foreach (var property in entry.Properties) {
                        property.IsModified = snapshot.ModifiedProperties.Contains(property.Metadata.Name);
                    }
                } else {
                    entry.State = snapshot.State;
                }
            }

            _resetStructurePlacement();
            return Task.CompletedTask;
        }

        public void Dispose() {
        }

        private sealed record EntrySnapshot(
            object Entity,
            EntityState State,
            PropertyValues CurrentValues,
            PropertyValues OriginalValues,
            IReadOnlySet<string> ModifiedProperties);
    }
}
