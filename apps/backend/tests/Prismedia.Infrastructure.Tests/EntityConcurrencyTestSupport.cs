using Prismedia.Application.Entities;
using Prismedia.Application.Playback;
using Prismedia.Application.Security;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Entities.Mappers;
using Prismedia.Infrastructure.Entities.Thumbnails;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Playback;

namespace Prismedia.Infrastructure.Tests;

/// <summary>Shared builders and deterministic scheduling controls for Entity concurrency tests.</summary>
internal static class EntityConcurrencyTestSupport {
    internal static EntityCapabilityService CreateService(
        PrismediaDbContext db,
        Guid userId,
        IEntityWriteRepository repository,
        TimeProvider? timeProvider = null) {
        var user = TestUserContext.Admin(userId);
        var reads = new EfEntityReadService(
            db,
            user,
            CreateRepository(db, userId),
            ThumbnailContributors.For(db),
            new EfEntityProgressTopologyResolver(db));
        return new EntityCapabilityService(
            repository,
            reads,
            new EfEntityProgressTopologyResolver(db),
            consumptionEvents: new EfConsumptionEventStore(db, user),
            consumptionActivities: new EfConsumptionActivityStore(db, user),
            timeProvider: timeProvider);
    }

    internal static EfEntityRepository CreateRepository(PrismediaDbContext db, Guid userId) {
        var user = TestUserContext.Admin(userId);
        return new EfEntityRepository(
            db,
            user,
            EntityMappers.Kinds(db, user),
            EntityMappers.Capabilities(db, user));
    }

    internal static async Task SeedAsync(
        PostgresTestDatabase database,
        Guid userId,
        Guid entityId,
        EntityKind kind,
        bool includeState) {
        var now = DateTimeOffset.UtcNow;
        await using var db = database.CreateContext();
        db.Users.Add(new UserRow {
            Id = userId,
            Username = $"user-{userId:N}",
            NormalizedUsername = $"user-{userId:N}",
            DisplayName = "Concurrency Tester",
            Role = UserRole.Admin,
            AllowNsfw = true,
            CanCreateLibraries = true,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Entities.Add(new EntityRow {
            Id = entityId,
            KindCode = kind.ToCode(),
            Title = "Concurrent state",
            CreatedAt = now,
            UpdatedAt = now
        });
        if (includeState) {
            db.UserEntityStates.Add(new UserEntityStateRow {
                UserId = userId,
                EntityId = entityId,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync();
    }

    internal static async Task TouchStateAsync(
        PostgresTestDatabase database,
        Guid userId,
        Guid entityId,
        CancellationToken cancellationToken) {
        await using var context = database.CreateContext();
        var state = await context.UserEntityStates.FindAsync([userId, entityId], cancellationToken);
        Assert.NotNull(state);
        state!.IsFavorite = !state.IsFavorite;
        state.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    internal static async Task SetPlaybackStateAsync(
        PostgresTestDatabase database,
        Guid userId,
        Guid entityId,
        double resumeSeconds,
        DateTimeOffset lastPlayedAt,
        DateTimeOffset? completedAt) {
        await using var context = database.CreateContext();
        var state = await context.UserEntityStates.FindAsync([userId, entityId]);
        Assert.NotNull(state);
        state!.ResumeSeconds = resumeSeconds;
        state.LastActiveAt = lastPlayedAt;
        state.CompletedAt = completedAt;
        state.UpdatedAt = lastPlayedAt;
        await context.SaveChangesAsync();
    }

    internal static async Task SetProgressStateAsync(
        PostgresTestDatabase database,
        Guid userId,
        Guid entityId,
        int index,
        int total,
        DateTimeOffset updatedAt,
        DateTimeOffset? completedAt) {
        await using var context = database.CreateContext();
        var state = await context.UserEntityStates.FindAsync([userId, entityId]);
        Assert.NotNull(state);
        state!.ProgressCurrentEntityId = entityId;
        state.ProgressUnit = ProgressUnit.Page.ToCode();
        state.ProgressIndex = index;
        state.ProgressTotal = total;
        state.ProgressMode = ReaderMode.Paged.ToCode();
        state.ProgressCompletedAt = completedAt;
        state.ProgressUpdatedAt = updatedAt;
        state.UpdatedAt = updatedAt;
        await context.SaveChangesAsync();
    }

    internal sealed class GatedEntityWriteRepository(
        IEntityWriteRepository inner,
        SaveBarrier gate) : IEntityWriteRepository {
        public IEntityWriteAttempt BeginAttempt() => inner.BeginAttempt();

        public Task<Entity?> FindShallowAsync(Guid id, CancellationToken cancellationToken) =>
            inner.FindShallowAsync(id, cancellationToken);

        public Task<Guid?> FindParentIdAsync(Guid id, CancellationToken cancellationToken) =>
            inner.FindParentIdAsync(id, cancellationToken);

        public async Task SaveMutableStateAsync(
            Entity entity,
            EntityMutableStateChange change,
            CancellationToken cancellationToken) {
            await gate.WaitForFirstSaveAsync(cancellationToken);
            await inner.SaveMutableStateAsync(entity, change, cancellationToken);
        }
    }

    internal sealed class ConflictOnceEntityWriteRepository(
        IEntityWriteRepository inner,
        Func<CancellationToken, Task> beforeFirstSaveAsync) : IEntityWriteRepository {
        private int _hasForcedConflict;

        public IEntityWriteAttempt BeginAttempt() => inner.BeginAttempt();

        public Task<Entity?> FindShallowAsync(Guid id, CancellationToken cancellationToken) =>
            inner.FindShallowAsync(id, cancellationToken);

        public Task<Guid?> FindParentIdAsync(Guid id, CancellationToken cancellationToken) =>
            inner.FindParentIdAsync(id, cancellationToken);

        public async Task SaveMutableStateAsync(
            Entity entity,
            EntityMutableStateChange change,
            CancellationToken cancellationToken) {
            if (Interlocked.Exchange(ref _hasForcedConflict, 1) == 0) {
                await beforeFirstSaveAsync(cancellationToken);
            }

            await inner.SaveMutableStateAsync(entity, change, cancellationToken);
        }
    }

    internal sealed class PausedFirstSaveEntityWriteRepository(
        IEntityWriteRepository inner,
        SavePause pause) : IEntityWriteRepository {
        private int _hasPaused;

        public IEntityWriteAttempt BeginAttempt() => inner.BeginAttempt();

        public Task<Entity?> FindShallowAsync(Guid id, CancellationToken cancellationToken) =>
            inner.FindShallowAsync(id, cancellationToken);

        public Task<Guid?> FindParentIdAsync(Guid id, CancellationToken cancellationToken) =>
            inner.FindParentIdAsync(id, cancellationToken);

        public async Task SaveMutableStateAsync(
            Entity entity,
            EntityMutableStateChange change,
            CancellationToken cancellationToken) {
            if (Interlocked.Exchange(ref _hasPaused, 1) == 0) {
                await pause.PauseAsync(cancellationToken);
            }

            await inner.SaveMutableStateAsync(entity, change, cancellationToken);
        }
    }

    internal sealed class SaveBarrier(int requiredArrivals) {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        internal async Task WaitForFirstSaveAsync(CancellationToken cancellationToken) {
            if (Interlocked.Increment(ref _arrivals) == requiredArrivals) {
                _release.TrySetResult();
            }

            await _release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }

    internal sealed class SavePause {
        private readonly TaskCompletionSource _paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal async Task PauseAsync(CancellationToken cancellationToken) {
            _paused.TrySetResult();
            await _release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }

        internal Task WaitUntilPausedAsync() =>
            _paused.Task.WaitAsync(TimeSpan.FromSeconds(10));

        internal void Release() => _release.TrySetResult();
    }

    internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
