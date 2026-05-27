using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Settings;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Settings;

namespace Prismedia.Infrastructure.Media.Persistence;

/// <summary>
/// Implements entity persistence operations for library scanning against the entity schema.
/// </summary>

public sealed partial class LibraryScanPersistenceService {
    public async Task<IReadOnlyList<EntityRefreshTarget>> GetEntityTreeAsync(
        Guid entityId, CancellationToken cancellationToken) {
        var root = await _db.Entities.AsNoTracking()
            .Where(e => e.Id == entityId && e.DeletedAt == null)
            .Select(e => new EntityRefreshTarget(e.Id, e.KindCode, e.Title))
            .FirstOrDefaultAsync(cancellationToken);
        if (root is null) return [];

        var result = new List<EntityRefreshTarget> { root };
        var parentIds = new List<Guid> { entityId };

        // Walk up to 3 levels of children (series → seasons → episodes).
        for (var depth = 0; depth < 3 && parentIds.Count > 0; depth++) {
            var children = await _db.Entities.AsNoTracking()
                .Where(e => e.DeletedAt == null && e.ParentEntityId != null && parentIds.Contains(e.ParentEntityId.Value))
                .Select(e => new EntityRefreshTarget(e.Id, e.KindCode, e.Title))
                .ToArrayAsync(cancellationToken);
            if (children.Length == 0) break;
            result.AddRange(children);
            parentIds = children.Select(c => c.Id).ToList();
        }

        return result;
    }

}
