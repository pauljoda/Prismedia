using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>
/// Owns creation uncertainty and availability transitions without conflating search success with imported
/// bytes. What each phase permits comes from its <see cref="ManagedRequestPhaseDefinition"/>.
/// </summary>
public sealed class ManagedRequestOperation {
    #region Static Variables

    private const int MaximumRemoteIdLength = 512;

    #endregion

    #region Variables

    /// <summary>Immutable progress saved at every external boundary using revision comparison.</summary>
    public ManagedRequestState State { get; private set; }

    /// <summary>Behavior of the current phase.</summary>
    public ManagedRequestPhaseDefinition Phase => ManagedRequestPhaseDefinition.For(State.Phase);

    /// <summary>Whether a future observation may advance fulfillment.</summary>
    public bool IsActive => Phase.IsActive;

    /// <summary>Cancellation is safe only before dispatch or after a definite creation refusal.</summary>
    public bool CanCancel => Phase.CanCancel;

    #endregion

    #region Constructors

    /// <summary>Rehydrates persisted progress, refusing a state that no transition could have produced.</summary>
    /// <exception cref="ArgumentException">The state lacks its identities, revision, or pinned remote holding.</exception>
    public ManagedRequestOperation(ManagedRequestState state) {
        ArgumentNullException.ThrowIfNull(state);
        if (state.OperationId == Guid.Empty || state.ConnectionId == Guid.Empty || state.EntityId == Guid.Empty
            || state.LibraryRootId == Guid.Empty || state.Revision < 1) {
            throw new ArgumentException("A managed request requires stable identities and a positive revision.", nameof(state));
        }

        if (ManagedRequestPhaseDefinition.For(state.Phase).HoldsRemoteIdentity && string.IsNullOrWhiteSpace(state.RemoteId)) {
            throw new ArgumentException("A managed request with an accepted holding must retain its remote identifier.", nameof(state));
        }

        State = state;
    }

    #endregion

    #region Actions - Creation

    /// <summary>Accepts a stable wanted identity and mapped root before any remote side effect.</summary>
    public static ManagedRequestOperation Create(Guid id, Guid connectionId, Guid entityId, Guid libraryRootId) {
        if (id == Guid.Empty || connectionId == Guid.Empty || entityId == Guid.Empty || libraryRootId == Guid.Empty) {
            throw new ArgumentException("A managed request requires stable request, connection, entity, and library identities.");
        }

        return new(new(id, connectionId, entityId, libraryRootId, 1, ManagedRequestPhase.PendingCreation));
    }

    /// <summary>Must be committed before creation. An uncertain operation can never dispatch itself again.</summary>
    public void BeginCreation() {
        Require(State.Phase == ManagedRequestPhase.PendingCreation, "begin remote creation");
        Change(State with { Phase = ManagedRequestPhase.CreationUncertain, ReviewRequired = false });
    }

    /// <summary>Accepts exact-identity evidence, including an existing holding or recovery after a lost response.</summary>
    /// <exception cref="ArgumentException">The remote identifier is empty, too long, or contains control characters.</exception>
    public void AcceptHolding(string remoteId) {
        Require(Phase.AwaitsHolding, "accept a remote holding");
        if (string.IsNullOrWhiteSpace(remoteId) || remoteId.Length > MaximumRemoteIdLength || remoteId.Any(char.IsControl)) {
            throw new ArgumentException("The manager returned an unusable remote holding identifier.", nameof(remoteId));
        }

        Change(State with { Phase = ManagedRequestPhase.AwaitingFiles, RemoteId = remoteId, ReviewRequired = false });
    }

    /// <summary>A definite creation refusal retains intent for review; it makes cancellation safe.</summary>
    public void RejectCreation() {
        Require(State.Phase == ManagedRequestPhase.CreationUncertain, "record a creation refusal");
        Change(State with { Phase = ManagedRequestPhase.Rejected, ReviewRequired = true });
    }

    #endregion

    #region Actions - Files

    /// <summary>Only the committed exact local source binding may complete fulfillment.</summary>
    public void ConfirmFiles() {
        Require(Phase.AwaitsFiles, "confirm its files");
        Change(State with { Phase = ManagedRequestPhase.Completed, ReviewRequired = false });
    }

    /// <summary>A fresh valid observation may keep waiting for bytes without changing the accepted remote identity.</summary>
    public void ContinueWaiting() {
        Require(Phase.AwaitsFiles, "keep waiting for files");
        Change(State with { ReviewRequired = false });
    }

    /// <summary>Records a retryable observation failure without removing an existing review fence.</summary>
    public void RecordRetryableObservation() {
        Require(Phase.AwaitsFiles, "record a retryable observation");
        Change(State);
    }

    #endregion

    #region Actions - Ownership

    /// <summary>Records provider-confirmed absence without releasing fulfillment ownership or erasing the pinned remote identity.</summary>
    public void ConfirmRemoteRemoval() {
        Require(Phase.HoldsRemoteIdentity, "record its holding's removal");
        Change(State with { Phase = ManagedRequestPhase.RemoteRemoved, ReviewRequired = false });
    }

    /// <summary>Stops fulfillment only after the associated holding has completed its explicit handoff.</summary>
    public void ReleaseOwnership() {
        Require(Phase.HoldsRemoteIdentity, "release its ownership");
        Change(State with { Phase = ManagedRequestPhase.OwnershipReleased, ReviewRequired = false });
    }

    /// <summary>Pauses automatic observation without releasing ownership or making a mutation repeatable.</summary>
    public void RequireReview() {
        Require(Phase.IsActive, "be held for review");
        Change(State with { ReviewRequired = true });
    }

    /// <summary>Stops an intent that has no possible remote creation effect; release its reservation in the same transaction.</summary>
    public void Cancel() {
        Require(Phase.CanCancel, "be cancelled");
        Change(State with { Phase = ManagedRequestPhase.Cancelled, ReviewRequired = false });
    }

    #endregion

    #region Actions - Transitions

    private void Require(bool allowed, string action) {
        if (!allowed) {
            throw new InvalidOperationException($"A managed request that is {Phase.Description} cannot {action}.");
        }
    }

    private void Change(ManagedRequestState next) => State = next with { Revision = State.Revision + 1 };

    #endregion
}
