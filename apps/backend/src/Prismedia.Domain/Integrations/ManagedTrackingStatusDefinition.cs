using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>
/// The behavior of one <see cref="ManagedTrackingStatus"/>: whether a retained holding is observed on a
/// schedule, can take manager actions or new targets, blocks plugin changes, and how activity presents it.
/// </summary>
public sealed class ManagedTrackingStatusDefinition {
    #region Static Variables

    /// <summary>Explicit local associations are awaiting worker verification.</summary>
    public static readonly ManagedTrackingStatusDefinition Pending = new(ManagedTrackingStatus.Pending,
        "waiting for its first check", isObserved: true, blocksPluginChanges: true);

    /// <summary>Owned wanted targets exist independently of their first locally readable source files.</summary>
    public static readonly ManagedTrackingStatusDefinition WaitingForFiles = new(ManagedTrackingStatus.WaitingForFiles,
        "waiting for files", isEstablished: true, acceptsControls: true);

    /// <summary>The most recent observation reconciled the established scope.</summary>
    public static readonly ManagedTrackingStatusDefinition Tracking = new(ManagedTrackingStatus.Tracking,
        "tracking", isObserved: true, isEstablished: true, acceptsControls: true, isFollowed: true);

    /// <summary>Identity, coverage, or source ownership needs an explicit decision.</summary>
    public static readonly ManagedTrackingStatusDefinition NeedsReview = new(ManagedTrackingStatus.NeedsReview,
        "waiting for review", needsAttention: true);

    /// <summary>The connected service could not be observed; previous evidence is retained.</summary>
    public static readonly ManagedTrackingStatusDefinition Stale = new(ManagedTrackingStatus.Stale,
        "unverified", isObserved: true, needsAttention: true);

    /// <summary>The manager definitively no longer contains this holding; local files and identities remain.</summary>
    public static readonly ManagedTrackingStatusDefinition Removed = new(ManagedTrackingStatus.Removed,
        "removed from its manager", isObserved: true, acceptsControls: true, keepsStatusWhenUnverifiable: true,
        isSettled: true);

    /// <summary>Host actions are frozen while a fresh remote drain observation is pending.</summary>
    public static readonly ManagedTrackingStatusDefinition ReleasePending = new(ManagedTrackingStatus.ReleasePending,
        "releasing ownership", isObserved: true, freezesHostActions: true, blocksPluginChanges: true);

    /// <summary>Ownership and active source associations were explicitly released; files and history remain.</summary>
    public static readonly ManagedTrackingStatusDefinition Released = new(ManagedTrackingStatus.Released,
        "released to its manager", freezesHostActions: true, isSettled: true);

    /// <summary>Every status definition, one per <see cref="ManagedTrackingStatus"/> member.</summary>
    public static IReadOnlyList<ManagedTrackingStatusDefinition> All { get; } = [
        Pending,
        WaitingForFiles,
        Tracking,
        NeedsReview,
        Stale,
        Removed,
        ReleasePending,
        Released
    ];

    /// <summary>Statuses observed on a schedule without an explicit refresh.</summary>
    public static IReadOnlyList<ManagedTrackingStatus> Observed { get; } = Codes(status => status.IsObserved);

    /// <summary>Statuses whose accepted holding can receive manager actions.</summary>
    public static IReadOnlyList<ManagedTrackingStatus> AcceptingControls { get; } = Codes(status => status.AcceptsControls);

    /// <summary>Statuses in which a plugin update or removal would interrupt unfinished work.</summary>
    public static IReadOnlyList<ManagedTrackingStatus> BlockingPluginChanges { get; } = Codes(status => status.BlocksPluginChanges);

    /// <summary>Statuses in steady-state following rather than first delivery.</summary>
    public static IReadOnlyList<ManagedTrackingStatus> Followed { get; } = Codes(status => status.IsFollowed);

    /// <summary>Statuses shown as finished history.</summary>
    public static IReadOnlyList<ManagedTrackingStatus> Settled { get; } = Codes(status => status.IsSettled);

    /// <summary>Statuses that need a person to look at them.</summary>
    public static IReadOnlyList<ManagedTrackingStatus> NeedingAttention { get; } = Codes(status => status.NeedsAttention);

    #endregion

    #region Variables

    /// <summary>Persisted and generated identity of this status.</summary>
    public ManagedTrackingStatus Status { get; }

    /// <summary>Plain description used in transition errors, completing "A tracked holding that is …".</summary>
    public string Description { get; }

    /// <summary>Whether the holding is observed on a schedule without an explicit refresh.</summary>
    public bool IsObserved { get; }

    /// <summary>Whether the accepted holding is established and usable for another finite target.</summary>
    public bool IsEstablished { get; }

    /// <summary>Whether the holding can receive manager monitoring and search actions.</summary>
    public bool AcceptsControls { get; }

    /// <summary>Whether reconciliation, controls, and new targets are frozen for an ownership handoff.</summary>
    public bool FreezesHostActions { get; }

    /// <summary>Whether a plugin update or removal would interrupt unfinished work on this holding.</summary>
    public bool BlocksPluginChanges { get; }

    /// <summary>Whether an unverifiable connection keeps this status instead of marking the holding stale.</summary>
    public bool KeepsStatusWhenUnverifiable { get; }

    /// <summary>Whether the holding is in steady-state following rather than first delivery.</summary>
    public bool IsFollowed { get; }

    /// <summary>Whether the holding is finished history.</summary>
    public bool IsSettled { get; }

    /// <summary>Whether a person should look at the holding.</summary>
    public bool NeedsAttention { get; }

    #endregion

    #region Constructors

    private ManagedTrackingStatusDefinition(
        ManagedTrackingStatus status,
        string description,
        bool isObserved = false,
        bool isEstablished = false,
        bool acceptsControls = false,
        bool freezesHostActions = false,
        bool blocksPluginChanges = false,
        bool keepsStatusWhenUnverifiable = false,
        bool isFollowed = false,
        bool isSettled = false,
        bool needsAttention = false) {
        Status = status;
        Description = description;
        IsObserved = isObserved;
        IsEstablished = isEstablished;
        AcceptsControls = acceptsControls;
        FreezesHostActions = freezesHostActions;
        BlocksPluginChanges = blocksPluginChanges;
        KeepsStatusWhenUnverifiable = keepsStatusWhenUnverifiable;
        IsFollowed = isFollowed;
        IsSettled = isSettled;
        NeedsAttention = needsAttention;
    }

    #endregion

    #region Actions - Lookup

    /// <summary>Returns the definition of a persisted status.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined status.</exception>
    public static ManagedTrackingStatusDefinition For(ManagedTrackingStatus status) =>
        All.FirstOrDefault(definition => definition.Status == status)
        ?? throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown managed tracking status.");

    private static IReadOnlyList<ManagedTrackingStatus> Codes(Func<ManagedTrackingStatusDefinition, bool> predicate) =>
        All.Where(predicate).Select(definition => definition.Status).ToArray();

    #endregion
}
