using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Requests;
using Prismedia.Application.Security;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// Scoped, EF-translatable library visibility filter. It resolves the small hidden-root and
/// wanted-target sets once per caller scope while keeping physical Entity ownership in indexed
/// database subqueries, avoiding both large identifier snapshots and repeated request-target plans.
/// </summary>
public sealed class EfEntityLibraryVisibilityFilter(
    PrismediaDbContext db,
    ICurrentUserContext currentUser,
    VisibilityScopeCache? scopeCache = null) {
    private static readonly EntityKindDefinition[] DescendantLibraryRootDefinitions = EntityKindRegistry.All
        .Where(definition => definition.LibraryVisibility.Mode == EntityLibraryVisibilityMode.DescendantRoot)
        .ToArray();

    private VisibilityScope? _currentScope;
    private readonly Dictionary<(Guid UserId, UserRole Role), VisibilityScope> _explicitScopes = [];

    /// <summary>Resolves visibility for the active request user.</summary>
    internal async Task<bool> RequiresCurrentUserVisibilityAsync(CancellationToken cancellationToken) =>
        (await GetCurrentScopeAsync(cancellationToken)).RequiresFiltering;

    /// <summary>
    /// Library roots hidden from the active request user (disabled roots plus denied grants);
    /// empty when the caller is unrestricted. Rollup-backed chip reads filter their root-keyed
    /// count rows with this set instead of embedding ownership subqueries.
    /// </summary>
    internal async Task<Guid[]> GetCurrentHiddenRootIdsAsync(CancellationToken cancellationToken) =>
        (await GetCurrentScopeAsync(cancellationToken)).HiddenRootIds;

    /// <summary>Applies the active request user's already-resolved visibility scope.</summary>
    internal IQueryable<EntityRow> ApplyCurrentUserVisibility(
        IQueryable<EntityRow> query,
        string? knownKindCode = null) =>
        ApplyVisibility(query, _currentScope ?? throw new InvalidOperationException(
            "Library visibility must be resolved before applying the filter."), knownKindCode);

    /// <summary>Filters entity identifiers for an explicit collection owner identity.</summary>
    internal async Task<IReadOnlySet<Guid>> FilterVisibleIdsAsync(
        IReadOnlyCollection<Guid> ids,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken) {
        if (ids.Count == 0) return new HashSet<Guid>();
        var scope = await GetExplicitScopeAsync(userId, role, cancellationToken);
        if (!scope.RequiresFiltering) return ids.ToHashSet();

        var candidateEntities = db.Entities.AsNoTracking()
            .Where(entity => ids.Contains(entity.Id));
        return await ApplyVisibility(candidateEntities, scope)
            .Select(entity => entity.Id)
            .ToHashSetAsync(cancellationToken);
    }

    private async Task<VisibilityScope> GetCurrentScopeAsync(CancellationToken cancellationToken) {
        if (_currentScope is not null) return _currentScope;
        return _currentScope = await CreateScopeAsync(
            await currentUser.GetAllowedLibraryRootIdsAsync(cancellationToken),
            cancellationToken);
    }

    private async Task<VisibilityScope> GetExplicitScopeAsync(
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken) {
        var key = (userId, role);
        if (_explicitScopes.TryGetValue(key, out var scope)) return scope;

        IReadOnlySet<Guid>? allowedRootIds = null;
        if (role != UserRole.Admin) {
            allowedRootIds = await db.UserLibraryAccess.AsNoTracking()
                .Where(access => access.UserId == userId)
                .Select(access => access.LibraryRootId)
                .ToHashSetAsync(cancellationToken);
        }
        scope = await CreateScopeAsync(allowedRootIds, cancellationToken);
        _explicitScopes[key] = scope;
        return scope;
    }

    private async Task<VisibilityScope> CreateScopeAsync(
        IReadOnlySet<Guid>? allowedRootIds,
        CancellationToken cancellationToken) {
        var cacheKey = VisibilityScopeCache.KeyFor(allowedRootIds);
        if (scopeCache?.TryGet(cacheKey) is { } cached) {
            return new VisibilityScope(cached.HiddenRootIds, cached.HiddenWantedEntityIds);
        }

        var roots = db.LibraryRoots.AsNoTracking();
        IQueryable<Guid> hiddenRootIds;
        if (allowedRootIds is null) {
            hiddenRootIds = roots
                .Where(root => !root.Enabled)
                .Select(root => root.Id);
        } else {
            var allowed = allowedRootIds.ToArray();
            hiddenRootIds = roots
                .Where(root => !root.Enabled || !allowed.Contains(root.Id))
                .Select(root => root.Id);
        }

        var hidden = await hiddenRootIds.ToArrayAsync(cancellationToken);
        if (hidden.Length == 0) {
            scopeCache?.Set(cacheKey, [], []);
            return new VisibilityScope([], []);
        }

        // Wanted-target visibility is a comparatively small, request-owned projection, but its
        // acquisition/monitor/default-profile plan is expensive when EF inlines it repeatedly into
        // Entity lists and hierarchy checks. Resolve it once for this request. Physical ownership
        // remains query-backed below so Entities imported later in the scope cannot become visible.
        var hiddenWantedEntityIds = await ResolveHiddenRequestTargetedWantedEntityIdsAsync(
            hidden,
            cancellationToken);
        scopeCache?.Set(cacheKey, hidden, hiddenWantedEntityIds);
        return new VisibilityScope(hidden, hiddenWantedEntityIds);
    }

    private IQueryable<Guid> HiddenLibraryTargetedEntityIds(VisibilityScope scope) =>
        BuildHiddenEntityIdsQuery(db, scope.HiddenRootIds, scope.HiddenWantedEntityIds);

    /// <summary>
    /// Builds the small indexed ownership predicate embedded in Entity reads after request-target
    /// visibility has already been resolved for the caller scope.
    /// </summary>
    internal static IQueryable<Guid> BuildHiddenEntityIdsQuery(
        PrismediaDbContext db,
        Guid[] hiddenRootIds,
        Guid[] hiddenWantedEntityIds) {
        var rootedEntityIds = db.EntityLibraryRoots
            .Where(root => root.LibraryRootId != null && hiddenRootIds.Contains(root.LibraryRootId.Value))
            .Select(root => root.EntityId);
        if (hiddenWantedEntityIds.Length == 0) return rootedEntityIds;

        var wantedEntityIds = db.Entities
            .Where(entity => hiddenWantedEntityIds.Contains(entity.Id))
            .Select(entity => entity.Id);
        return rootedEntityIds.Concat(wantedEntityIds);
    }

    private async Task<Guid[]> ResolveHiddenRequestTargetedWantedEntityIdsAsync(
        Guid[] hiddenRootIds,
        CancellationToken cancellationToken) {
        var targetVisibility = await BuildRequestTargetVisibilityQuery(db, hiddenRootIds)
            .ToArrayAsync(cancellationToken);
        var targetedEntityIds = targetVisibility.Select(target => target.EntityId).ToArray();
        var explicitlyHiddenEntityIds = targetVisibility
            .Where(target => target.HasHiddenTarget && !target.HasVisibleTarget)
            .Select(target => target.EntityId)
            .ToArray();
        var profiles = db.BookAcquisitionProfiles;
        var defaultProfiles = await profiles.AsNoTracking()
            .Where(profile => profile.IsDefault)
            .Select(profile => new { profile.Kind, profile.TargetLibraryRootId })
            .ToArrayAsync(cancellationToken);
        var hiddenProfileKinds = defaultProfiles
            .Where(profile => hiddenRootIds.Contains(profile.TargetLibraryRootId))
            .Select(profile => profile.Kind)
            .ToHashSet();
        var visibleProfileKinds = defaultProfiles
            .Where(profile => !hiddenRootIds.Contains(profile.TargetLibraryRootId))
            .Select(profile => profile.Kind)
            .ToHashSet();
        var hiddenDefaultEntityKindCodes = RequestKindRegistry.WantedEntityKindsByProfile
            .Where(entry => hiddenProfileKinds.Contains(entry.Key) && !visibleProfileKinds.Contains(entry.Key))
            .SelectMany(entry => entry.Value)
            .Select(kind => kind.ToCode())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return await db.Entities.AsNoTracking()
            .Where(entity => entity.IsWanted && (
                explicitlyHiddenEntityIds.Contains(entity.Id) ||
                !targetedEntityIds.Contains(entity.Id) && hiddenDefaultEntityKindCodes.Contains(entity.KindCode)))
            .Select(entity => entity.Id)
            .ToArrayAsync(cancellationToken);
    }

    /// <summary>
    /// Reduces acquisition and monitor targets to one visibility row per wanted Entity before the
    /// result is embedded in ordinary Entity reads.
    /// </summary>
    internal static IQueryable<RequestTargetVisibility> BuildRequestTargetVisibilityQuery(
        PrismediaDbContext db,
        Guid[] hiddenRootIds) {
        var profiles = db.BookAcquisitionProfiles;
        var acquisitionTargets = db.Acquisitions
            .Where(row => row.EntityId != null)
            .Select(row => new {
                EntityId = row.EntityId!.Value,
                LibraryRootId = row.TargetLibraryRootId ?? profiles
                    .Where(profile => row.ProfileId != null && profile.Id == row.ProfileId.Value)
                    .Select(profile => (Guid?)profile.TargetLibraryRootId)
                    .FirstOrDefault()
            });
        var monitorTargets = db.Monitors
            .Where(row => row.EntityId != null)
            .Select(row => new {
                EntityId = row.EntityId!.Value,
                LibraryRootId = row.TargetLibraryRootId ?? profiles
                    .Where(profile => row.ProfileId != null && profile.Id == row.ProfileId.Value)
                    .Select(profile => (Guid?)profile.TargetLibraryRootId)
                    .FirstOrDefault()
            });
        return acquisitionTargets
            .Concat(monitorTargets)
            .Where(target => target.LibraryRootId != null)
            .GroupBy(target => target.EntityId)
            .Select(group => new RequestTargetVisibility(
                group.Key,
                group.Count(target => hiddenRootIds.Contains(target.LibraryRootId!.Value)) > 0,
                group.Count(target => !hiddenRootIds.Contains(target.LibraryRootId!.Value)) > 0));
    }

    private IQueryable<EntityRow> ApplyVisibility(
        IQueryable<EntityRow> query,
        VisibilityScope scope,
        string? knownKindCode = null) {
        if (!scope.RequiresFiltering) return query;
        if (db.Database.IsNpgsql()) {
            return ApplyVisibilityFromRollups(query, scope, knownKindCode);
        }

        var entities = db.Entities;
        var hidden = HiddenLibraryTargetedEntityIds(scope);
        if (!EntityKindRegistry.TryDescribe(knownKindCode, out var definition)) {
            query = ApplyInherited(query, entities, hidden);
            foreach (var descendantDefinition in DescendantLibraryRootDefinitions) {
                query = ApplyDescendant(query, entities, hidden, descendantDefinition, applyOnlyToKind: true);
            }
            return query;
        }

        return definition.LibraryVisibility.Mode switch {
            EntityLibraryVisibilityMode.DirectRoot =>
                query.Where(entity => !hidden.Contains(entity.Id)),
            EntityLibraryVisibilityMode.AncestorRoot => ApplyInherited(query, entities, hidden),
            EntityLibraryVisibilityMode.DescendantRoot => ApplyDescendant(query, entities, hidden, definition, applyOnlyToKind: false),
            _ => query
        };
    }

    /// <summary>
    /// Rollup-backed visibility predicate: the trigger-maintained per-entity effective library
    /// root replaces the multi-level ancestor climbs, and root-keyed descendant counts replace
    /// the descendant-owner unions. Each term is a primary-key-indexed EXISTS with the small
    /// hidden-root/wanted arrays as parameters, instead of whole-table correlated subqueries
    /// embedded in every read. Entities without a rollup row (drift repaired by reconciliation)
    /// default to visible, matching rootless taxonomy.
    /// </summary>
    private IQueryable<EntityRow> ApplyVisibilityFromRollups(
        IQueryable<EntityRow> query,
        VisibilityScope scope,
        string? knownKindCode) {
        var hiddenRootIds = scope.HiddenRootIds;
        var hiddenWantedIds = scope.HiddenWantedEntityIds;
        var rollups = db.EntityRollups;
        query = query.Where(entity =>
            !hiddenWantedIds.Contains(entity.Id) &&
            !rollups.Any(rollup => rollup.EntityId == entity.Id &&
                rollup.EffectiveLibraryRootId != null &&
                hiddenRootIds.Contains(rollup.EffectiveLibraryRootId.Value)));

        // Descendant-rooted containers (series/season/author) stay visible while any of their
        // structural descendants of the policy kind live in a visible root; a container whose
        // rooted descendants are all hidden disappears with them.
        var counts = db.EntityDescendantCounts;
        var descendantDefinitions = EntityKindRegistry.TryDescribe(knownKindCode, out var definition)
            ? definition.LibraryVisibility.Mode == EntityLibraryVisibilityMode.DescendantRoot
                ? [definition]
                : Array.Empty<EntityKindDefinition>()
            : DescendantLibraryRootDefinitions;
        foreach (var descendantDefinition in descendantDefinitions) {
            var ownerCode = descendantDefinition.Code;
            var descendantCode = EntityKindRegistry
                .Describe(descendantDefinition.LibraryVisibility.DescendantKind!.Value)
                .Code;
            query = query.Where(entity => entity.KindCode != ownerCode ||
                !counts.Any(count => count.EntityId == entity.Id &&
                    count.DescendantKindCode == descendantCode) ||
                counts.Any(count => count.EntityId == entity.Id &&
                    count.DescendantKindCode == descendantCode &&
                    !hiddenRootIds.Contains(count.LibraryRootId)));
        }

        return query;
    }

    private static IQueryable<EntityRow> ApplyDescendant(
        IQueryable<EntityRow> query,
        IQueryable<EntityRow> entities,
        IQueryable<Guid> hidden,
        EntityKindDefinition definition,
        bool applyOnlyToKind) {
        var policy = definition.LibraryVisibility;
        var descendantCode = EntityKindRegistry.Describe(policy.DescendantKind!.Value).Code;
        var hiddenOwners = BuildDescendantOwnerIds(
            entities,
            descendantCode,
            policy.MaximumDepth,
            hidden,
            visibleOnly: false);
        var visibleOwners = BuildDescendantOwnerIds(
            entities,
            descendantCode,
            policy.MaximumDepth,
            hidden,
            visibleOnly: true);
        if (!applyOnlyToKind) {
            return query.Where(entity => !hidden.Contains(entity.Id) &&
                (!hiddenOwners.Contains(entity.Id) || visibleOwners.Contains(entity.Id)));
        }
        var ownerCode = definition.Code;
        return query.Where(entity => entity.KindCode != ownerCode ||
            (!hiddenOwners.Contains(entity.Id) || visibleOwners.Contains(entity.Id)));
    }

    private static IQueryable<Guid> BuildDescendantOwnerIds(
        IQueryable<EntityRow> entities,
        string descendantCode,
        int maximumDepth,
        IQueryable<Guid> hidden,
        bool visibleOnly) {
        var descendants = entities.Where(entity =>
            entity.KindCode == descendantCode &&
            (!visibleOnly || !hidden.Contains(entity.Id)));
        var frontier = descendants
            .Where(entity => entity.ParentEntityId != null)
            .Select(entity => entity.ParentEntityId!.Value);
        var ownerIds = frontier;
        for (var depth = 2; depth <= maximumDepth; depth++) {
            var previous = frontier;
            frontier = entities.Where(entity =>
                    previous.Contains(entity.Id) && entity.ParentEntityId != null)
                .Select(entity => entity.ParentEntityId!.Value);
            ownerIds = ownerIds.Concat(frontier);
        }
        return ownerIds;
    }

    private static IQueryable<EntityRow> ApplyInherited(
        IQueryable<EntityRow> query,
        IQueryable<EntityRow> entities,
        IQueryable<Guid> hidden) =>
        query.Where(entity =>
            !hidden.Contains(entity.Id) &&
            !entities.Any(parent => parent.Id == entity.ParentEntityId && hidden.Contains(parent.Id)) &&
            !entities.Any(parent => parent.Id == entity.ParentEntityId && entities.Any(grandparent =>
                grandparent.Id == parent.ParentEntityId && hidden.Contains(grandparent.Id))) &&
            !entities.Any(parent => parent.Id == entity.ParentEntityId && entities.Any(grandparent =>
                grandparent.Id == parent.ParentEntityId && entities.Any(rootParent =>
                    rootParent.Id == grandparent.ParentEntityId && hidden.Contains(rootParent.Id)))));

    private sealed record VisibilityScope(Guid[] HiddenRootIds, Guid[] HiddenWantedEntityIds) {
        public bool RequiresFiltering => HiddenRootIds.Length > 0;
    }

    internal sealed record RequestTargetVisibility(
        Guid EntityId,
        bool HasHiddenTarget,
        bool HasVisibleTarget);
}
