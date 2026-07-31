using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Entities;

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
        if (visibleParentIds.Count == 0) {
            return new EntityChildrenBatchResponse([]);
        }

        var childQuery = _db.Entities.AsNoTracking()
            .Where(entity => entity.ParentEntityId != null &&
                visibleParentIds.Contains(entity.ParentEntityId.Value));
        childQuery = ApplyCollectionVisibility(childQuery);
        if (enforceLibraryVisibility) {
            childQuery = ApplyEnabledLibraryVisibility(childQuery);
        }
        childQuery = ApplyNsfwVisibility(childQuery, hideNsfw);
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
}
