using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Entities.Mappers;
using Prismedia.Infrastructure.Entities.Thumbnails;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfEntityReadServiceDetailHydrationTests {
    [Fact]
    public async Task GetAsyncProjectsMixedChildAndRelationshipGroupsThroughOneThumbnailPage() {
        await using var db = CreateContext();
        var sourceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var volumeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var trackId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var firstPersonId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var secondPersonId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var tagId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            Row(sourceId, EntityKind.Book, "Source", parentId: null, sortOrder: 0),
            Row(volumeId, EntityKind.BookVolume, "Direct volume", sourceId, sortOrder: 1),
            Row(trackId, EntityKind.AudioTrack, "Direct track", sourceId, sortOrder: 0),
            Row(firstPersonId, EntityKind.Person, "First person", parentId: null, sortOrder: 0),
            Row(secondPersonId, EntityKind.Person, "Second person", parentId: null, sortOrder: 0),
            Row(tagId, EntityKind.Tag, "Tag", parentId: null, sortOrder: 0));
        db.BookDetails.Add(new BookDetailRow { EntityId = sourceId });
        db.AudioTrackDetails.Add(new AudioTrackDetailRow { EntityId = trackId });
        db.EntityRelationshipLinks.AddRange(
            Link(sourceId, firstPersonId, EntityKind.Person, RelationshipKind.Cast, sortOrder: 1),
            Link(sourceId, secondPersonId, EntityKind.Person, RelationshipKind.Cast, sortOrder: 0),
            Link(sourceId, trackId, EntityKind.AudioTrack, RelationshipKind.Related, sortOrder: 0),
            Link(sourceId, tagId, EntityKind.Tag, RelationshipKind.Tags, sortOrder: 0));
        await db.SaveChangesAsync();

        var projectionCounter = new CountingThumbnailContributor();
        var service = CreateService(db, projectionCounter);

        var detail = Assert.IsType<EntityCard>(
            await service.GetAsync(sourceId, hideNsfw: false, CancellationToken.None));

        Assert.Equal(1, projectionCounter.InvocationCount);
        Assert.Equal(
            new[] { volumeId, trackId, firstPersonId, secondPersonId, tagId }.Order(),
            Assert.Single(projectionCounter.ProjectedIdSets).Order());
        Assert.Equal(
            new[] { EntityKind.AudioTrack, EntityKind.BookVolume }.OrderBy(kind => kind.ToCode()),
            detail.ChildrenByKind.Select(group => group.Kind));
        Assert.Equal(trackId, Assert.Single(detail.ChildrenByKind, group => group.Kind == EntityKind.AudioTrack).Entities.Single().Id);
        Assert.Equal(volumeId, Assert.Single(detail.ChildrenByKind, group => group.Kind == EntityKind.BookVolume).Entities.Single().Id);
        Assert.Equal(
            new[] { secondPersonId, firstPersonId },
            Assert.Single(detail.Relationships, group => group.Code == RelationshipKind.Cast).Entities.Select(entity => entity.Id));
        Assert.Equal(trackId, Assert.Single(detail.Relationships, group => group.Code == RelationshipKind.Related).Entities.Single().Id);
        Assert.Equal(tagId, Assert.Single(detail.Relationships, group => group.Code == RelationshipKind.Tags).Entities.Single().Id);

        EntityRow Row(Guid id, EntityKind kind, string title, Guid? parentId, int sortOrder) =>
            new() {
                Id = id,
                KindCode = kind.ToCode(),
                Title = title,
                ParentEntityId = parentId,
                SortOrder = sortOrder,
                CreatedAt = now,
                UpdatedAt = now
            };

        EntityRelationshipLinkRow Link(
            Guid source,
            Guid target,
            EntityKind targetKind,
            RelationshipKind relationship,
            int sortOrder) =>
            new() {
                EntityId = source,
                RelationshipCode = relationship.ToCode(),
                Label = relationship.ToCode(),
                TargetEntityId = target,
                TargetKindCode = targetKind.ToCode(),
                SortOrder = sortOrder,
                CreatedAt = now
            };
    }

    private static EfEntityReadService CreateService(
        PrismediaDbContext db,
        IThumbnailContributor projectionCounter) {
        var currentUser = TestUserContext.Admin();
        var repository = new EfEntityRepository(
            db,
            currentUser,
            EntityMappers.Kinds(db),
            EntityMappers.Capabilities(db, currentUser));
        return new EfEntityReadService(
            db,
            currentUser,
            repository,
            [projectionCounter],
            new EfEntityProgressTopologyResolver(db));
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class CountingThumbnailContributor : IThumbnailContributor {
        public int InvocationCount { get; private set; }

        public List<IReadOnlyList<Guid>> ProjectedIdSets { get; } = [];

        public Task ContributeAsync(ThumbnailContributions contributions, CancellationToken cancellationToken) {
            InvocationCount++;
            ProjectedIdSets.Add(contributions.Rows.Select(row => row.Id).ToArray());
            return Task.CompletedTask;
        }
    }
}
