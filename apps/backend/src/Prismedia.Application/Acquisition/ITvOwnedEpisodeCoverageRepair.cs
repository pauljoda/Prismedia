namespace Prismedia.Application.Acquisition;

/// <summary>Restores missing episode links from confidently identified, previously imported shared files.</summary>
public interface ITvOwnedEpisodeCoverageRepair {
    /// <summary>
    /// Repairs an actively monitored season while preserving existing owners and bytes. The callback
    /// publishes normal reconciliation work in the same transaction as each restored source binding.
    /// Returns the number of restored episodes; ambiguous or superseded evidence is left unchanged.
    /// </summary>
    Task<int> RepairAsync(Guid monitorId, Guid seasonId,
        Func<Guid, CancellationToken, Task> enqueueReconciliation, CancellationToken cancellationToken);
}
