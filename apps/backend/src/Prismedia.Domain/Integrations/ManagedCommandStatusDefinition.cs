using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>
/// The behavior of one <see cref="ManagedCommandStatus"/>: which action phase an observed manager command
/// settles into, whether its outcome needs a person, and what a person should be told.
/// </summary>
public sealed class ManagedCommandStatusDefinition {
    #region Static Variables

    /// <summary>The manager accepted a command which has not started.</summary>
    public static readonly ManagedCommandStatusDefinition Pending = new(ManagedCommandStatus.Pending, settledPhase: null);

    /// <summary>The manager is executing the command.</summary>
    public static readonly ManagedCommandStatusDefinition Running = new(ManagedCommandStatus.Running, settledPhase: null);

    /// <summary>The command finished; it may have found no releases.</summary>
    public static readonly ManagedCommandStatusDefinition Completed = new(ManagedCommandStatus.Completed,
        ManagedControlPhase.Completed);

    /// <summary>The command finished unsuccessfully.</summary>
    public static readonly ManagedCommandStatusDefinition Failed = new(ManagedCommandStatus.Failed,
        ManagedControlPhase.Failed,
        problem: "The manager reported that the search failed. Previously confirmed settings remain applied.");

    /// <summary>The manager cancelled or aborted execution.</summary>
    public static readonly ManagedCommandStatusDefinition Cancelled = new(ManagedCommandStatus.Cancelled,
        ManagedControlPhase.Cancelled,
        problem: "The manager reported that the search was cancelled. Previously confirmed settings remain applied.");

    /// <summary>History is missing or the manager cannot establish an execution outcome.</summary>
    public static readonly ManagedCommandStatusDefinition Unknown = new(ManagedCommandStatus.Unknown,
        settledPhase: null, requiresReview: true,
        problem: "The manager cannot establish the original command's outcome. No replacement search was sent.");

    /// <summary>Every status definition, one per <see cref="ManagedCommandStatus"/> member.</summary>
    public static IReadOnlyList<ManagedCommandStatusDefinition> All { get; } = [Pending, Running, Completed, Failed, Cancelled, Unknown];

    #endregion

    #region Variables

    /// <summary>Persisted and generated identity of this status.</summary>
    public ManagedCommandStatus Status { get; }

    /// <summary>The action phase this outcome settles into, or null while the command is still being observed.</summary>
    public ManagedControlPhase? SettledPhase { get; }

    /// <summary>Whether the outcome cannot be established and needs a person.</summary>
    public bool RequiresReview { get; }

    /// <summary>Explanation shown with the action when the outcome is not a plain success, or null.</summary>
    public string? Problem { get; }

    #endregion

    #region Constructors

    private ManagedCommandStatusDefinition(
        ManagedCommandStatus status,
        ManagedControlPhase? settledPhase,
        bool requiresReview = false,
        string? problem = null) {
        Status = status;
        SettledPhase = settledPhase;
        RequiresReview = requiresReview;
        Problem = problem;
    }

    #endregion

    #region Actions - Lookup

    /// <summary>Returns the definition of an observed command status.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined status.</exception>
    public static ManagedCommandStatusDefinition For(ManagedCommandStatus status) =>
        All.FirstOrDefault(definition => definition.Status == status)
        ?? throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown manager command status.");

    #endregion
}
