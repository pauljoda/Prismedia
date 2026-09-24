using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Durable bindings, transactional publication, and identity-preserving source persistence.</summary>
public interface IManagedTrackingStore {
    #region Abstract Methods

    /// <summary>Loads retained intent and current source bindings for one operation.</summary>
    Task<ManagedTrackingWork?> FindAsync(Guid id, CancellationToken token);

    /// <summary>Lists up to 1,000 retained holdings without contacting the external application.</summary>
    Task<IReadOnlyList<ManagedTrackingResponse>> ListAsync(Guid connectionId, CancellationToken token);

    /// <summary>Maps fresh file claims and reads their existing local owners without changing files.</summary>
    Task<ManagedTrackingObservation> ObserveAsync(Guid connectionId, ManagedItemSnapshot snapshot, CancellationToken token);

    /// <summary>Atomically stores reviewed intent and publishes its first queue run.</summary>
    Task<ManagedTrackingResponse> CreateAsync(Guid connectionId, TrackManagedHoldingRequest request, string title, CancellationToken token);

    /// <summary>Revalidates observed bytes and commits a domain decision under source-owner lifecycle leases.</summary>
    Task ApplyAsync(ManagedTrackingWork work, ManagedTrackingObservation observation, IReadOnlyList<ManagedFileBinding>? adoption,
        IReadOnlyList<ManagedSourceChange> changes, CancellationToken token);

    /// <summary>Atomically records provider-confirmed removal while retaining local identities, readable files, and the
    /// fulfillment fence.</summary>
    Task ConfirmRemovalAsync(ManagedTrackingWork work, string problem, CancellationToken token);

    /// <summary>Holds the holding for an explicit decision and withdraws all of its source availability without deleting
    /// identities.</summary>
    Task RequireReviewAsync(Guid id, long revision, string problem, CancellationToken token);

    /// <summary>Records an unverifiable connection, withdrawing only sources that are no longer readable.</summary>
    Task RecordUnverifiableAsync(Guid id, long revision, CancellationToken token);

    /// <summary>Records that a removed remote identity reappeared, keeping the confirmed removal and still-readable local bytes.</summary>
    Task RecordReappearanceAsync(Guid id, long revision, string problem, CancellationToken token);

    /// <summary>Publishes an explicit finite refresh for a holding owned by this connection.</summary>
    Task QueueAsync(Guid connectionId, Guid id, CancellationToken token);

    /// <summary>Recovers due observations independently of transient queue history.</summary>
    Task QueueDueAsync(CancellationToken token);

    #endregion
}
