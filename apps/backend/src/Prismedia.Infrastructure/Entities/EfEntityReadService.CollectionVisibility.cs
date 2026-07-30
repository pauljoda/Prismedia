using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

public sealed partial class EfEntityReadService {
    private IQueryable<EntityRow> ApplyCollectionVisibility(IQueryable<EntityRow> query) {
        if (_currentUser.IsSystem) {
            return query;
        }

        var collectionKindCode = EntityKind.Collection.ToCode();
        var userId = CurrentUserId;
        return query.Where(entity =>
            entity.KindCode != collectionKindCode ||
            _db.CollectionDetails.Any(detail =>
                detail.EntityId == entity.Id &&
                (detail.OwnerUserId == userId || detail.IsShared)));
    }

    private Task<bool> IsCollectionVisibleAsync(Guid entityId, CancellationToken cancellationToken) {
        if (_currentUser.IsSystem) {
            return Task.FromResult(true);
        }

        var collectionKindCode = EntityKind.Collection.ToCode();
        var userId = CurrentUserId;
        return _db.Entities.AsNoTracking().AnyAsync(entity =>
            entity.Id == entityId &&
            (entity.KindCode != collectionKindCode ||
             _db.CollectionDetails.Any(detail =>
                 detail.EntityId == entity.Id &&
                 (detail.OwnerUserId == userId || detail.IsShared))),
            cancellationToken);
    }
}
