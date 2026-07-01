using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities.Mappers;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// EF-backed repository that hydrates domain entities from row storage and persists their
/// mutable state. Implements the Application <see cref="IEntityWriteRepository"/> port so
/// Application services can mutate entities without taking a direct dependency on EF Core.
///
/// All kind-specific construction and per-capability row read/write logic lives in
/// <see cref="IEntityKindMapper"/> and <see cref="IEntityCapabilityMapper"/> implementations
/// discovered via dependency injection; this class is intentionally a coordinator over
/// those mappers and never branches on a concrete entity kind or capability itself.
/// </summary>
public sealed class EfEntityRepository : IEntityWriteRepository {
    private static readonly string RelatedRelationshipCode = RelationshipKind.Related.ToCode();

    private readonly PrismediaDbContext _db;
    private readonly IReadOnlyDictionary<EntityKind, IEntityKindMapper> _kindMappers;
    private readonly IReadOnlyList<IEntityCapabilityMapper> _capabilityMappers;

    public EfEntityRepository(
        PrismediaDbContext db,
        IEnumerable<IEntityKindMapper> kindMappers,
        IEnumerable<IEntityCapabilityMapper> capabilityMappers) {
        _db = db;
        _kindMappers = kindMappers.ToDictionary(mapper => mapper.Kind);
        _capabilityMappers = capabilityMappers.ToArray();
    }

    /// <summary>
    /// Finds an active entity and hydrates its domain relationships plus mutable state capabilities.
    /// </summary>
    public async Task<Entity?> FindAsync(Guid id, CancellationToken cancellationToken) {
        var row = await _db.Entities.AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);
        if (row is null) {
            return null;
        }

        var context = new EntityHydrationContext();
        return await HydrateAsync(row, context, cancellationToken);
    }

    /// <summary>
    /// Finds an active entity's persisted parent identifier without hydrating the domain slice.
    /// </summary>
    public async Task<Guid?> FindParentIdAsync(Guid id, CancellationToken cancellationToken) =>
        await _db.Entities.AsNoTracking()
            .Where(entity => entity.Id == id)
            .Select(entity => entity.ParentEntityId)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Finds an active entity and hydrates only its own kind detail and capabilities.
    /// Child and relationship projections that do not need full domain recursion can
    /// batch-load their thumbnail rows separately through read-optimized queries.
    /// </summary>
    public async Task<Entity?> FindShallowAsync(Guid id, CancellationToken cancellationToken) {
        var row = await _db.Entities.AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);
        if (row is null) {
            return null;
        }

        var entity = await ConstructEntityAsync(row, cancellationToken);
        await HydrateUniversalPropertiesAsync(entity, row, cancellationToken);
        foreach (var mapper in _capabilityMappers) {
            await mapper.HydrateAsync(entity, cancellationToken);
        }
        return entity;
    }

    /// <summary>
    /// Resolves a chapter-local page cursor into an absolute book-level cursor.
    /// </summary>
    public async Task<BookProgressPosition?> ResolveBookProgressPositionAsync(
        Guid bookId,
        Guid currentEntityId,
        int index,
        int total,
        CancellationToken cancellationToken) {
        var chapterId = await ResolveProgressChapterIdAsync(currentEntityId, cancellationToken);
        if (chapterId is null) {
            return null;
        }

        var chapters = await LoadOrderedBookChaptersAsync(bookId, cancellationToken);
        if (chapters.Count == 0) {
            return null;
        }

        var chapterIds = chapters.Select(chapter => chapter.Id).ToArray();
        var pageCounts = await _db.Entities.AsNoTracking()
            .Where(row => row.ParentEntityId != null &&
                          chapterIds.Contains(row.ParentEntityId.Value) &&
                          row.KindCode == EntityKindRegistry.BookPage.Code)
            .GroupBy(row => row.ParentEntityId!.Value)
            .Select(group => new { ChapterId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.ChapterId, group => group.Count, cancellationToken);

        var workTotal = chapterIds.Sum(id => pageCounts.GetValueOrDefault(id));
        if (workTotal <= 0) {
            return null;
        }

        var offset = 0;
        foreach (var chapter in chapters) {
            var pageCount = pageCounts.GetValueOrDefault(chapter.Id);
            if (chapter.Id == chapterId.Value) {
                var localTotal = pageCount > 0 ? pageCount : Math.Max(0, total);
                var localIndex = localTotal == 0 ? 0 : Math.Clamp(index, 0, localTotal - 1);
                return new BookProgressPosition(chapter.Id, Math.Min(workTotal - 1, offset + localIndex), workTotal);
            }

            offset += pageCount;
        }

        return null;
    }

    /// <summary>
    /// Finds an active entity and returns it only when it matches the requested concrete domain type.
    /// </summary>
    public async Task<TEntity?> FindAsync<TEntity>(Guid id, CancellationToken cancellationToken)
        where TEntity : Entity =>
        await FindAsync(id, cancellationToken) is TEntity entity ? entity : null;

    /// <summary>
    /// Finds a required active entity of the requested concrete domain type.
    /// </summary>
    public async Task<TEntity> RequireAsync<TEntity>(Guid id, CancellationToken cancellationToken)
        where TEntity : Entity =>
        await FindAsync<TEntity>(id, cancellationToken)
            ?? throw new InvalidOperationException($"Entity '{id}' was not found as {typeof(TEntity).Name}.");

    /// <summary>
    /// Persists one hydrated domain entity slice, including structural links, relationships, and mutable capabilities.
    /// </summary>
    public async Task SaveAsync(Entity entity, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(entity);

        // The capability save flushes stale rows before re-adding them (see the intermediate
        // SaveChanges in SaveEntityAsync), so the write spans two SaveChanges calls. Wrap both in a
        // single transaction so a failure between them cannot leave an entity with its capabilities
        // cleared but not re-persisted, which would matter when a failed job is retried. The save is
        // pure database work with no file IO between the flushes, so the transaction stays short.
        // The in-memory test provider has no transaction support, and if a caller already opened a
        // transaction we participate in it rather than nesting.
        try {
            if (!_db.Database.IsRelational() || _db.Database.CurrentTransaction is not null) {
                await SaveCoreAsync(entity, cancellationToken);
                return;
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            await SaveCoreAsync(entity, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        } catch (DbUpdateConcurrencyException ex) {
            // Translate EF's optimistic-concurrency failure into a persistence-agnostic conflict so
            // application services can reload and retry without referencing EF Core. Detach the stale
            // tracked entries so a retry on the same DbContext re-reads current rows.
            _db.ChangeTracker.Clear();
            throw new EntityConcurrencyConflictException(
                $"Concurrent modification of entity '{entity.Id}'.",
                ex);
        }
    }

    private async Task SaveCoreAsync(Entity entity, CancellationToken cancellationToken) {
        var visited = new HashSet<Guid>();
        await SaveEntityAsync(entity, visited, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Entity> HydrateAsync(
        EntityRow row,
        EntityHydrationContext context,
        CancellationToken cancellationToken) {
        if (context.TryGet(row.Id, out var existing)) {
            return existing;
        }

        var entity = await ConstructEntityAsync(row, cancellationToken);
        context.Add(entity);
        await HydrateUniversalPropertiesAsync(entity, row, cancellationToken);
        await HydrateChildrenAsync(entity, context, cancellationToken);
        await HydrateRelationshipsAsync(entity, context, cancellationToken);
        foreach (var mapper in _capabilityMappers) {
            await mapper.HydrateAsync(entity, cancellationToken);
        }
        return entity;
    }

    private async Task<Entity> ConstructEntityAsync(EntityRow row, CancellationToken cancellationToken) {
        var kind = EntityKindRegistry.Require(row.KindCode);
        if (!_kindMappers.TryGetValue(kind, out var mapper)) {
            throw new InvalidOperationException($"Entity kind '{row.KindCode}' cannot be hydrated.");
        }

        var entity = await mapper.ConstructAsync(row, cancellationToken);
        entity.HydrateStructuralPlacement(row.ParentEntityId, row.SortOrder);
        return entity;
    }

    private async Task<Guid?> ResolveProgressChapterIdAsync(Guid currentEntityId, CancellationToken cancellationToken) {
        var row = await _db.Entities.AsNoTracking()
            .Where(entity => entity.Id == currentEntityId)
            .Select(entity => new { entity.Id, entity.KindCode, entity.ParentEntityId })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null) {
            return null;
        }

        if (row.KindCode == EntityKindRegistry.BookChapter.Code) {
            return row.Id;
        }

        if (row.KindCode == EntityKindRegistry.BookPage.Code && row.ParentEntityId is { } parentId) {
            return parentId;
        }

        return null;
    }

    private async Task<IReadOnlyList<EntityRow>> LoadOrderedBookChaptersAsync(
        Guid bookId,
        CancellationToken cancellationToken) {
        var directChildren = await _db.Entities.AsNoTracking()
            .Where(row => row.ParentEntityId == bookId)
            .OrderBy(row => row.SortOrder)
            .ThenBy(row => row.Title)
            .ThenBy(row => row.Id)
            .ToArrayAsync(cancellationToken);
        var volumeIds = directChildren
            .Where(row => row.KindCode == EntityKindRegistry.BookVolume.Code)
            .Select(row => row.Id)
            .ToArray();
        var volumeChapters = volumeIds.Length == 0
            ? Array.Empty<EntityRow>()
            : await _db.Entities.AsNoTracking()
                .Where(row => row.ParentEntityId != null &&
                              volumeIds.Contains(row.ParentEntityId.Value) &&
                              row.KindCode == EntityKindRegistry.BookChapter.Code)
                .OrderBy(row => row.ParentEntityId)
                .ThenBy(row => row.SortOrder)
                .ThenBy(row => row.Title)
                .ThenBy(row => row.Id)
                .ToArrayAsync(cancellationToken);
        var chaptersByVolume = volumeChapters
            .GroupBy(row => row.ParentEntityId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var chapters = new List<EntityRow>();

        foreach (var child in directChildren) {
            if (child.KindCode == EntityKindRegistry.BookChapter.Code) {
                chapters.Add(child);
                continue;
            }

            if (child.KindCode == EntityKindRegistry.BookVolume.Code &&
                chaptersByVolume.TryGetValue(child.Id, out var childChapters)) {
                chapters.AddRange(childChapters);
            }
        }

        return chapters;
    }

    private async Task HydrateChildrenAsync(
        Entity entity,
        EntityHydrationContext context,
        CancellationToken cancellationToken) {
        var childRows = await _db.Entities.AsNoTracking()
            .Where(row => row.ParentEntityId == entity.Id)
            .OrderBy(row => row.SortOrder)
            .ThenBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .ToArrayAsync(cancellationToken);
        foreach (var childRow in childRows) {
            var child = await HydrateAsync(childRow, context, cancellationToken);
            if (!entity.ChildEntities.Any(existing => existing.Id == child.Id)) {
                entity.AddChild(child, childRow.SortOrder);
            }
        }
    }

    private async Task HydrateRelationshipsAsync(
        Entity entity,
        EntityHydrationContext context,
        CancellationToken cancellationToken) {
        var links = await _db.EntityRelationshipLinks.AsNoTracking()
            .Where(link => link.EntityId == entity.Id &&
                           link.RelationshipCode == RelatedRelationshipCode)
            .OrderBy(link => link.SortOrder)
            .ToArrayAsync(cancellationToken);
        var targetIds = links.Select(link => link.TargetEntityId).ToArray();
        var targetRows = await _db.Entities.AsNoTracking()
            .Where(row => targetIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        foreach (var link in links) {
            if (!targetRows.TryGetValue(link.TargetEntityId, out var targetRow)) {
                continue;
            }

            var target = await HydrateAsync(targetRow, context, cancellationToken);
            if (!entity.Relationships.Any(existing => existing.Id == target.Id)) {
                entity.AddRelationship(target);
            }
        }
    }

    private async Task SaveEntityAsync(Entity entity, ISet<Guid> visited, CancellationToken cancellationToken) {
        if (!visited.Add(entity.Id)) {
            return;
        }

        await UpsertEntityRowAsync(entity, cancellationToken);

        foreach (var child in entity.ChildEntities) {
            await SaveEntityAsync(child, visited, cancellationToken);
        }

        foreach (var relationship in entity.Relationships) {
            await SaveEntityAsync(relationship, visited, cancellationToken);
        }

        foreach (var credit in entity.Credits?.Credits ?? Array.Empty<CapabilityCredits.Item>()) {
            await SaveEntityAsync(credit.Person, visited, cancellationToken);
        }

        _db.EntityRelationshipLinks.RemoveRange(
            _db.EntityRelationshipLinks.Where(link =>
                link.EntityId == entity.Id &&
                link.RelationshipCode == RelatedRelationshipCode));
        ClearUniversalCollections(entity);
        foreach (var mapper in _capabilityMappers) {
            await mapper.ClearAsync(entity, cancellationToken);
        }

        // Flush all stale rows before re-queueing the new state. The intermediate save is
        // here because rows keyed by EntityId (description, classification, …) would
        // otherwise collide with their re-added counterparts in the same change tracker.
        await _db.SaveChangesAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var relationshipIndex = 0;
        foreach (var relationship in entity.Relationships) {
            _db.EntityRelationshipLinks.Add(new EntityRelationshipLinkRow {
                EntityId = entity.Id,
                RelationshipCode = RelatedRelationshipCode,
                Label = relationship.Title,
                TargetEntityId = relationship.Id,
                TargetKindCode = EntityKindRegistry.ToCode(relationship.Kind),
                SortOrder = relationshipIndex,
                CreatedAt = now,
            });
            relationshipIndex++;
        }

        await PersistUniversalCollectionsAsync(entity);
        foreach (var mapper in _capabilityMappers) {
            await mapper.PersistAsync(entity, cancellationToken);
        }

        if (_kindMappers.TryGetValue(entity.Kind, out var kindMapper)) {
            await kindMapper.PersistDetailAsync(entity, cancellationToken);
        }
    }

    private async Task UpsertEntityRowAsync(Entity entity, CancellationToken cancellationToken) {
        var row = await _db.Entities.FindAsync([entity.Id], cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (row is null) {
            _db.Entities.Add(new EntityRow {
                Id = entity.Id,
                KindCode = EntityKindRegistry.ToCode(entity.Kind),
                Title = entity.Title,
                ParentEntityId = entity.ParentEntityId,
                SortOrder = entity.SortOrder,
                RatingValue = entity.RatingValue,
                IsFavorite = entity.IsFavorite ?? false,
                IsNsfw = entity.IsNsfw ?? false,
                IsOrganized = entity.IsOrganized ?? false,
                IsWanted = entity.IsWanted ?? false,
                CreatedAt = now,
                UpdatedAt = now,
            });
            return;
        }

        row.KindCode = EntityKindRegistry.ToCode(entity.Kind);
        row.Title = entity.Title;
        row.ParentEntityId = entity.ParentEntityId;
        row.SortOrder = entity.SortOrder;
        row.RatingValue = entity.RatingValue;
        row.IsFavorite = entity.IsFavorite ?? false;
        row.IsNsfw = entity.IsNsfw ?? false;
        row.IsOrganized = entity.IsOrganized ?? false;
        row.IsWanted = entity.IsWanted ?? false;
        row.UpdatedAt = now;
    }

    private async Task HydrateUniversalPropertiesAsync(
        Entity entity,
        EntityRow row,
        CancellationToken cancellationToken) {
        var urls = await _db.EntityUrls.AsNoTracking()
            .Where(r => r.EntityId == entity.Id)
            .OrderBy(r => r.SortOrder)
            .Select(r => new EntityUrl(r.Url, r.Label))
            .ToArrayAsync(cancellationToken);
        var externalIds = await _db.EntityExternalIds.AsNoTracking()
            .Where(r => r.EntityId == entity.Id)
            .Select(r => new EntityExternalId(r.Provider, r.Value, r.Url))
            .ToArrayAsync(cancellationToken);
        var files = await _db.EntityFiles.AsNoTracking()
            .Where(r => r.EntityId == entity.Id)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new EntityFile(r.Role, r.Path, r.MimeType))
            .ToArrayAsync(cancellationToken);

        entity.HydrateUniversalProperties(
            row.RatingValue,
            row.IsFavorite,
            row.IsNsfw,
            row.IsOrganized,
            urls,
            externalIds,
            files,
            row.IsWanted);
    }

    private Task PersistUniversalCollectionsAsync(Entity entity) {
        var now = DateTimeOffset.UtcNow;
        var order = 0;
        foreach (var url in entity.Urls) {
            _db.EntityUrls.Add(new EntityUrlRow {
                Id = Guid.NewGuid(),
                EntityId = entity.Id,
                Url = url.Value,
                Label = url.Label,
                SortOrder = order++,
                CreatedAt = now,
            });
        }

        foreach (var externalId in entity.ExternalIds) {
            _db.EntityExternalIds.Add(new EntityExternalIdRow {
                Id = Guid.NewGuid(),
                EntityId = entity.Id,
                Provider = externalId.Provider,
                Value = externalId.Value,
                Url = externalId.Url,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        foreach (var file in entity.EntityFiles) {
            _db.EntityFiles.Add(new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = entity.Id,
                Role = file.Role,
                Path = file.Path,
                MimeType = file.MimeType,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        return Task.CompletedTask;
    }

    private void ClearUniversalCollections(Entity entity) {
        _db.EntityUrls.RemoveRange(
            _db.EntityUrls.Where(r => r.EntityId == entity.Id));
        _db.EntityExternalIds.RemoveRange(
            _db.EntityExternalIds.Where(r => r.EntityId == entity.Id));
        _db.EntityFiles.RemoveRange(
            _db.EntityFiles.Where(r => r.EntityId == entity.Id));
    }

    private sealed class EntityHydrationContext {
        private readonly Dictionary<Guid, Entity> _entities = [];

        public bool TryGet(Guid id, out Entity entity) => _entities.TryGetValue(id, out entity!);

        public void Add(Entity entity) => _entities.Add(entity.Id, entity);
    }
}
