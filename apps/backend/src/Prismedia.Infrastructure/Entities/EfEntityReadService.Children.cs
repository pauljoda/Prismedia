using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

public sealed partial class EfEntityReadService {
    /// <inheritdoc />
    public async Task<EntityChildrenBatchResponse> GetChildrenAsync(
        IReadOnlyList<Guid> parentIds,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (parentIds.Count == 0) {
            return new EntityChildrenBatchResponse([]);
        }

        var (orderedParentIds, visibleParentIds, enforceLibraryVisibility) =
            await ResolveVisibleChildParentsAsync(parentIds, hideNsfw, cancellationToken);
        if (visibleParentIds.Count == 0) {
            return new EntityChildrenBatchResponse([]);
        }

        var childQuery = VisibleChildrenQuery(visibleParentIds, enforceLibraryVisibility, hideNsfw);
        var childRows = await childQuery
            .OrderBy(entity => entity.ParentEntityId)
            .ThenBy(entity => entity.KindCode)
            .ThenBy(entity => entity.SortOrder)
            .ThenBy(entity => entity.Title)
            .ThenBy(entity => entity.Id)
            .ToArrayAsync(cancellationToken);
        var thumbnails = await ProjectThumbnailsAsync(
            childRows,
            hideNsfw,
            enforceLibraryVisibility,
            cancellationToken);
        var thumbnailsById = thumbnails.ToDictionary(thumbnail => thumbnail.Id);
        var childrenByParent = childRows
            .Where(row => thumbnailsById.ContainsKey(row.Id))
            .GroupBy(row => row.ParentEntityId!.Value)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<EntityThumbnail>)group
                    .Select(row => thumbnailsById[row.Id])
                    .ToArray());

        return new EntityChildrenBatchResponse(
            orderedParentIds
                .Where(visibleParentIds.Contains)
                .Select(parentId => new EntityChildrenBatchGroup(
                    parentId,
                    childrenByParent.GetValueOrDefault(parentId) ?? []))
                .ToArray());
    }

    /// <inheritdoc />
    public async Task<EntityChildReferenceBatchResponse> GetChildReferencesAsync(
        IReadOnlyList<Guid> parentIds,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (parentIds.Count == 0) {
            return new EntityChildReferenceBatchResponse([]);
        }

        var (orderedParentIds, visibleParentIds, enforceLibraryVisibility) =
            await ResolveVisibleChildParentsAsync(parentIds, hideNsfw, cancellationToken);
        if (visibleParentIds.Count == 0) {
            return new EntityChildReferenceBatchResponse([]);
        }

        var rows = await VisibleChildrenQuery(visibleParentIds, enforceLibraryVisibility, hideNsfw)
            .OrderBy(entity => entity.ParentEntityId)
            .ThenBy(entity => entity.KindCode)
            .ThenBy(entity => entity.SortOrder)
            .ThenBy(entity => entity.Title)
            .ThenBy(entity => entity.Id)
            .Select(entity => new {
                entity.Id,
                ParentId = entity.ParentEntityId!.Value,
                entity.KindCode,
            })
            .ToArrayAsync(cancellationToken);
        var childrenByParent = rows
            .GroupBy(row => row.ParentId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<EntityChildReference>)group
                    .Select(row => new EntityChildReference(
                        row.Id,
                        row.KindCode.DecodeAs<EntityKind>()))
                    .ToArray());

        return new EntityChildReferenceBatchResponse(
            orderedParentIds
                .Where(visibleParentIds.Contains)
                .Select(parentId => new EntityChildReferenceBatchGroup(
                    parentId,
                    childrenByParent.GetValueOrDefault(parentId) ?? []))
                .ToArray());
    }

    private async Task<(Guid[] OrderedParentIds, HashSet<Guid> VisibleParentIds, bool EnforceLibraryVisibility)>
        ResolveVisibleChildParentsAsync(
            IReadOnlyList<Guid> parentIds,
            bool hideNsfw,
            CancellationToken cancellationToken) {
        var orderedParentIds = parentIds.Distinct().ToArray();
        var enforceLibraryVisibility = await RequiresLibraryVisibilityAsync(cancellationToken);
        var parentQuery = _db.Entities.AsNoTracking()
            .Where(entity => orderedParentIds.Contains(entity.Id));
        parentQuery = ApplyCollectionVisibility(parentQuery);
        if (enforceLibraryVisibility) {
            parentQuery = ApplyEnabledLibraryVisibility(parentQuery);
        }
        parentQuery = ApplyNsfwVisibility(parentQuery, hideNsfw);
        var visibleParentIds = await parentQuery
            .Select(entity => entity.Id)
            .ToHashSetAsync(cancellationToken);
        return (orderedParentIds, visibleParentIds, enforceLibraryVisibility);
    }

    private IQueryable<EntityRow> VisibleChildrenQuery(
        HashSet<Guid> visibleParentIds,
        bool enforceLibraryVisibility,
        bool hideNsfw) {
        var childQuery = _db.Entities.AsNoTracking()
            .Where(entity => entity.ParentEntityId != null &&
                visibleParentIds.Contains(entity.ParentEntityId.Value));
        childQuery = ApplyCollectionVisibility(childQuery);
        if (enforceLibraryVisibility) {
            childQuery = ApplyEnabledLibraryVisibility(childQuery);
        }
        return ApplyNsfwVisibility(childQuery, hideNsfw);
    }
}
