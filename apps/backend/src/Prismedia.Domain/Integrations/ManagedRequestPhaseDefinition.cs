using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>
/// The behavior of one <see cref="ManagedRequestPhase"/>. The enum stays the persisted and generated
/// identity; each definition declares what a request in that phase may do, so services, stores, and
/// projections ask the phase instead of repeating their own lists of phases.
/// </summary>
public sealed class ManagedRequestPhaseDefinition {
    #region Static Variables

    /// <summary>No remote creation call has been dispatched.</summary>
    public static readonly ManagedRequestPhaseDefinition PendingCreation = new(ManagedRequestPhase.PendingCreation,
        "waiting to create its remote holding", isActive: true, canCancel: true, awaitsHolding: true,
        holdsFulfillment: true);

    /// <summary>A creation may have happened; only an exact identity observation can resolve it.</summary>
    public static readonly ManagedRequestPhaseDefinition CreationUncertain = new(ManagedRequestPhase.CreationUncertain,
        "unsure whether its remote holding was created", isActive: true, awaitsHolding: true, holdsFulfillment: true,
        needsAttention: true);

    /// <summary>A remote holding exists; its requested files have not yet been bound locally.</summary>
    public static readonly ManagedRequestPhaseDefinition AwaitingFiles = new(ManagedRequestPhase.AwaitingFiles,
        "waiting for files", isActive: true, awaitsFiles: true, holdsRemoteIdentity: true, providesHolding: true,
        holdsFulfillment: true);

    /// <summary>The manager no longer contains the accepted holding; ownership stays fenced for explicit release.</summary>
    public static readonly ManagedRequestPhaseDefinition RemoteRemoved = new(ManagedRequestPhase.RemoteRemoved,
        "removed from its manager", isActive: true, holdsRemoteIdentity: true, holdsFulfillment: true, isSettled: true);

    /// <summary>The exact requested local identities own verified mapped files.</summary>
    public static readonly ManagedRequestPhaseDefinition Completed = new(ManagedRequestPhase.Completed,
        "completed", holdsRemoteIdentity: true, providesHolding: true, holdsFulfillment: true, isSettled: true);

    /// <summary>Creation was definitely refused; ownership remains until the request is cancelled.</summary>
    public static readonly ManagedRequestPhaseDefinition Rejected = new(ManagedRequestPhase.Rejected,
        "refused by its manager", canCancel: true, holdsFulfillment: true, needsAttention: true);

    /// <summary>Intent was cancelled before any possible remote creation effect.</summary>
    public static readonly ManagedRequestPhaseDefinition Cancelled = new(ManagedRequestPhase.Cancelled,
        "cancelled", isSettled: true);

    /// <summary>The associated holding completed an explicit ownership handoff.</summary>
    public static readonly ManagedRequestPhaseDefinition OwnershipReleased = new(ManagedRequestPhase.OwnershipReleased,
        "released to its manager", isSettled: true);

    /// <summary>Every phase definition, one per <see cref="ManagedRequestPhase"/> member.</summary>
    public static IReadOnlyList<ManagedRequestPhaseDefinition> All { get; } = [
        PendingCreation,
        CreationUncertain,
        AwaitingFiles,
        RemoteRemoved,
        Completed,
        Rejected,
        Cancelled,
        OwnershipReleased
    ];

    /// <summary>Phases in which a future observation may still advance fulfillment.</summary>
    public static IReadOnlyList<ManagedRequestPhase> Active { get; } = Codes(phase => phase.IsActive);

    /// <summary>Phases whose request still holds its fulfillment reservation.</summary>
    public static IReadOnlyList<ManagedRequestPhase> HoldingFulfillment { get; } = Codes(phase => phase.HoldsFulfillment);

    /// <summary>Phases that retain an accepted remote holding identity.</summary>
    public static IReadOnlyList<ManagedRequestPhase> HoldingRemoteIdentity { get; } = Codes(phase => phase.HoldsRemoteIdentity);

    /// <summary>Phases whose accepted holding is present and can receive another finite target.</summary>
    public static IReadOnlyList<ManagedRequestPhase> ProvidingHolding { get; } = Codes(phase => phase.ProvidesHolding);

    /// <summary>Phases still working toward files: creation is unsettled or files have not arrived.</summary>
    public static IReadOnlyList<ManagedRequestPhase> InFlight { get; } = Codes(phase => phase.AwaitsHolding || phase.AwaitsFiles);

    /// <summary>Phases whose remote creation is not yet settled.</summary>
    public static IReadOnlyList<ManagedRequestPhase> AwaitingHolding { get; } = Codes(phase => phase.AwaitsHolding);

    /// <summary>Phases that no longer change without an explicit new decision.</summary>
    public static IReadOnlyList<ManagedRequestPhase> Settled { get; } = Codes(phase => phase.IsSettled);

    /// <summary>Phases that need a person to look at them even without a review fence.</summary>
    public static IReadOnlyList<ManagedRequestPhase> NeedingAttention { get; } = Codes(phase => phase.NeedsAttention);

    #endregion

    #region Variables

    /// <summary>Persisted and generated identity of this phase.</summary>
    public ManagedRequestPhase Phase { get; }

    /// <summary>Plain description used in transition errors, completing "A managed request that is …".</summary>
    public string Description { get; }

    /// <summary>Whether a future observation may still advance fulfillment.</summary>
    public bool IsActive { get; }

    /// <summary>Whether cancellation is safe: nothing was dispatched, or creation was definitely refused.</summary>
    public bool CanCancel { get; }

    /// <summary>Whether remote creation is unsettled, so an exact holding may still be accepted.</summary>
    public bool AwaitsHolding { get; }

    /// <summary>Whether an accepted holding is waiting for its requested files to be bound locally.</summary>
    public bool AwaitsFiles { get; }

    /// <summary>Whether an accepted remote holding identity is pinned to the request.</summary>
    public bool HoldsRemoteIdentity { get; }

    /// <summary>Whether the accepted holding is present and usable, not removed or released.</summary>
    public bool ProvidesHolding { get; }

    /// <summary>Whether the request still holds its fulfillment reservation.</summary>
    public bool HoldsFulfillment { get; }

    /// <summary>Whether the phase no longer changes on its own.</summary>
    public bool IsSettled { get; }

    /// <summary>Whether a person should look at the request even without a review fence.</summary>
    public bool NeedsAttention { get; }

    #endregion

    #region Constructors

    private ManagedRequestPhaseDefinition(
        ManagedRequestPhase phase,
        string description,
        bool isActive = false,
        bool canCancel = false,
        bool awaitsHolding = false,
        bool awaitsFiles = false,
        bool holdsRemoteIdentity = false,
        bool providesHolding = false,
        bool holdsFulfillment = false,
        bool isSettled = false,
        bool needsAttention = false) {
        Phase = phase;
        Description = description;
        IsActive = isActive;
        CanCancel = canCancel;
        AwaitsHolding = awaitsHolding;
        AwaitsFiles = awaitsFiles;
        HoldsRemoteIdentity = holdsRemoteIdentity;
        ProvidesHolding = providesHolding;
        HoldsFulfillment = holdsFulfillment;
        IsSettled = isSettled;
        NeedsAttention = needsAttention;
    }

    #endregion

    #region Actions - Lookup

    /// <summary>Returns the definition of a persisted phase.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined phase.</exception>
    public static ManagedRequestPhaseDefinition For(ManagedRequestPhase phase) =>
        All.FirstOrDefault(definition => definition.Phase == phase)
        ?? throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown managed request phase.");

    private static IReadOnlyList<ManagedRequestPhase> Codes(Func<ManagedRequestPhaseDefinition, bool> predicate) =>
        All.Where(predicate).Select(definition => definition.Phase).ToArray();

    #endregion
}
