using Prismedia.Contracts.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Serializes handoff with control dispatch, reconciliation and native ownership acquisition.</summary>
public interface IManagedReleaseStore {
    #region Abstract Methods

    /// <summary>Reads retained release intent, including terminal history.</summary>
    Task<ManagedReleaseWork?> FindAsync(Guid holdingId, CancellationToken token);

    /// <summary>Rejects unfinished or unverified manager writes before handoff can freeze the scope.</summary>
    Task RequireSettledControlsAsync(Guid holdingId, CancellationToken token);

    /// <summary>Atomically freezes host controls and file reconciliation and queues a fresh remote observation.</summary>
    Task<ManagedTrackingResponse> BeginAsync(Guid connectionId, Guid holdingId, ReleaseManagedHoldingRequest request,
        CancellationToken token);

    /// <summary>Archives source associations, retains files and identities, and releases ownership in one transaction.</summary>
    Task CompleteAsync(ManagedReleaseWork work, ManagedReleaseObservation evidence, CancellationToken token);

    /// <summary>Retains ownership and schedules another finite observation after failed or incomplete evidence.</summary>
    Task RecordProblemAsync(ManagedReleaseWork work, string problem, CancellationToken token);

    #endregion
}
