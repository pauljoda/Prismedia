using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;
using static Prismedia.Infrastructure.Tests.EntityConcurrencyTestSupport;

namespace Prismedia.Infrastructure.Tests;

/// <summary>PostgreSQL regressions for aggregate-root and retry-boundary concurrency.</summary>
public sealed class EntityAggregateConcurrencyPostgresTests {
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ConcurrentGlobalFlagPatchesWithoutUserStatePreserveBothFlags() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var userId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        await SeedAsync(database, userId, entityId, EntityKind.Video, includeState: false);

        await using var nsfwContext = database.CreateContext();
        await using var organizedContext = database.CreateContext();
        var pause = new SavePause();
        var nsfwService = CreateService(
            nsfwContext,
            userId,
            new PausedFirstSaveEntityWriteRepository(CreateRepository(nsfwContext, userId), pause));
        var organizedService = CreateService(
            organizedContext,
            userId,
            CreateRepository(organizedContext, userId));

        var staleNsfwWrite = nsfwService.UpdateFlagsAsync(
            entityId, null, true, null, CancellationToken.None);
        await pause.WaitUntilPausedAsync();
        try {
            await organizedService.UpdateFlagsAsync(
                entityId, null, null, true, CancellationToken.None);
        } finally {
            pause.Release();
        }
        await staleNsfwWrite;

        await using var verification = database.CreateContext();
        var entity = await verification.Entities.SingleAsync(row => row.Id == entityId);
        Assert.True(entity.IsNsfw);
        Assert.True(entity.IsOrganized);
        Assert.Empty(await verification.UserEntityStates
            .Where(row => row.UserId == userId && row.EntityId == entityId)
            .ToArrayAsync());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ConcurrentMarkerAddsWithoutUserStatePreserveBothMarkers() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var userId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        await SeedAsync(database, userId, entityId, EntityKind.Video, includeState: false);

        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var pause = new SavePause();
        var firstService = CreateService(
            firstContext,
            userId,
            new PausedFirstSaveEntityWriteRepository(CreateRepository(firstContext, userId), pause));
        var secondService = CreateService(
            secondContext,
            userId,
            CreateRepository(secondContext, userId));

        var staleOpeningWrite = firstService.AddMarkerAsync(
            entityId, "Opening", 5, null, CancellationToken.None);
        await pause.WaitUntilPausedAsync();
        try {
            await secondService.AddMarkerAsync(
                entityId, "Credits", 95, null, CancellationToken.None);
        } finally {
            pause.Release();
        }
        await staleOpeningWrite;

        await using var verification = database.CreateContext();
        var markers = await verification.EntityMarkers
            .Where(row => row.EntityId == entityId)
            .OrderBy(row => row.Seconds)
            .ToArrayAsync();
        Assert.Equal(["Opening", "Credits"], markers.Select(row => row.Title));
        Assert.Empty(await verification.UserEntityStates
            .Where(row => row.UserId == userId && row.EntityId == entityId)
            .ToArrayAsync());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task AttemptRollbackRestoresAnUnrelatedDeletionAcceptedByTheIntermediateSave() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var userId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var unrelatedEntityId = Guid.NewGuid();
        await SeedAsync(database, userId, entityId, EntityKind.Video, includeState: true);
        await using (var setup = database.CreateContext()) {
            var now = DateTimeOffset.UtcNow;
            setup.Entities.Add(new EntityRow {
                Id = unrelatedEntityId,
                KindCode = EntityKind.Image.ToCode(),
                Title = "Unrelated staged deletion",
                CreatedAt = now,
                UpdatedAt = now
            });
            await setup.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var unrelated = await context.Entities.SingleAsync(row => row.Id == unrelatedEntityId);
        context.Entities.Remove(unrelated);
        var repository = CreateRepository(context, userId);
        using var attempt = repository.BeginAttempt();
        var target = await repository.FindShallowAsync(entityId, CancellationToken.None);
        Assert.NotNull(target);
        target!.Rate(5);
        await TouchStateAsync(database, userId, entityId, CancellationToken.None);

        await Assert.ThrowsAsync<EntityConcurrencyConflictException>(() =>
            repository.SaveAsync(target, CancellationToken.None));
        await attempt.RollbackAsync(CancellationToken.None);

        Assert.Equal(EntityState.Deleted, context.Entry(unrelated).State);
        await context.SaveChangesAsync();

        await using var verification = database.CreateContext();
        Assert.Null(await verification.Entities.SingleOrDefaultAsync(row => row.Id == unrelatedEntityId));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task AttemptRollbackPreservesAnUnrelatedPartialUpdateMask() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var userId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        await SeedAsync(database, userId, entityId, EntityKind.Video, includeState: true);

        await using var context = database.CreateContext();
        var unrelatedUser = await context.Users.SingleAsync(row => row.Id == userId);
        unrelatedUser.DisplayName = "Staged display name";
        var repository = CreateRepository(context, userId);
        using var attempt = repository.BeginAttempt();
        var target = await repository.FindShallowAsync(entityId, CancellationToken.None);
        Assert.NotNull(target);
        target!.Rate(5);
        await TouchStateAsync(database, userId, entityId, CancellationToken.None);

        await Assert.ThrowsAsync<EntityConcurrencyConflictException>(() =>
            repository.SaveAsync(target, CancellationToken.None));
        await attempt.RollbackAsync(CancellationToken.None);

        await using (var siblingUpdate = database.CreateContext()) {
            var user = await siblingUpdate.Users.SingleAsync(row => row.Id == userId);
            user.CanCreateLibraries = false;
            await siblingUpdate.SaveChangesAsync();
        }
        await context.SaveChangesAsync();

        await using var verification = database.CreateContext();
        var saved = await verification.Users.SingleAsync(row => row.Id == userId);
        Assert.Equal("Staged display name", saved.DisplayName);
        Assert.False(saved.CanCreateLibraries);
    }
}
