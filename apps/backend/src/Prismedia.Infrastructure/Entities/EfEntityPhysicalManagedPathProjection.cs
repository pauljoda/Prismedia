using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Files;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// Projects the physical paths Prismedia is allowed to remove for an Entity. A managed path is either
/// a real source payload or a folder-provenance source; provenance deliberately remains separate from
/// media availability and request fulfillment.
/// </summary>
internal sealed class EfEntityPhysicalManagedPathProjection(PrismediaDbContext db) {
    /// <summary>Lists the normalized physical paths owned by the supplied Entity identifiers.</summary>
    public async Task<IReadOnlyList<EntityPhysicalManagedPath>> ListAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken) {
        return await ListCoreAsync(entityIds, includeEntities: true, cancellationToken);
    }

    /// <summary>
    /// Lists normalized physical paths outside the supplied Entity set for overlap validation. The caller
    /// deliberately applies filesystem-aware comparison in memory so case-sensitive SQL never misses a
    /// conflicting owner on a case-insensitive host filesystem.
    /// </summary>
    public Task<IReadOnlyList<EntityPhysicalManagedPath>> ListOutsideAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken) =>
        ListCoreAsync(entityIds, includeEntities: false, cancellationToken);

    private async Task<IReadOnlyList<EntityPhysicalManagedPath>> ListCoreAsync(
        IReadOnlyCollection<Guid> entityIds,
        bool includeEntities,
        CancellationToken cancellationToken) {
        if (entityIds.Count == 0 && includeEntities) {
            return [];
        }

        var ids = entityIds.Distinct().ToArray();
        var sourceRole = EntityFileRole.Source;
        var folderCode = EntitySourceCode.Folder.ToCode();
        var payloadPaths = await db.EntityFiles.AsNoTracking()
            .Where(file => (includeEntities ? ids.Contains(file.EntityId) : !ids.Contains(file.EntityId))
                && file.Role == sourceRole)
            .Select(file => new EntityPhysicalManagedPath(file.EntityId, file.Path))
            .ToArrayAsync(cancellationToken);
        var folderPaths = await db.EntitySources.AsNoTracking()
            .Where(source => (includeEntities ? ids.Contains(source.EntityId) : !ids.Contains(source.EntityId))
                && source.Code == folderCode)
            .Select(source => new EntityPhysicalManagedPath(source.EntityId, source.Value))
            .ToArrayAsync(cancellationToken);

        return payloadPaths.Concat(folderPaths)
            .Select(path => path with { Path = EntitySourcePath.PhysicalOwner(path.Path) })
            .Distinct()
            .ToArray();
    }
}

/// <summary>One Entity-owned physical path available to destructive file management.</summary>
internal sealed record EntityPhysicalManagedPath(Guid EntityId, string Path);
