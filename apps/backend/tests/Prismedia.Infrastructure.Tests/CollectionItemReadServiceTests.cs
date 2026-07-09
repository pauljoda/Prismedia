using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Collections;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class CollectionItemReadServiceTests {
    [Fact]
    public async Task ListItemsAsyncReturnsOrderedVisibleCollectionItems() {
        await using var db = CreateContext();
        var collectionId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var hiddenId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        SeedEntity(db, collectionId, EntityKindRegistry.Collection.Code, "Favorites");
        SeedEntity(db, firstId, EntityKindRegistry.Video.Code, "First");
        SeedEntity(db, hiddenId, EntityKindRegistry.Image.Code, "Hidden", isNsfw: true);
        SeedEntity(db, secondId, EntityKindRegistry.AudioTrack.Code, "Second");
        db.CollectionItemDetails.AddRange(
            Item(collectionId, secondId, 20),
            Item(collectionId, hiddenId, 10),
            Item(collectionId, firstId, 0));
        await db.SaveChangesAsync();

        var service = new CollectionItemReadService(db, new FakeEntityReadService(db));

        var result = await service.ListItemsAsync(collectionId, hideNsfw: true, CancellationToken.None);

        Assert.Collection(result.Items,
            first => {
                Assert.Equal(firstId, first.EntityId);
                Assert.Equal(EntityKind.Video, first.EntityType);
                Assert.Equal(CollectionItemSource.Manual, first.Source);
                Assert.Equal("First", first.Entity.Title);
            },
            second => {
                Assert.Equal(secondId, second.EntityId);
                Assert.Equal(EntityKind.AudioTrack, second.EntityType);
                Assert.Equal("Second", second.Entity.Title);
            });
    }

    private static CollectionItemDetailRow Item(Guid collectionId, Guid itemId, int sortOrder) =>
        new() {
            Id = Guid.NewGuid(),
            CollectionEntityId = collectionId,
            ItemEntityId = itemId,
            Source = CollectionItemSource.Manual,
            SortOrder = sortOrder,
            AddedAt = DateTimeOffset.UtcNow
        };

    private static void SeedEntity(
        PrismediaDbContext db,
        Guid id,
        string kind,
        string title,
        bool isNsfw = false) {
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = kind,
            Title = title,
            IsNsfw = isNsfw,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"collection-items-{Guid.NewGuid():N}")
            .Options);

    private sealed class FakeEntityReadService(PrismediaDbContext db) : IEntityReadService {
        public Task<EntityListResponse> ListAsync(
            string? kind,
            string? query,
            string? cursor,
            bool? hideNsfw,
            int? limit,
            CancellationToken cancellationToken,
            Guid? referencedBy = null,
            string? relationshipCode = null,
            string? sort = null,
            string? sortDir = null,
            int? seed = null,
            bool? favorite = null,
            bool? organized = null,
            int? ratingMin = null,
            int? ratingMax = null,
            bool? unrated = null,
            string? status = null,
            string? bookType = null,
            string? bookFormat = null,
            bool? nsfw = null,
            bool? hasFile = null,
            bool? played = null,
            bool? orphaned = null,
            bool? wanted = null,
            AcquisitionStatus? acquisitionStatus = null) =>
            throw new NotSupportedException();

        public Task<EntityCard?> GetAsync(Guid id, bool hideNsfw, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<EntityThumbnailBatchResponse> GetThumbnailsAsync(
            IReadOnlyList<Guid> ids,
            bool hideNsfw,
            CancellationToken cancellationToken) {
            var rows = await db.Entities.AsNoTracking()
                .Where(row => ids.Contains(row.Id) && (!hideNsfw || !row.IsNsfw))
                .ToArrayAsync(cancellationToken);
            var byId = rows.ToDictionary(row => row.Id);
            var thumbnails = ids
                .Select(id => byId.GetValueOrDefault(id))
                .Where(row => row is not null)
                .Select(row => new EntityThumbnail(
                    row!.Id,
                    row.KindCode.DecodeAs<EntityKind>(),
                    row.Title,
                    row.ParentEntityId,
                    row.SortOrder,
                    null,
                    null,
                    ThumbnailHoverKind.None,
                    null,
                    [],
                    [],
                    null,
                    false,
                    row.IsNsfw,
                    row.IsOrganized))
                .ToArray();
            return new EntityThumbnailBatchResponse(thumbnails);
        }

        public Task<IEntityCard?> GetDetailAsync(Guid id, string kind, bool hideNsfw, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
