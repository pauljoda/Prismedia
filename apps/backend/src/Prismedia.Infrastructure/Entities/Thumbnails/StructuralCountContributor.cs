using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Thumbnails;

/// <summary>
/// Contributes compact membership counts for structural media containers. One grouped aggregate
/// covers the page's visible descendants at depths one through three, which is sufficient for the
/// deepest supported shape (book → volume → chapter → page) without hydrating any descendant row.
/// </summary>
internal sealed class StructuralCountContributor : IThumbnailContributor {
    private static readonly IReadOnlyDictionary<string, EntityKindDefinition> DefinitionsByRootCode =
        EntityKindRegistry.All
            .Where(definition => definition.StructuralThumbnailCounts.Count > 0)
            .ToDictionary(definition => definition.Code, StringComparer.Ordinal);

    private static readonly string[] RootKindCodes = DefinitionsByRootCode.Keys.ToArray();

    private static readonly string[] CountedKindCodes = DefinitionsByRootCode.Values
        .SelectMany(definition => definition.StructuralThumbnailCounts)
        .Select(metric => metric.DescendantKind.ToCode())
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    private readonly PrismediaDbContext _db;

    /// <summary>Creates the contributor for the scoped persistence context.</summary>
    public StructuralCountContributor(PrismediaDbContext db) {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc />
    public int MetaPriority => -100;

    /// <inheritdoc />
    public async Task ContributeAsync(
        ThumbnailContributions contributions,
        CancellationToken cancellationToken) {
        var roots = contributions.Rows
            .Where(row => RootKindCodes.Contains(row.KindCode, StringComparer.Ordinal))
            .ToArray();
        if (roots.Length == 0) {
            return;
        }

        if (contributions.ReadsPersistedRollups) {
            await ContributeFromRollupsAsync(contributions, roots, cancellationToken);
            return;
        }

        var maxDepth = roots
            .SelectMany(root => DefinitionsByRootCode[root.KindCode].StructuralThumbnailCounts)
            .Max(metric => metric.MaximumDepth);
        var counts = await BuildQuery(
                contributions.VisibleEntities,
                roots.Select(row => row.Id).ToArray(),
                maxDepth)
            .ToArrayAsync(cancellationToken);
        var countByRootKindAndDepth = counts.ToDictionary(
            count => (count.RootEntityId, count.KindCode, count.Depth),
            count => count.Count);

        foreach (var root in roots) {
            var rootId = root.Id;
            int AtDepth(string kindCode, int depth) =>
                countByRootKindAndDepth.GetValueOrDefault((rootId, kindCode, depth));
            foreach (var metric in DefinitionsByRootCode[root.KindCode].StructuralThumbnailCounts) {
                var descendantCode = metric.DescendantKind.ToCode();
                var count = Enumerable.Range(1, metric.MaximumDepth)
                    .Sum(depth => AtDepth(descendantCode, depth));
                AddCount(contributions, rootId, metric.Icon, count);
            }
        }
    }

    /// <summary>
    /// Reads the chips from the trigger-maintained descendant-count projection: one indexed lookup
    /// over root-keyed rows, summed across the viewer's allowed roots, with NSFW sub-counts
    /// subtracted for NSFW-hiding viewers. Structure policies cap nesting at the metric depths, so
    /// the projection's all-descendant totals equal the live depth-bounded aggregate.
    /// </summary>
    private async Task ContributeFromRollupsAsync(
        ThumbnailContributions contributions,
        EntityRow[] roots,
        CancellationToken cancellationToken) {
        var rootIds = roots.Select(row => row.Id).ToArray();
        var hiddenRoots = contributions.HiddenLibraryRootIds;
        var counts = await _db.EntityDescendantCounts.AsNoTracking()
            .Where(count => rootIds.Contains(count.EntityId) && !hiddenRoots.Contains(count.LibraryRootId))
            .GroupBy(count => new { count.EntityId, count.DescendantKindCode })
            .Select(group => new {
                group.Key.EntityId,
                group.Key.DescendantKindCode,
                Total = group.Sum(count => count.CountTotal),
                Nsfw = group.Sum(count => count.CountNsfw)
            })
            .ToArrayAsync(cancellationToken);
        var countByRootAndKind = counts.ToDictionary(
            count => (count.EntityId, count.DescendantKindCode),
            count => contributions.HideNsfw ? count.Total - count.Nsfw : count.Total);

        foreach (var root in roots) {
            foreach (var metric in DefinitionsByRootCode[root.KindCode].StructuralThumbnailCounts) {
                var count = countByRootAndKind.GetValueOrDefault((root.Id, metric.DescendantKind.ToCode()));
                AddCount(contributions, root.Id, metric.Icon, count);
            }
        }
    }

    /// <summary>
    /// Builds the single server-side aggregate used by this contributor. Each union branch follows
    /// indexed <c>parent_entity_id</c> links for one fixed depth; grouping returns only a handful of
    /// count rows per page root instead of transferring descendants.
    /// </summary>
    internal static IQueryable<StructuralDescendantCount> BuildQuery(
        IQueryable<EntityRow> visibleEntities,
        Guid[] rootIds,
        int maxDepth) {
        var direct = visibleEntities
            .Where(child => child.ParentEntityId != null && rootIds.Contains(child.ParentEntityId.Value))
            .Select(child => new {
                RootEntityId = child.ParentEntityId!.Value,
                child.KindCode,
                Depth = 1
            });
        var grandchildren =
            from parent in visibleEntities
            where parent.ParentEntityId != null && rootIds.Contains(parent.ParentEntityId.Value)
            join child in visibleEntities.Where(child => child.ParentEntityId != null)
                on parent.Id equals child.ParentEntityId!.Value
            select new {
                RootEntityId = parent.ParentEntityId!.Value,
                child.KindCode,
                Depth = 2
            };
        var greatGrandchildren =
            from parent in visibleEntities
            where parent.ParentEntityId != null && rootIds.Contains(parent.ParentEntityId.Value)
            join child in visibleEntities.Where(child => child.ParentEntityId != null)
                on parent.Id equals child.ParentEntityId!.Value
            join grandchild in visibleEntities.Where(grandchild => grandchild.ParentEntityId != null)
                on child.Id equals grandchild.ParentEntityId!.Value
            select new {
                RootEntityId = parent.ParentEntityId!.Value,
                grandchild.KindCode,
                Depth = 3
            };

        var descendants = direct;
        if (maxDepth >= 2) {
            descendants = descendants.Concat(grandchildren);
        }
        if (maxDepth >= 3) {
            descendants = descendants.Concat(greatGrandchildren);
        }

        return descendants
            .Where(descendant => CountedKindCodes.Contains(descendant.KindCode))
            .GroupBy(descendant => new {
                descendant.RootEntityId,
                descendant.KindCode,
                descendant.Depth
            })
            .Select(group => new StructuralDescendantCount(
                group.Key.RootEntityId,
                group.Key.KindCode,
                group.Key.Depth,
                group.Count()));
    }

    private static void AddCount(
        ThumbnailContributions contributions,
        Guid entityId,
        string icon,
        int count) {
        if (count > 0) {
            contributions.AddMeta(entityId, icon, count.ToString(CultureInfo.InvariantCulture));
        }
    }
}

/// <summary>One grouped descendant count returned by the structural aggregate.</summary>
internal sealed record StructuralDescendantCount(Guid RootEntityId, string KindCode, int Depth, int Count);
