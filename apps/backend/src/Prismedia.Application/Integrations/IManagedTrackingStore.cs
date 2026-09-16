using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Existing connected scope and the explicit selections accepted before any worker effects.</summary>
public sealed record ManagedTrackingWork(ManagedTrackingResponse Tracking, IReadOnlyList<ManagedBindingSelection> Selections);
/// <summary>Independently mapped bytes and existing local source owners observed for a connected holding.</summary>
public sealed record ManagedTrackingObservation(Guid? LibraryRootId, IReadOnlyList<ManagedObservedFile> Files,
    IReadOnlyList<ManagedLocalSource> Sources);

/// <summary>Durable bindings, transactional publication, and identity-preserving source persistence.</summary>
public interface IManagedTrackingStore {
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
    /// <summary>Retains failure evidence and withdraws unverified source availability without deleting identities.</summary>
    Task RecordProblemAsync(Guid id, long revision, ManagedTrackingStatus status, string problem, CancellationToken token);
    /// <summary>Publishes an explicit finite refresh for a holding owned by this connection.</summary>
    Task QueueAsync(Guid connectionId, Guid id, CancellationToken token);
    /// <summary>Recovers due observations independently of transient queue history.</summary>
    Task QueueDueAsync(CancellationToken token);
}
