using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Media.Persistence;

/// <summary>
/// EF implementation of the shared imported-Entity ready-state check. A linked wrapper is ready only
/// when its canonical subtree owns every exact placed media path; ownership elsewhere cannot satisfy it.
/// </summary>
public sealed class EfImportedEntityReadinessPersistence(
    PrismediaDbContext db,
    IEntityHierarchyReader hierarchy) : IImportedEntityReadinessPersistence {
    public async Task<bool> IsReadyAsync(
        Guid? entityId,
        IReadOnlyCollection<string> placedMediaPaths,
        CancellationToken cancellationToken) {
        var expectedPaths = placedMediaPaths
            .Select(Path.GetFullPath)
            .ToHashSet(FileSystemPathComparison.Comparer);
        if (expectedPaths.Count == 0) {
            return false;
        }

        if (entityId is not { } linkedEntityId) {
            // Database collations do not describe the host filesystem. In particular PostgreSQL remains
            // case-sensitive when Prismedia runs against a Windows media mount, so path equality must be
            // decided in memory. Length is a safe, case-neutral bound for this otherwise-global lookup.
            var expectedPathLengths = expectedPaths.Select(path => path.Length).Distinct().ToArray();
            var globallyOwnedPaths = await db.EntityFiles.AsNoTracking()
                .Where(file => file.Role == EntityFileRole.Source
                    && expectedPathLengths.Contains(file.Path.Length))
                .Select(file => file.Path)
                .ToListAsync(cancellationToken);
            return expectedPaths.SetEquals(
                globallyOwnedPaths.Where(expectedPaths.Contains));
        }

        var target = await db.Entities.AsNoTracking()
            .Where(entity => entity.Id == linkedEntityId)
            .Select(entity => new { entity.IsWanted })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null || target.IsWanted) {
            return false;
        }

        var subtreeIds = await hierarchy.ListSubtreeIdsAsync(linkedEntityId, cancellationToken);
        var targetOwnedPaths = await db.EntityFiles.AsNoTracking()
            .Where(file => subtreeIds.Contains(file.EntityId)
                && file.Role == EntityFileRole.Source)
            .Select(file => file.Path)
            .ToListAsync(cancellationToken);
        return expectedPaths.SetEquals(
            targetOwnedPaths.Where(expectedPaths.Contains));
    }

    public async Task<ImportedEntityReadyScope> ResolveScopeAsync(
        IReadOnlyCollection<string> placedMediaPaths,
        CancellationToken cancellationToken) {
        var expectedPaths = placedMediaPaths
            .Select(Path.GetFullPath)
            .ToHashSet(FileSystemPathComparison.Comparer);
        if (expectedPaths.Count == 0) return new ImportedEntityReadyScope([], []);

        var lengths = expectedPaths.Select(path => path.Length).Distinct().ToArray();
        var candidates = await db.EntityFiles.AsNoTracking()
            .Where(file => file.Role == EntityFileRole.Source && lengths.Contains(file.Path.Length))
            .Join(
                db.Entities.AsNoTracking(),
                file => file.EntityId,
                entity => entity.Id,
                (file, entity) => new { entity.Id, entity.KindCode, file.Path })
            .ToListAsync(cancellationToken);
        var owners = candidates
            .Where(candidate => expectedPaths.Contains(Path.GetFullPath(candidate.Path)))
            .Select(candidate => EntityKindRegistry.TryGet(candidate.KindCode, out var kind)
                ? new ImportedEntityOwner(candidate.Id, kind)
                : null)
            .Where(owner => owner is not null)
            .Select(owner => owner!)
            .DistinctBy(owner => owner.Id)
            .ToArray();
        var ancestors = new HashSet<Guid>();
        foreach (var owner in owners) {
            ancestors.UnionWith(await hierarchy.ListAncestorIdsAsync(owner.Id, cancellationToken));
        }
        return new ImportedEntityReadyScope(owners, ancestors.ToArray());
    }
}
