using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Files;
using Prismedia.Application.Organization;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Organization;

/// <summary>
/// EF Core adapter for <see cref="IOrganizePersistence"/>. Reads the planner inputs
/// (library roots, active entities, canonical source files) and applies the post-move
/// path-prefix rewrite across source files and source-capability rows in one commit.
/// </summary>
public sealed class EfOrganizePersistence : IOrganizePersistence {
    private readonly PrismediaDbContext _db;

    public EfOrganizePersistence(PrismediaDbContext db) {
        _db = db;
    }

    public async Task<IReadOnlyList<OrganizeLibraryRoot>> ListRootsAsync(Guid? rootId, CancellationToken cancellationToken) {
        return await _db.LibraryRoots.AsNoTracking()
            .Where(root => rootId == null || root.Id == rootId)
            .Select(root => new OrganizeLibraryRoot(root.Id, root.Path))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrganizeEntityRow>> ListActiveEntitiesAsync(Guid? entityId, CancellationToken cancellationToken) {
        return await _db.Entities.AsNoTracking()
            .Where(entity => entityId == null || entity.Id == entityId)
            .Select(entity => new OrganizeEntityRow(entity.Id, entity.KindCode, entity.Title, entity.ParentEntityId))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrganizeSourceFile>> ListSourceFilesAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken) {
        if (entityIds.Count == 0) {
            return [];
        }

        var ids = entityIds.ToArray();
        return await _db.EntityFiles.AsNoTracking()
            .Where(file => file.Role == EntityFileRole.Source && ids.Contains(file.EntityId))
            .GroupBy(file => file.EntityId)
            .Select(group => new OrganizeSourceFile(
                group.Key,
                group.OrderBy(file => file.CreatedAt).First().Path))
            .ToArrayAsync(cancellationToken);
    }

    public async Task ApplyPathPrefixRewriteAsync(string sourcePath, string targetPath, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;

        var sourceFiles = await _db.EntityFiles
            .Where(file => file.Role == EntityFileRole.Source)
            .ToArrayAsync(cancellationToken);
        foreach (var sourceFile in sourceFiles) {
            if (EntitySourcePath.TryMapPhysicalPrefix(sourceFile.Path, sourcePath, targetPath, out var nextPath)) {
                sourceFile.Path = nextPath;
                sourceFile.UpdatedAt = now;
            }
        }

        var folderSources = await _db.EntitySources.ToArrayAsync(cancellationToken);
        foreach (var source in folderSources) {
            if (EntitySourcePath.TryMapPhysicalPrefix(source.Value, sourcePath, targetPath, out var nextPath)) {
                source.Value = nextPath;
                source.UpdatedAt = now;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

}
