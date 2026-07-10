using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// Projects physical source ownership through the canonical <see cref="EntityRow.ParentEntityId"/>
/// hierarchy. An entity is source-backed when it or any structural descendant owns a Source file.
/// PostgreSQL resolves the transitive closure in one recursive query; other providers use an
/// equivalent in-memory parent walk so infrastructure tests exercise the same semantics.
/// </summary>
internal sealed class EfEntitySourceOwnershipProjection(PrismediaDbContext db)
    : IEntitySourceOwnershipReader {
    private readonly PrismediaDbContext _db = db;
    private IReadOnlySet<Guid>? _nonRelationalSourceBackedIds;

    /// <summary>Applies source-backed or fileless filtering before count, ordering, and paging.</summary>
    public async Task<IQueryable<EntityRow>> ApplyFilterAsync(
        IQueryable<EntityRow> query,
        bool? hasSourceMedia,
        CancellationToken cancellationToken) {
        if (hasSourceMedia is not { } wantsSourceMedia) {
            return query;
        }

        if (_db.Database.IsNpgsql()) {
            var sourceBackedIds = PostgreSqlSourceBackedIds();
            return wantsSourceMedia
                ? query.Where(entity => sourceBackedIds.Contains(entity.Id))
                : query.Where(entity => !sourceBackedIds.Contains(entity.Id));
        }

        var fallbackIds = (await NonRelationalSourceBackedIdsAsync(cancellationToken)).ToArray();
        return wantsSourceMedia
            ? query.Where(entity => fallbackIds.Contains(entity.Id))
            : query.Where(entity => !fallbackIds.Contains(entity.Id));
    }

    /// <summary>
    /// Resolves source ownership for a bounded batch of card or thumbnail identifiers.
    /// </summary>
    public async Task<IReadOnlySet<Guid>> ResolveAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken) {
        if (entityIds.Count == 0) {
            return new HashSet<Guid>();
        }

        var distinctIds = entityIds.Distinct().ToArray();
        if (_db.Database.IsNpgsql()) {
            return await PostgreSqlSourceBackedIds()
                .Where(id => distinctIds.Contains(id))
                .ToHashSetAsync(cancellationToken);
        }

        var sourceBackedIds = await NonRelationalSourceBackedIdsAsync(cancellationToken);
        return distinctIds.Where(sourceBackedIds.Contains).ToHashSet();
    }

    private IQueryable<Guid> PostgreSqlSourceBackedIds() {
        var sourceRole = EntityFileRole.Source.ToCode();
        return _db.Database.SqlQuery<Guid>($"""
            WITH RECURSIVE source_backed("Value", parent_entity_id) AS (
                SELECT entity.id AS "Value", entity.parent_entity_id
                FROM entities AS entity
                WHERE EXISTS (
                    SELECT 1
                    FROM entity_files AS file
                    WHERE file.entity_id = entity.id
                      AND file.role = {sourceRole}
                )
                UNION
                SELECT parent.id AS "Value", parent.parent_entity_id
                FROM entities AS parent
                INNER JOIN source_backed AS child
                    ON child.parent_entity_id = parent.id
            )
            SELECT "Value"
            FROM source_backed
            """);
    }

    private async Task<IReadOnlySet<Guid>> NonRelationalSourceBackedIdsAsync(
        CancellationToken cancellationToken) {
        if (_nonRelationalSourceBackedIds is not null) {
            return _nonRelationalSourceBackedIds;
        }

        var sourceOwners = await _db.EntityFiles.AsNoTracking()
            .Where(file => file.Role == EntityFileRole.Source)
            .Select(file => file.EntityId)
            .ToArrayAsync(cancellationToken);
        if (sourceOwners.Length == 0) {
            _nonRelationalSourceBackedIds = new HashSet<Guid>();
            return _nonRelationalSourceBackedIds;
        }

        var parents = await _db.Entities.AsNoTracking()
            .Select(entity => new { entity.Id, entity.ParentEntityId })
            .ToDictionaryAsync(entity => entity.Id, entity => entity.ParentEntityId, cancellationToken);
        var sourceBackedIds = sourceOwners.ToHashSet();
        foreach (var sourceOwner in sourceOwners) {
            cancellationToken.ThrowIfCancellationRequested();
            var currentId = sourceOwner;
            while (parents.TryGetValue(currentId, out var parentId) && parentId is { } parent) {
                if (!sourceBackedIds.Add(parent)) {
                    break;
                }

                currentId = parent;
            }
        }

        _nonRelationalSourceBackedIds = sourceBackedIds;
        return sourceBackedIds;
    }
}
