using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// Maintains page-count metadata for page-owning leaves and their book/comic ancestors.
/// Pages themselves remain manifest resources and are never materialized as Entities.
/// </summary>
internal static class EntityPageCountPersistence {
    /// <summary>Updates one leaf count and refreshes every affected ancestor rollup.</summary>
    public static async Task SetAsync(
        PrismediaDbContext db,
        Guid entityId,
        int pageCount,
        CancellationToken cancellationToken) {
        ArgumentOutOfRangeException.ThrowIfNegative(pageCount);

        var entity = await db.Entities.SingleOrDefaultAsync(
            row => row.Id == entityId,
            cancellationToken);
        if (entity is null) {
            return;
        }

        await SetLeafDetailAsync(db, entity, pageCount, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var affected = new List<EntityRow> { entity };
        var parentId = entity.ParentEntityId;
        while (parentId is { } id) {
            var parent = await db.Entities.SingleOrDefaultAsync(
                row => row.Id == id,
                cancellationToken);
            if (parent is null) {
                break;
            }

            affected.Add(parent);
            parentId = parent.ParentEntityId;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var target in affected) {
            var count = await CalculateAsync(db, target, cancellationToken);
            await UpsertStatAsync(db, target.Id, count, now, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SetLeafDetailAsync(
        PrismediaDbContext db,
        EntityRow entity,
        int pageCount,
        CancellationToken cancellationToken) {
        if (entity.KindCode == EntityKind.ComicInstallment.ToCode()) {
            var detail = await db.ComicInstallmentDetails.SingleOrDefaultAsync(
                row => row.EntityId == entity.Id,
                cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Comic installment Entity '{entity.Id}' is missing its required detail row.");
            detail.PageCount = pageCount;
            return;
        }

        if (entity.KindCode == EntityKind.BookChapter.ToCode()) {
            var detail = await db.BookChapterDetails.SingleOrDefaultAsync(
                row => row.EntityId == entity.Id,
                cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Book chapter Entity '{entity.Id}' is missing its required detail row.");
            detail.PageCount = pageCount;
            return;
        }

        if (entity.KindCode == EntityKind.Book.ToCode()) {
            var detail = await db.BookDetails.SingleOrDefaultAsync(
                row => row.EntityId == entity.Id,
                cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Book Entity '{entity.Id}' is missing its required detail row.");
            detail.PageCount = pageCount;
        }
    }

    private static async Task<int> CalculateAsync(
        PrismediaDbContext db,
        EntityRow entity,
        CancellationToken cancellationToken) {
        if (entity.KindCode == EntityKind.ComicInstallment.ToCode()) {
            return await db.ComicInstallmentDetails
                .Where(row => row.EntityId == entity.Id)
                .Select(row => row.PageCount)
                .SingleOrDefaultAsync(cancellationToken);
        }

        if (entity.KindCode is var kindCode &&
            (kindCode == EntityKind.ComicVolume.ToCode() ||
             kindCode == EntityKind.ComicSeries.ToCode())) {
            var installmentIds = await DescendantIdsOfKindAsync(
                db,
                entity.Id,
                EntityKind.ComicInstallment.ToCode(),
                cancellationToken);
            return await db.ComicInstallmentDetails
                .Where(row => installmentIds.Contains(row.EntityId))
                .SumAsync(row => row.PageCount, cancellationToken);
        }

        if (entity.KindCode == EntityKind.BookChapter.ToCode()) {
            return await db.BookChapterDetails
                .Where(row => row.EntityId == entity.Id)
                .Select(row => row.PageCount ?? 0)
                .SingleOrDefaultAsync(cancellationToken);
        }

        if (entity.KindCode == EntityKind.BookVolume.ToCode() ||
            entity.KindCode == EntityKind.Book.ToCode()) {
            var chapterIds = await DescendantIdsOfKindAsync(
                db,
                entity.Id,
                EntityKind.BookChapter.ToCode(),
                cancellationToken);
            var chapterCount = await db.BookChapterDetails
                .Where(row => chapterIds.Contains(row.EntityId))
                .SumAsync(row => row.PageCount ?? 0, cancellationToken);
            if (chapterCount > 0 || entity.KindCode == EntityKind.BookVolume.ToCode()) {
                return chapterCount;
            }

            return await db.BookDetails
                .Where(row => row.EntityId == entity.Id)
                .Select(row => row.PageCount ?? 0)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return 0;
    }

    private static async Task<HashSet<Guid>> DescendantIdsOfKindAsync(
        PrismediaDbContext db,
        Guid rootId,
        string wantedKindCode,
        CancellationToken cancellationToken) {
        var descendants = new HashSet<Guid>();
        var frontier = new[] { rootId };
        while (frontier.Length > 0) {
            var children = await db.Entities.AsNoTracking()
                .Where(row => row.ParentEntityId != null && frontier.Contains(row.ParentEntityId.Value))
                .Select(row => new { row.Id, row.KindCode })
                .ToArrayAsync(cancellationToken);
            foreach (var child in children) {
                if (child.KindCode == wantedKindCode) {
                    descendants.Add(child.Id);
                }
            }

            frontier = children.Select(child => child.Id).ToArray();
        }

        return descendants;
    }

    private static async Task UpsertStatAsync(
        PrismediaDbContext db,
        Guid entityId,
        int value,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        var stat = await db.EntityStats.SingleOrDefaultAsync(
            row => row.EntityId == entityId && row.Code == EntityStatCodes.Pages,
            cancellationToken);
        if (stat is null) {
            db.EntityStats.Add(new EntityStatRow {
                EntityId = entityId,
                Code = EntityStatCodes.Pages,
                Value = value,
                UpdatedAt = now
            });
            return;
        }

        stat.Value = value;
        stat.UpdatedAt = now;
    }
}
