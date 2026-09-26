using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Entities;

public sealed partial class EfEntityReadService : IUnidentifiedEntityCounter {
    #region Actions - Identification Backlog

    /// <inheritdoc />
    /// <remarks>
    /// Applies the same catalog, collection, library, and NSFW visibility as a kind browse, so the counts
    /// agree with the grids Identify opens, then groups in the database instead of counting kind by kind.
    /// </remarks>
    public async Task<IReadOnlyList<UnidentifiedKindCount>> CountUnidentifiedAsync(
        string? kind,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var kindCodes = ParseKindCodes(kind);
        var allEntities = _db.Entities.AsNoTracking();
        var query = allEntities;
        if (kindCodes.Length > 0) {
            query = query.Where(entity => kindCodes.Contains(entity.KindCode));
            query = EntityCatalogQueryPolicy.Apply(query, allEntities, EntityCatalogSurface.KindBrowse, kindCodes);
        } else {
            query = EntityCatalogQueryPolicy.Apply(query, allEntities, EntityCatalogSurface.Discovery);
        }

        query = ApplyCollectionVisibility(query);
        if (await RequiresLibraryVisibilityAsync(cancellationToken)) {
            query = ApplyEnabledLibraryVisibility(query, kindCodes.Length == 1 ? kindCodes[0] : null);
        }

        query = ApplyNsfwVisibility(query, hideNsfw)
            .Where(entity => !entity.IsOrganized && !entity.IsWanted);
        query = await _sourceOwnershipFilter.ApplyFilterAsync(query, hasSourceMedia: true, cancellationToken);
        var counts = await query
            .GroupBy(entity => entity.KindCode)
            .Select(group => new { Kind = group.Key, Count = group.Count() })
            .ToArrayAsync(cancellationToken);
        return counts
            .OrderBy(count => count.Kind, StringComparer.Ordinal)
            .Select(count => new UnidentifiedKindCount(count.Kind, count.Count))
            .ToArray();
    }

    #endregion
}
