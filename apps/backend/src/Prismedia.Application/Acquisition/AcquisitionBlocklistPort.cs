using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Persistence port for the acquisition blocklist: release identities refused for future grabs. Consulted
/// by the search runner (to reject blocklisted releases) and written by failed-download auto-recovery and
/// manual blocking.
/// </summary>
public interface IAcquisitionBlocklistStore {
    /// <summary>Returns every blocklisted release identity, for the decision engine's blocklist gate.</summary>
    Task<IReadOnlySet<string>> GetIdentitiesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adds a release identity to the blocklist. Idempotent and first-reason-wins: if the identity is
    /// already present its existing reason, message, and timestamp are kept and the request is a no-op
    /// (so an automatic <see cref="BlocklistReason.Failed"/> entry is not overwritten by a later add).
    /// </summary>
    Task AddAsync(BlocklistAddRequest request, CancellationToken cancellationToken);

    /// <summary>Lists blocklist entries newest-first for the management surface.</summary>
    Task<IReadOnlyList<Contracts.Acquisition.AcquisitionBlocklistEntry>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Removes a blocklist entry by id. Returns false when it no longer exists.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Removes every entry matching an optional library Entity and creation-time lower bound. Null
    /// filters clear the complete blocklist. Returns the number of releases allowed again.
    /// </summary>
    Task<int> ClearAsync(
        Guid? entityId,
        DateTimeOffset? createdAfter,
        CancellationToken cancellationToken) {
        return ClearMatchingEntriesAsync(entityId, createdAfter, cancellationToken);

        async Task<int> ClearMatchingEntriesAsync(
            Guid? scopedEntityId,
            DateTimeOffset? cutoff,
            CancellationToken token) {
            var entries = await ListAsync(token);
            var removed = 0;
            foreach (var entry in entries
                .Where(entry => scopedEntityId is null || entry.EntityId == scopedEntityId)
                .Where(entry => cutoff is null || entry.CreatedAt >= cutoff)) {
                if (await DeleteAsync(entry.Id, token)) {
                    removed++;
                }
            }
            return removed;
        }
    }
}
