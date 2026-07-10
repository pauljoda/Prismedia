using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Files;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Files;

/// <summary>
/// Resolves direct Entity source ownership for a physical file or folder using the media host's path
/// comparison semantics. Database collations cannot safely model Windows and Unix hosts alike, so the
/// bounded source projection is compared in memory.
/// </summary>
public sealed class EfEntitySourcePathOwnerReader(PrismediaDbContext db)
    : IEntitySourcePathOwnerReader {
    /// <inheritdoc />
    public async Task<IReadOnlySet<Guid>> ListDirectOwnerIdsAsync(
        string physicalPath,
        CancellationToken cancellationToken) {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(physicalPath));
        var candidates = await db.EntityFiles.AsNoTracking()
            .Where(file => file.Role == EntityFileRole.Source)
            .Select(file => new { file.EntityId, file.Path })
            .ToArrayAsync(cancellationToken);

        return candidates
            .Where(candidate => FileSystemPathComparison.IsSameOrDescendant(
                path,
                EntitySourcePath.PhysicalOwner(candidate.Path)))
            .Select(candidate => candidate.EntityId)
            .ToHashSet();
    }
}
