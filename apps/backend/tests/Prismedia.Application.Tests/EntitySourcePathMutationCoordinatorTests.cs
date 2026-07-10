using Prismedia.Application.Entities;
using Prismedia.Application.Files;

namespace Prismedia.Application.Tests;

public sealed class EntitySourcePathMutationCoordinatorTests {
    [Fact]
    public async Task AddedScannerOwnerIsIncludedBeforeFilesystemMutationRuns() {
        var firstOwnerId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var scannerOwnerId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var owners = new SequencedOwnerReader([
            [firstOwnerId],
            [firstOwnerId, scannerOwnerId],
            [firstOwnerId, scannerOwnerId],
            [firstOwnerId, scannerOwnerId],
        ]);
        var lifecycle = new RecordingLifecycleLease();
        var coordinator = new EntitySourcePathMutationCoordinator(owners, lifecycle);
        var mutations = 0;

        var executed = await coordinator.ExecuteAsync(
            "/media/series",
            _ => {
                mutations++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.True(executed);
        Assert.Equal(1, mutations);
        Assert.Collection(
            lifecycle.EntityIdBatches,
            batch => Assert.Equal([firstOwnerId], batch),
            batch => Assert.Equal([firstOwnerId, scannerOwnerId], batch));
    }

    [Fact]
    public async Task MissingRequiredOwnerRejectsStaleOrganizePlanWithoutMutation() {
        var coordinator = new EntitySourcePathMutationCoordinator(
            new SequencedOwnerReader([[]]),
            new RecordingLifecycleLease());
        var mutations = 0;

        var executed = await coordinator.ExecuteAsync(
            "/media/old-name.mkv",
            [Guid.Parse("20000000-0000-0000-0000-000000000001")],
            _ => {
                mutations++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.False(executed);
        Assert.Equal(0, mutations);
    }

    private sealed class SequencedOwnerReader(IReadOnlyList<Guid[]> snapshots)
        : IEntitySourcePathOwnerReader {
        private int _index;

        public Task<IReadOnlySet<Guid>> ListDirectOwnerIdsAsync(
            string physicalPath,
            CancellationToken cancellationToken) {
            var snapshot = snapshots[Math.Min(_index, snapshots.Count - 1)];
            _index++;
            return Task.FromResult<IReadOnlySet<Guid>>(snapshot.ToHashSet());
        }
    }

    private sealed class RecordingLifecycleLease : IEntityLifecycleMutationLease {
        public List<Guid[]> EntityIdBatches { get; } = [];

        public Task<bool> ExecuteAsync(
            Guid entityId,
            Func<CancellationToken, Task> mutation,
            CancellationToken cancellationToken) =>
            ExecuteManyAsync([entityId], mutation, cancellationToken);

        public async Task<bool> ExecuteManyAsync(
            IReadOnlyCollection<Guid> entityIds,
            Func<CancellationToken, Task> mutation,
            CancellationToken cancellationToken) {
            EntityIdBatches.Add(entityIds.Order().ToArray());
            await mutation(cancellationToken);
            return true;
        }
    }
}
