using Prismedia.Application.Entities;

namespace Prismedia.Application.Files;

/// <summary>
/// Resolves Entities that directly own source media at or beneath a physical filesystem path.
/// Folder moves and deletes use this projection to coordinate every affected Entity subtree at once.
/// </summary>
public interface IEntitySourcePathOwnerReader {
    /// <summary>
    /// Returns direct Entity source owners whose physical source is the supplied path or a descendant.
    /// Archive-member sources resolve through their physical archive owner.
    /// </summary>
    Task<IReadOnlySet<Guid>> ListDirectOwnerIdsAsync(
        string physicalPath,
        CancellationToken cancellationToken);
}

/// <summary>
/// Serializes linked filesystem mutations with every affected Entity lifecycle. Ownership is re-read
/// after the batch lease is acquired, so a scanner-published owner can be included before disk state
/// changes and an Entity whose source moved meanwhile cannot be organized from a stale plan.
/// </summary>
public sealed class EntitySourcePathMutationCoordinator(
    IEntitySourcePathOwnerReader owners,
    IEntityLifecycleMutationLease lifecycle) {
    /// <summary>
    /// Executes one filesystem mutation after leasing all current direct source owners. Unlinked paths
    /// execute immediately because they have no Entity lifecycle with which to coordinate.
    /// </summary>
    public Task<bool> ExecuteAsync(
        string physicalPath,
        Func<CancellationToken, Task> mutation,
        CancellationToken cancellationToken) =>
        ExecuteAsync(physicalPath, [], mutation, cancellationToken);

    /// <summary>
    /// Executes one filesystem mutation when all required direct owners still own the path and every
    /// affected owner accepts the shared lifecycle lease. Returns false without mutating on conflict.
    /// </summary>
    public async Task<bool> ExecuteAsync(
        string physicalPath,
        IReadOnlyCollection<Guid> requiredOwnerIds,
        Func<CancellationToken, Task> mutation,
        CancellationToken cancellationToken) {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(physicalPath));
        var required = requiredOwnerIds.Distinct().ToHashSet();

        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            var ownerIds = await owners.ListDirectOwnerIdsAsync(path, cancellationToken);
            if (!required.IsSubsetOf(ownerIds)) {
                return false;
            }

            if (ownerIds.Count == 0) {
                await mutation(cancellationToken);
                return true;
            }

            var ownershipChanged = false;
            var mutationExecuted = false;
            var acquired = await lifecycle.ExecuteManyAsync(
                ownerIds,
                async token => {
                    var revalidatedOwnerIds = await owners.ListDirectOwnerIdsAsync(path, token);
                    if (!required.IsSubsetOf(revalidatedOwnerIds)
                        || !ownerIds.SetEquals(revalidatedOwnerIds)) {
                        ownershipChanged = true;
                        return;
                    }

                    await mutation(token);
                    mutationExecuted = true;
                },
                cancellationToken);
            if (!acquired) {
                return false;
            }

            if (mutationExecuted) {
                return true;
            }

            if (!ownershipChanged) {
                return false;
            }
        }
    }
}
