using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// Builds an Entity query for a definition-owned catalog surface and scopes it to the active
/// viewer's enabled library grants. Consumers compose their own projection over this query rather
/// than independently combining catalog hierarchy and library visibility rules.
/// </summary>
public sealed class EfEntityCatalogQuery(
    PrismediaDbContext db,
    EfEntityLibraryVisibilityFilter libraryVisibility) {
    /// <summary>
    /// Gets catalog-eligible Entities on <paramref name="surface"/> visible to the active viewer.
    /// </summary>
    /// <param name="surface">The catalog surface whose definition-owned hierarchy rules apply.</param>
    /// <param name="cancellationToken">Cancellation token used while resolving the viewer scope.</param>
    /// <returns>An EF query ready for the caller's projection or join.</returns>
    public async Task<IQueryable<EntityRow>> ForCurrentUserAsync(
        EntityCatalogSurface surface,
        CancellationToken cancellationToken) {
        var allEntities = db.Entities.AsNoTracking();
        var catalogEntities = EntityCatalogQueryPolicy.Apply(allEntities, allEntities, surface);
        return await libraryVisibility.RequiresCurrentUserVisibilityAsync(cancellationToken)
            ? libraryVisibility.ApplyCurrentUserVisibility(catalogEntities)
            : catalogEntities;
    }
}
