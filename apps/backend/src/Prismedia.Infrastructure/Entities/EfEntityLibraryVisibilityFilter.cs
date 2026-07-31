using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Requests;
using Prismedia.Application.Security;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// Scoped, EF-translatable library visibility filter. It materializes the small hidden-root and
/// targeted-wanted sets once per caller scope so every consumer shares the same disabled-root,
/// member-grant, and effective-request-target behavior.
/// </summary>
public sealed class EfEntityLibraryVisibilityFilter(
    PrismediaDbContext db,
    ICurrentUserContext currentUser) {
    private static readonly EntityKindDefinition[] DescendantLibraryRootDefinitions = EntityKindRegistry.All
        .Where(definition => definition.LibraryVisibility.Mode == EntityLibraryVisibilityMode.DescendantRoot)
        .ToArray();

    private VisibilityScope? _currentScope;
    private readonly Dictionary<(Guid UserId, UserRole Role), VisibilityScope> _explicitScopes = [];

    /// <summary>Resolves visibility for the active request user.</summary>
    internal async Task<bool> RequiresCurrentUserVisibilityAsync(CancellationToken cancellationToken) =>
        (await GetCurrentScopeAsync(cancellationToken)).RequiresFiltering;

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
        var hidden = (await db.LibraryRoots.AsNoTracking()
            .Where(root => !root.Enabled)
            .Select(root => root.Id)
            .ToArrayAsync(cancellationToken))
            .ToHashSet();
        if (allowedRootIds is not null) {
            var allRootIds = await db.LibraryRoots.AsNoTracking()
                .Select(root => root.Id)
                .ToArrayAsync(cancellationToken);
            hidden.UnionWith(allRootIds.Where(rootId => !allowedRootIds.Contains(rootId)));
        }

        if (hidden.Count == 0) return new VisibilityScope([], HasHiddenRoots: false);
        var hiddenRootIds = hidden.ToArray();
        var hiddenEntityIds = await HiddenLibraryTargetedEntityIds(hiddenRootIds)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        return new VisibilityScope(hiddenEntityIds, HasHiddenRoots: true);
    }

    private IQueryable<Guid> HiddenLibraryTargetedEntityIds(Guid[] hiddenRootIds) {
        var rootedEntityIds = db.EntityLibraryRoots
            .Where(root => root.LibraryRootId != null && hiddenRootIds.Contains(root.LibraryRootId.Value))
            .Select(root => root.EntityId);
        return rootedEntityIds.Concat(HiddenRequestTargetedWantedEntityIds(hiddenRootIds));
    }

    private IQueryable<Guid> HiddenRequestTargetedWantedEntityIds(Guid[] hiddenRootIds) {
        var profiles = db.BookAcquisitionProfiles;
        var hiddenAcquisitions = db.Acquisitions
            .Where(row => row.EntityId != null && (
                row.TargetLibraryRootId != null && hiddenRootIds.Contains(row.TargetLibraryRootId.Value) ||
                row.TargetLibraryRootId == null && row.ProfileId != null && profiles.Any(profile =>
                    profile.Id == row.ProfileId.Value && hiddenRootIds.Contains(profile.TargetLibraryRootId))))
            .Select(row => row.EntityId!.Value);
        var visibleAcquisitions = db.Acquisitions
            .Where(row => row.EntityId != null && (
                row.TargetLibraryRootId != null && !hiddenRootIds.Contains(row.TargetLibraryRootId.Value) ||
                row.TargetLibraryRootId == null && row.ProfileId != null && profiles.Any(profile =>
                    profile.Id == row.ProfileId.Value && !hiddenRootIds.Contains(profile.TargetLibraryRootId))))
            .Select(row => row.EntityId!.Value);
        var hiddenMonitors = db.Monitors
            .Where(row => row.EntityId != null && (
                row.TargetLibraryRootId != null && hiddenRootIds.Contains(row.TargetLibraryRootId.Value) ||
                row.TargetLibraryRootId == null && row.ProfileId != null && profiles.Any(profile =>
                    profile.Id == row.ProfileId.Value && hiddenRootIds.Contains(profile.TargetLibraryRootId))))
            .Select(row => row.EntityId!.Value);
        var visibleMonitors = db.Monitors
            .Where(row => row.EntityId != null && (
                row.TargetLibraryRootId != null && !hiddenRootIds.Contains(row.TargetLibraryRootId.Value) ||
                row.TargetLibraryRootId == null && row.ProfileId != null && profiles.Any(profile =>
                    profile.Id == row.ProfileId.Value && !hiddenRootIds.Contains(profile.TargetLibraryRootId))))
            .Select(row => row.EntityId!.Value);
        var directlyTargeted = hiddenAcquisitions
            .Concat(visibleAcquisitions)
            .Concat(hiddenMonitors)
            .Concat(visibleMonitors);
        var hiddenProfiles = profiles
            .Where(profile => profile.IsDefault && hiddenRootIds.Contains(profile.TargetLibraryRootId))
            .Select(profile => profile.Kind);
        var visibleProfiles = profiles
            .Where(profile => profile.IsDefault && !hiddenRootIds.Contains(profile.TargetLibraryRootId))
            .Select(profile => profile.Kind);
        var defaultHidden = db.Entities
            .Where(entity => entity.IsWanted && !directlyTargeted.Contains(entity.Id))
            .Where(DefaultProfileVisibilityExpression(hiddenProfiles, visibleProfiles))
            .Select(entity => entity.Id);
        var explicitlyHidden = db.Entities.Where(entity => entity.IsWanted &&
                (hiddenAcquisitions.Contains(entity.Id) || hiddenMonitors.Contains(entity.Id)) &&
                !visibleAcquisitions.Contains(entity.Id) && !visibleMonitors.Contains(entity.Id))
            .Select(entity => entity.Id);
        return explicitlyHidden.Concat(defaultHidden);
    }

    internal static Expression<Func<EntityRow, bool>> DefaultProfileVisibilityExpression(
        IQueryable<EntityKind> hiddenProfileKinds,
        IQueryable<EntityKind> visibleProfileKinds) {
        var entity = Expression.Parameter(typeof(EntityRow), "entity");
        Expression Match(IQueryable<EntityKind> kinds) {
            Expression result = Expression.Constant(false);
            foreach (var (profileKind, wantedKinds) in RequestKindRegistry.WantedEntityKindsByProfile) {
                var active = Expression.Call(
                    typeof(Queryable),
                    nameof(Queryable.Contains),
                    [typeof(EntityKind)],
                    kinds.Expression,
                    Expression.Constant(profileKind));
                var entityMatches = Expression.Call(
                    typeof(Enumerable),
                    nameof(Enumerable.Contains),
                    [typeof(string)],
                    Expression.Constant(wantedKinds.Select(kind => kind.ToCode()).ToArray()),
                    Expression.Property(entity, nameof(EntityRow.KindCode)));
                result = Expression.OrElse(result, Expression.AndAlso(active, entityMatches));
            }
            return result;
        }
        return Expression.Lambda<Func<EntityRow, bool>>(
            Expression.AndAlso(
                Match(hiddenProfileKinds),
                Expression.Not(Match(visibleProfileKinds))),
            entity);
    }

    private IQueryable<EntityRow> ApplyVisibility(
        IQueryable<EntityRow> query,
        VisibilityScope scope,
        string? knownKindCode = null) {
        if (!scope.RequiresFiltering) return query;
        var entities = db.Entities;
        var hidden = scope.HiddenEntityIds;
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

    private static IQueryable<EntityRow> ApplyDescendant(
        IQueryable<EntityRow> query,
        IQueryable<EntityRow> entities,
        Guid[] hidden,
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
        Guid[] hidden,
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
        Guid[] hidden) =>
        query.Where(entity =>
            !hidden.Contains(entity.Id) &&
            !entities.Any(parent => parent.Id == entity.ParentEntityId && hidden.Contains(parent.Id)) &&
            !entities.Any(parent => parent.Id == entity.ParentEntityId && entities.Any(grandparent =>
                grandparent.Id == parent.ParentEntityId && hidden.Contains(grandparent.Id))) &&
            !entities.Any(parent => parent.Id == entity.ParentEntityId && entities.Any(grandparent =>
                grandparent.Id == parent.ParentEntityId && entities.Any(rootParent =>
                    rootParent.Id == grandparent.ParentEntityId && hidden.Contains(rootParent.Id)))));

    private sealed record VisibilityScope(Guid[] HiddenEntityIds, bool HasHiddenRoots) {
        public bool RequiresFiltering => HasHiddenRoots;
    }
}
