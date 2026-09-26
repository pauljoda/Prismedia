using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>
/// The behavior of one <see cref="ManagedControlPhase"/>: whether the action still holds its control slot,
/// whether its next stage can be stopped or refused, and whether its outcome is unverified.
/// </summary>
public sealed class ManagedControlPhaseDefinition {
    #region Static Variables

    /// <summary>A settings change has not been sent.</summary>
    public static readonly ManagedControlPhaseDefinition PendingConfiguration = new(ManagedControlPhase.PendingConfiguration,
        "waiting to change settings", isActive: true, canCancel: true, awaitsConfiguration: true, canBeRejected: true);

    /// <summary>A settings change may have been applied; only observation can confirm it.</summary>
    public static readonly ManagedControlPhaseDefinition ConfigurationUncertain = new(ManagedControlPhase.ConfigurationUncertain,
        "unsure whether its settings changed", isActive: true, awaitsConfiguration: true, canBeRejected: true, isUncertain: true);

    /// <summary>A search has not been requested.</summary>
    public static readonly ManagedControlPhaseDefinition PendingSearch = new(ManagedControlPhase.PendingSearch,
        "waiting to request a search", isActive: true, canCancel: true, canBeRejected: true);

    /// <summary>A search may have been requested, but no command identity was retained.</summary>
    public static readonly ManagedControlPhaseDefinition SearchUncertain = new(ManagedControlPhase.SearchUncertain,
        "unsure whether its search was requested", isActive: true, canBeRejected: true, isUncertain: true);

    /// <summary>An acknowledged search command is being observed.</summary>
    public static readonly ManagedControlPhaseDefinition AwaitingCommand = new(ManagedControlPhase.AwaitingCommand,
        "waiting for its search command", isActive: true, isUncertain: true);

    /// <summary>The requested settings and search finished.</summary>
    public static readonly ManagedControlPhaseDefinition Completed = new(ManagedControlPhase.Completed, "completed");

    /// <summary>The manager definitely refused the current stage.</summary>
    public static readonly ManagedControlPhaseDefinition Rejected = new(ManagedControlPhase.Rejected, "refused by its manager");

    /// <summary>The manager reported that the search command failed.</summary>
    public static readonly ManagedControlPhaseDefinition Failed = new(ManagedControlPhase.Failed, "failed");

    /// <summary>The action stopped before its next stage, or the manager cancelled the command.</summary>
    public static readonly ManagedControlPhaseDefinition Cancelled = new(ManagedControlPhase.Cancelled, "cancelled");

    /// <summary>A person closed an unresolved action without claiming success.</summary>
    public static readonly ManagedControlPhaseDefinition ClosedUnverified = new(ManagedControlPhase.ClosedUnverified,
        "closed without verification");

    /// <summary>Every phase definition, one per <see cref="ManagedControlPhase"/> member.</summary>
    public static IReadOnlyList<ManagedControlPhaseDefinition> All { get; } = [
        PendingConfiguration,
        ConfigurationUncertain,
        PendingSearch,
        SearchUncertain,
        AwaitingCommand,
        Completed,
        Rejected,
        Failed,
        Cancelled,
        ClosedUnverified
    ];

    #endregion

    #region Variables

    /// <summary>Persisted and generated identity of this phase.</summary>
    public ManagedControlPhase Phase { get; }

    /// <summary>Plain description used in transition errors, completing "A manager action that is …".</summary>
    public string Description { get; }

    /// <summary>Whether the action still holds its holding's exclusive control slot.</summary>
    public bool IsActive { get; }

    /// <summary>Whether the next stage is unsent, so stopping locally undoes nothing remote.</summary>
    public bool CanCancel { get; }

    /// <summary>Whether observed settings may still confirm the requested configuration.</summary>
    public bool AwaitsConfiguration { get; }

    /// <summary>Whether the manager may still definitely refuse the current stage.</summary>
    public bool CanBeRejected { get; }

    /// <summary>Whether a remote effect may have happened without confirmed evidence.</summary>
    public bool IsUncertain { get; }

    #endregion

    #region Constructors

    private ManagedControlPhaseDefinition(
        ManagedControlPhase phase,
        string description,
        bool isActive = false,
        bool canCancel = false,
        bool awaitsConfiguration = false,
        bool canBeRejected = false,
        bool isUncertain = false) {
        Phase = phase;
        Description = description;
        IsActive = isActive;
        CanCancel = canCancel;
        AwaitsConfiguration = awaitsConfiguration;
        CanBeRejected = canBeRejected;
        IsUncertain = isUncertain;
    }

    #endregion

    #region Actions - Lookup

    /// <summary>Returns the definition of a persisted phase.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined phase.</exception>
    public static ManagedControlPhaseDefinition For(ManagedControlPhase phase) =>
        All.FirstOrDefault(definition => definition.Phase == phase)
        ?? throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown manager action phase.");

    #endregion
}
