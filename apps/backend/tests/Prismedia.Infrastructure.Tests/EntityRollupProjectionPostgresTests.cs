using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Locks the trigger-maintained rollup projection: inherited context, root-keyed descendant
/// counts with NSFW/wanted semantics, reference and collection counts, reparent transfer,
/// deletion, and reconciliation repair.
/// </summary>
public sealed class EntityRollupProjectionPostgresTests {
    [Fact]
    public async Task TriggersMaintainStructuralRollupsAcrossLifecycle() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var now = DateTimeOffset.UtcNow;

        var rootId = Guid.NewGuid();
        db.LibraryRoots.Add(new LibraryRootRow { Id = rootId, Path = "/media/tv", Label = "TV" });
        var seriesId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var episodeAId = Guid.NewGuid();
        var episodeBId = Guid.NewGuid();
        db.Entities.AddRange(
            Entity(seriesId, EntityKind.VideoSeries, "Series", null, now),
            Entity(seasonId, EntityKind.VideoSeason, "Season 1", seriesId, now),
            Entity(episodeAId, EntityKind.VideoEpisode, "E1", seasonId, now.AddMinutes(1)),
            Entity(episodeBId, EntityKind.VideoEpisode, "E2", seasonId, now.AddMinutes(2)));
        db.EntityLibraryRoots.Add(new EntityLibraryRootRow { EntityId = seriesId, LibraryRootId = rootId });
        await db.SaveChangesAsync();

        // Counts key on the inherited root even though only the series carries its own row.
        var episodeCode = EntityKind.VideoEpisode.ToCode();
        var seasonCode = EntityKind.VideoSeason.ToCode();
        var seriesEpisodes = await CountAsync(db, seriesId, episodeCode);
        Assert.Equal(2, seriesEpisodes.CountTotal);
        Assert.Equal(0, seriesEpisodes.CountNsfw);
        Assert.Equal(rootId, seriesEpisodes.LibraryRootId);
        Assert.Equal(1, (await CountAsync(db, seriesId, seasonCode)).CountTotal);
        Assert.Equal(2, (await CountAsync(db, seasonId, episodeCode)).CountTotal);

        var seriesRollup = await RollupAsync(db, seriesId);
        Assert.Equal(1, seriesRollup.DirectChildCount);
        Assert.Equal(rootId, seriesRollup.EffectiveLibraryRootId);
        Assert.Equal(now.AddMinutes(2), seriesRollup.LatestDescendantCreatedAt!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal(rootId, (await RollupAsync(db, episodeAId)).EffectiveLibraryRootId);

        // A wanted placeholder joins no count.
        var wantedId = Guid.NewGuid();
        var wanted = Entity(wantedId, EntityKind.VideoEpisode, "E3 (wanted)", seasonId, now.AddMinutes(3));
        wanted.IsWanted = true;
        db.Entities.Add(wanted);
        await db.SaveChangesAsync();
        Assert.Equal(2, (await CountAsync(db, seriesId, episodeCode)).CountTotal);

        // An NSFW episode raises the NSFW sub-count up the chain.
        await db.Entities
            .Where(row => row.Id == episodeAId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsNsfw, true));
        Assert.Equal(1, (await CountAsync(db, seriesId, episodeCode)).CountNsfw);
        Assert.Equal(1, (await CountAsync(db, seasonId, episodeCode)).CountNsfw);

        // An NSFW container promotes its whole subtree into the parent's NSFW sub-count.
        await db.Entities
            .Where(row => row.Id == seasonId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsNsfw, true));
        Assert.Equal(2, (await CountAsync(db, seriesId, episodeCode)).CountNsfw);
        Assert.True((await RollupAsync(db, episodeBId)).EffectiveIsNsfw);

        // Reparenting the season transfers its counts to the new chain, keyed by the new root.
        var otherRootId = Guid.NewGuid();
        db.LibraryRoots.Add(new LibraryRootRow { Id = otherRootId, Path = "/media/tv2", Label = "TV 2" });
        var otherSeriesId = Guid.NewGuid();
        db.Entities.Add(Entity(otherSeriesId, EntityKind.VideoSeries, "Other series", null, now));
        db.EntityLibraryRoots.Add(new EntityLibraryRootRow { EntityId = otherSeriesId, LibraryRootId = otherRootId });
        await db.SaveChangesAsync();
        await db.Entities
            .Where(row => row.Id == seasonId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ParentEntityId, otherSeriesId));

        Assert.Null(await FindCountAsync(db, seriesId, episodeCode));
        var moved = await CountAsync(db, otherSeriesId, episodeCode);
        Assert.Equal(2, moved.CountTotal);
        Assert.Equal(otherRootId, moved.LibraryRootId);
        Assert.Equal(otherRootId, (await RollupAsync(db, episodeBId)).EffectiveLibraryRootId);

        // Deleting an episode decrements the new chain.
        await db.Entities.Where(row => row.Id == episodeAId).ExecuteDeleteAsync();
        Assert.Equal(1, (await CountAsync(db, otherSeriesId, episodeCode)).CountTotal);
    }

    [Fact]
    public async Task TriggersMaintainReferenceAndCollectionCountsAndReconcilerRepairsDrift() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var now = DateTimeOffset.UtcNow;

        var tagId = Guid.NewGuid();
        var videoAId = Guid.NewGuid();
        var videoBId = Guid.NewGuid();
        var collectionId = Guid.NewGuid();
        db.Entities.AddRange(
            Entity(tagId, EntityKind.Tag, "Tag", null, now),
            Entity(videoAId, EntityKind.Video, "Video A", null, now),
            Entity(videoBId, EntityKind.Video, "Video B", null, now),
            Entity(collectionId, EntityKind.Collection, "Collection", null, now));
        await db.SaveChangesAsync();

        var videoCode = EntityKind.Video.ToCode();
        db.EntityRelationshipLinks.AddRange(
            Link(videoAId, tagId),
            Link(videoBId, tagId));
        db.CollectionItemDetails.AddRange(
            CollectionItem(collectionId, videoAId, now),
            CollectionItem(collectionId, videoBId, now));
        await db.SaveChangesAsync();

        var references = await db.EntityReferenceCounts.AsNoTracking()
            .SingleAsync(row => row.EntityId == tagId && row.SourceKindCode == videoCode);
        Assert.Equal(2, references.CountTotal);
        Assert.Equal(0, references.CountNsfw);
        var membership = await db.EntityCollectionMemberCounts.AsNoTracking()
            .SingleAsync(row => row.EntityId == collectionId);
        Assert.Equal(2, membership.CountTotal);

        // An NSFW source moves into the NSFW sub-count on both projections.
        await db.Entities
            .Where(row => row.Id == videoAId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsNsfw, true));
        db.ChangeTracker.Clear();
        Assert.Equal(1, (await db.EntityReferenceCounts.AsNoTracking()
            .SingleAsync(row => row.EntityId == tagId && row.SourceKindCode == videoCode)).CountNsfw);

        // Unlinking decrements; removing the last link clears the row.
        await db.EntityRelationshipLinks
            .Where(row => row.EntityId == videoAId && row.TargetEntityId == tagId)
            .ExecuteDeleteAsync();
        Assert.Equal(1, (await db.EntityReferenceCounts.AsNoTracking()
            .SingleAsync(row => row.EntityId == tagId && row.SourceKindCode == videoCode)).CountTotal);

        // Reconciliation repairs manual corruption.
        await db.EntityReferenceCounts
            .Where(row => row.EntityId == tagId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.CountTotal, 99));
        await db.EntityRollups
            .Where(row => row.EntityId == videoBId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.EffectiveIsNsfw, true));
        var reconciler = new EfEntityRollupReconciler(db);
        Assert.True(await reconciler.ReconcileAsync(CancellationToken.None) > 0);
        db.ChangeTracker.Clear();
        Assert.Equal(1, (await db.EntityReferenceCounts.AsNoTracking()
            .SingleAsync(row => row.EntityId == tagId && row.SourceKindCode == videoCode)).CountTotal);
        Assert.False((await db.EntityRollups.AsNoTracking()
            .SingleAsync(row => row.EntityId == videoBId)).EffectiveIsNsfw);
    }

    private static EntityRow Entity(
        Guid id,
        EntityKind kind,
        string title,
        Guid? parentId,
        DateTimeOffset now) =>
        new() {
            Id = id,
            KindCode = kind.ToCode(),
            Title = title,
            ParentEntityId = parentId,
            CreatedAt = now,
            UpdatedAt = now,
        };

    private static EntityRelationshipLinkRow Link(Guid sourceId, Guid targetId) =>
        new() {
            EntityId = sourceId,
            RelationshipCode = RelationshipKind.Tags.ToCode(),
            TargetEntityId = targetId,
            TargetKindCode = EntityKind.Tag.ToCode(),
        };

    private static CollectionItemDetailRow CollectionItem(Guid collectionId, Guid itemId, DateTimeOffset now) =>
        new() {
            Id = Guid.NewGuid(),
            CollectionEntityId = collectionId,
            ItemEntityId = itemId,
            AddedAt = now,
        };

    private static async Task<EntityRollupRow> RollupAsync(PrismediaDbContext db, Guid id) {
        db.ChangeTracker.Clear();
        return await db.EntityRollups.AsNoTracking().SingleAsync(row => row.EntityId == id);
    }

    private static async Task<EntityDescendantCountRow> CountAsync(
        PrismediaDbContext db,
        Guid entityId,
        string kindCode) {
        var row = await FindCountAsync(db, entityId, kindCode);
        Assert.NotNull(row);
        return row;
    }

    private static async Task<EntityDescendantCountRow?> FindCountAsync(
        PrismediaDbContext db,
        Guid entityId,
        string kindCode) {
        db.ChangeTracker.Clear();
        var rows = await db.EntityDescendantCounts.AsNoTracking()
            .Where(row => row.EntityId == entityId && row.DescendantKindCode == kindCode)
            .ToArrayAsync();
        return rows.SingleOrDefault();
    }
}
