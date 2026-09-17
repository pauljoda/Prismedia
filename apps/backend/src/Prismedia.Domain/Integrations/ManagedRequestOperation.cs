using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>Rehydratable progress for one wanted work, its connection, and its immutable library boundary.</summary>
public sealed record ManagedRequestState(Guid OperationId, Guid ConnectionId, Guid EntityId, Guid LibraryRootId,
    long Revision, ManagedRequestPhase Phase, string? RemoteId = null, bool ReviewRequired = false);

/// <summary>Owns creation uncertainty and availability transitions without conflating search success with imported bytes.</summary>
public sealed class ManagedRequestOperation(ManagedRequestState state) {
    /// <summary>Immutable progress saved at every external boundary using revision comparison.</summary>
    public ManagedRequestState State { get; private set; } = state;
    /// <summary>Whether a future observation may advance fulfillment.</summary>
    public bool IsActive => State.Phase is ManagedRequestPhase.PendingCreation or ManagedRequestPhase.CreationUncertain or ManagedRequestPhase.AwaitingFiles;
    /// <summary>Cancellation is safe only before dispatch or after a definite creation refusal.</summary>
    public bool CanCancel => State.Phase is ManagedRequestPhase.PendingCreation or ManagedRequestPhase.Rejected;
    /// <summary>Accepts a stable wanted identity and mapped root before any remote side effect.</summary>
    public static ManagedRequestOperation Create(Guid id, Guid connectionId, Guid entityId, Guid libraryRootId) {
        if (id == Guid.Empty || connectionId == Guid.Empty || entityId == Guid.Empty || libraryRootId == Guid.Empty)
            throw new ArgumentException("A managed request requires stable request, connection, entity, and library identities.");
        return new(new(id, connectionId, entityId, libraryRootId, 1, ManagedRequestPhase.PendingCreation));
    }
    /// <summary>Must be committed before creation. An uncertain operation can never dispatch itself again.</summary>
    public void BeginCreation() {
        Require(ManagedRequestPhase.PendingCreation);
        Change(State with { Phase = ManagedRequestPhase.CreationUncertain, ReviewRequired = false });
    }
    /// <summary>Accepts exact-identity evidence, including an existing holding or recovery after a lost response.</summary>
    public void AcceptHolding(string remoteId) {
        if (State.Phase is not (ManagedRequestPhase.PendingCreation or ManagedRequestPhase.CreationUncertain)
            || string.IsNullOrWhiteSpace(remoteId) || remoteId.Length > 512 || remoteId.Any(char.IsControl)) throw Invalid();
        Change(State with { Phase = ManagedRequestPhase.AwaitingFiles, RemoteId = remoteId, ReviewRequired = false });
    }
    /// <summary>Only the committed exact local source binding may complete fulfillment.</summary>
    public void ConfirmFiles() {
        Require(ManagedRequestPhase.AwaitingFiles);
        Change(State with { Phase = ManagedRequestPhase.Completed, ReviewRequired = false });
    }
    /// <summary>A fresh valid observation may keep waiting for bytes without changing the accepted remote identity.</summary>
    public void ContinueWaiting() {
        Require(ManagedRequestPhase.AwaitingFiles);
        Change(State with { ReviewRequired = false });
    }
    /// <summary>A definite creation refusal retains intent for review; it makes cancellation safe.</summary>
    public void RejectCreation() {
        Require(ManagedRequestPhase.CreationUncertain);
        Change(State with { Phase = ManagedRequestPhase.Rejected, ReviewRequired = true });
    }
    /// <summary>Pauses automatic observation without releasing ownership or making a mutation repeatable.</summary>
    public void RequireReview() {
        if (!IsActive) throw Invalid();
        Change(State with { ReviewRequired = true });
    }
    /// <summary>Stops an intent that has no possible remote creation effect; release its reservation in the same transaction.</summary>
    public void Cancel() {
        if (!CanCancel) throw Invalid();
        Change(State with { Phase = ManagedRequestPhase.Cancelled, ReviewRequired = false });
    }
    private void Require(ManagedRequestPhase phase) { if (State.Phase != phase) throw Invalid(); }
    private void Change(ManagedRequestState next) => State = next with { Revision = State.Revision + 1 };
    private static InvalidOperationException Invalid() => new("This managed request cannot make that transition at its current phase.");
}
