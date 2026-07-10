using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfIdentifyTargetEligibilityServiceTests {
    [Fact]
    public async Task EvaluateManyAsyncDistinguishesMissingWantedFilelessAndSourceMediaTargets() {
        await using var db = CreateContext();
        var eligibleId = Guid.NewGuid();
        var wantedId = Guid.NewGuid();
        var filelessId = Guid.NewGuid();
        var missingId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        db.Entities.AddRange(
            Entity(eligibleId, isWanted: false, now),
            Entity(wantedId, isWanted: true, now),
            Entity(filelessId, isWanted: false, now));
        db.EntityFiles.AddRange(
            File(eligibleId, EntityFileRole.Source, "/media/scanned-series", now),
            File(wantedId, EntityFileRole.Source, "/media/inconsistent-wanted-series", now),
            File(filelessId, EntityFileRole.Poster, "/assets/poster.jpg", now));
        await db.SaveChangesAsync();
        var eligibility = new EfIdentifyTargetEligibilityService(db);

        var results = await eligibility.EvaluateManyAsync(
            [eligibleId, wantedId, filelessId, missingId],
            CancellationToken.None);

        Assert.Equal(IdentifyTargetEligibilityStatus.Eligible, results[eligibleId].Status);
        Assert.Equal(IdentifyTargetEligibilityStatus.Wanted, results[wantedId].Status);
        Assert.Equal(IdentifyTargetEligibilityStatus.NoSourceMedia, results[filelessId].Status);
        Assert.Equal(IdentifyTargetEligibilityStatus.Missing, results[missingId].Status);
    }

    [Fact]
    public async Task EvaluateAsyncUsesCanonicalSourceBackedSubtreeTruthForContainers() {
        await using var db = CreateContext();
        var containerId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var wantedContainerId = Guid.NewGuid();
        var wantedChildId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            Entity(containerId, isWanted: false, now),
            Entity(childId, isWanted: false, now, containerId),
            Entity(wantedContainerId, isWanted: true, now),
            Entity(wantedChildId, isWanted: false, now, wantedContainerId));
        db.EntityFiles.AddRange(
            File(childId, EntityFileRole.Source, "/media/series/season/episode.mkv", now),
            File(wantedChildId, EntityFileRole.Source, "/media/wanted/episode.mkv", now));
        await db.SaveChangesAsync();
        var eligibility = new EfIdentifyTargetEligibilityService(db);

        var results = await eligibility.EvaluateManyAsync(
            [containerId, wantedContainerId],
            CancellationToken.None);

        Assert.Equal(IdentifyTargetEligibilityStatus.Eligible, results[containerId].Status);
        Assert.Equal(IdentifyTargetEligibilityStatus.Wanted, results[wantedContainerId].Status);
    }

    private static EntityRow Entity(
        Guid id,
        bool isWanted,
        DateTimeOffset now,
        Guid? parentEntityId = null) =>
        new() {
            Id = id,
            KindCode = EntityKindRegistry.VideoSeries.Code,
            Title = id.ToString(),
            ParentEntityId = parentEntityId,
            IsWanted = isWanted,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static EntityFileRow File(
        Guid entityId,
        EntityFileRole role,
        string path,
        DateTimeOffset now) =>
        new() {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Role = role,
            Path = path,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"identify-eligibility-{Guid.NewGuid():N}")
            .Options;
        return new PrismediaDbContext(options);
    }
}
