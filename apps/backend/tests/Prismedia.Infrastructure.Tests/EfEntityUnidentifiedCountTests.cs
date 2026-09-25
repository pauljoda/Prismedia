using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Entities.Mappers;
using Prismedia.Infrastructure.Entities.Thumbnails;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfEntityUnidentifiedCountTests {
    [Fact]
    public async Task CountsUnorganizedSourceBackedItemsPerKindUnderNsfwVisibility() {
        await using var db = new PrismediaDbContext(
            new DbContextOptionsBuilder<PrismediaDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var now = DateTimeOffset.UtcNow;
        var unidentifiedVideo = Entity(EntityKind.Video, "Unidentified video");
        var nsfwVideo = Entity(EntityKind.Video, "Unidentified NSFW video", nsfw: true);
        var organizedVideo = Entity(EntityKind.Video, "Organized video", organized: true);
        var filelessVideo = Entity(EntityKind.Video, "Fileless video");
        var wantedBook = Entity(EntityKind.Book, "Wanted book", wanted: true);
        var unidentifiedBook = Entity(EntityKind.Book, "Unidentified book");
        db.Entities.AddRange(unidentifiedVideo, nsfwVideo, organizedVideo, filelessVideo, wantedBook, unidentifiedBook);
        db.BookDetails.AddRange(new BookDetailRow { EntityId = wantedBook.Id }, new BookDetailRow { EntityId = unidentifiedBook.Id });
        foreach (var entity in new[] { unidentifiedVideo, nsfwVideo, organizedVideo, wantedBook, unidentifiedBook }) {
            db.EntityFiles.Add(new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = entity.Id,
                Role = EntityFileRole.Source,
                Path = $"/media/{entity.Id:N}",
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        await db.SaveChangesAsync();
        var user = TestUserContext.Admin();
        var repository = new EfEntityRepository(db, user, EntityMappers.Kinds(db, user), EntityMappers.Capabilities(db, user));
        var reads = new EfEntityReadService(db, user, repository, ThumbnailContributors.For(db), new EfEntityProgressTopologyResolver(db));

        var visible = await reads.CountUnidentifiedAsync($"{EntityKind.Video.ToCode()},{EntityKind.Book.ToCode()}", hideNsfw: false, CancellationToken.None);
        var safe = await reads.CountUnidentifiedAsync(EntityKind.Video.ToCode(), hideNsfw: true, CancellationToken.None);

        Assert.Equal([(EntityKind.Book.ToCode(), 1), (EntityKind.Video.ToCode(), 2)], visible.Select(count => (count.Kind, count.Count)));
        Assert.Equal([(EntityKind.Video.ToCode(), 1)], safe.Select(count => (count.Kind, count.Count)));

        static EntityRow Entity(EntityKind kind, string title, bool organized = false, bool wanted = false, bool nsfw = false) => new() {
            Id = Guid.NewGuid(),
            KindCode = kind.ToCode(),
            Title = title,
            IsOrganized = organized,
            IsWanted = wanted,
            IsNsfw = nsfw,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }
}
