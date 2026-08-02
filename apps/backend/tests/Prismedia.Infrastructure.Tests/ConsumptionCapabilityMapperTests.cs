using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Infrastructure.Entities.Mappers.Capabilities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>Guards direct-plus-descendant consumption rollups for structural media containers.</summary>
public sealed class ConsumptionCapabilityMapperTests {
    /// <summary>
    /// Albums, seasons, books, and galleries all use the same definition-driven rollup: direct activity on
    /// the owner remains visible while leaf activity is summed exactly once.
    /// </summary>
    [Theory]
    [InlineData(EntityKind.AudioLibrary, EntityKind.AudioTrack)]
    [InlineData(EntityKind.VideoSeason, EntityKind.VideoEpisode)]
    [InlineData(EntityKind.Book, EntityKind.BookChapter)]
    [InlineData(EntityKind.Gallery, EntityKind.Image)]
    public async Task HydrateSumsDirectOwnerAndConsumableLeaves(
        EntityKind rootKind,
        EntityKind leafKind) {
        await using var db = CreateContext();
        var rootId = Guid.NewGuid();
        var firstLeafId = Guid.NewGuid();
        var secondLeafId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-08-01T18:00:00Z");
        db.Entities.AddRange(
            Row(rootId, rootKind, "Container", null, now),
            Row(firstLeafId, leafKind, "One", rootId, now),
            Row(secondLeafId, leafKind, "Two", rootId, now));
        db.UserEntityStates.AddRange(
            State(rootId, accessCount: 1, completionCount: 0, activeSeconds: 30, resumeSeconds: 12, now.AddMinutes(-3), completed: false),
            State(firstLeafId, accessCount: 2, completionCount: 1, activeSeconds: 100, resumeSeconds: 0, now.AddMinutes(-2), completed: true),
            State(secondLeafId, accessCount: 3, completionCount: 1, activeSeconds: 200, resumeSeconds: 0, now.AddMinutes(-1), completed: true));
        await db.SaveChangesAsync();

        var entity = CreateEntity(rootKind, rootId);
        var mapper = new ConsumptionCapabilityMapper(db, TestUserContext.Admin());

        await mapper.HydrateAsync(entity, CancellationToken.None);

        var consumption = entity.RequireCapability<CapabilityConsumption>().Value;
        Assert.Equal(6, consumption.AccessCount);
        Assert.Equal(2, consumption.CompletionCount);
        Assert.Equal(TimeSpan.FromSeconds(330), consumption.ActiveDuration);
        Assert.Equal(TimeSpan.FromSeconds(12), consumption.ResumeTime);
        Assert.Equal(now.AddMinutes(-1), consumption.LastActiveAt);
        Assert.Equal(now.AddMinutes(-1), consumption.CompletedAt);
    }

    private static PrismediaDbContext CreateContext() => new(
        new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Entity CreateEntity(EntityKind kind, Guid id) => kind switch {
        EntityKind.AudioLibrary => new AudioLibrary(id, "Album"),
        EntityKind.VideoSeason => new VideoSeason(id, "Season", parentEntityId: null),
        EntityKind.Book => new Book(id, "Book", BookType.Book, coverPageId: null),
        EntityKind.Gallery => new Gallery(id, "Gallery", GalleryType.Virtual, coverImageId: null),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static EntityRow Row(
        Guid id,
        EntityKind kind,
        string title,
        Guid? parentId,
        DateTimeOffset now) => new() {
        Id = id,
        KindCode = kind.ToCode(),
        Title = title,
        ParentEntityId = parentId,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static UserEntityStateRow State(
        Guid entityId,
        int accessCount,
        int completionCount,
        double activeSeconds,
        double resumeSeconds,
        DateTimeOffset occurredAt,
        bool completed) => new() {
        UserId = TestUserContext.UserId,
        EntityId = entityId,
        AccessCount = accessCount,
        CompletionCount = completionCount,
        ActiveSeconds = activeSeconds,
        ResumeSeconds = resumeSeconds,
        LastAccessedAt = occurredAt,
        LastActiveAt = occurredAt,
        CompletedAt = completed ? occurredAt : null,
        UpdatedAt = occurredAt
    };
}
