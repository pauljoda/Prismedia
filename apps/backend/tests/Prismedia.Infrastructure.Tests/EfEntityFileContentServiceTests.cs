using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfEntityFileContentServiceTests {
    [Theory]
    [InlineData(".svg", MediaContentTypes.ImageSvg)]
    [InlineData(".apng", MediaContentTypes.ImageApng)]
    public async Task InfersBrowserNativeImageMimeTypesWhenStoredMimeIsMissing(
        string extension,
        string expectedMimeType) {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Role = EntityFileRole.Source,
            Path = $"/media/image{extension}",
            MimeType = null,
            Source = FileSourceKind.Scan.ToCode(),
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var service = new EfEntityFileContentService(db, new AllVisibleEntityChecker());
        var content = await service.GetContentAsync(
            entityId,
            EntityFileRole.Source.ToCode(),
            CancellationToken.None);

        Assert.NotNull(content);
        Assert.Equal(expectedMimeType, content.ContentType);
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class AllVisibleEntityChecker : IEntityVisibilityChecker {
        public Task<bool> IsVisibleAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }
}
