using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>
/// The behavior of one <see cref="RemoteJobState"/>: the transfer phase an executor snapshot leads to, whether
/// execution has stopped, and whether its sealed output manifest must be retained.
/// </summary>
public sealed class RemoteJobStateDefinition {
    #region Static Variables

    /// <summary>Durably accepted and waiting to execute.</summary>
    public static readonly RemoteJobStateDefinition Queued = new(RemoteJobState.Queued, IntegrationTransferPhase.AwaitingRemote);

    /// <summary>Executing the accepted selection.</summary>
    public static readonly RemoteJobStateDefinition Running = new(RemoteJobState.Running, IntegrationTransferPhase.AwaitingRemote);

    /// <summary>Execution is durably waiting for a recoverable upstream condition.</summary>
    public static readonly RemoteJobStateDefinition Waiting = new(RemoteJobState.Waiting, IntegrationTransferPhase.AwaitingRemote);

    /// <summary>Some selected items failed or remain incomplete.</summary>
    public static readonly RemoteJobStateDefinition Partial = new(RemoteJobState.Partial, IntegrationTransferPhase.NeedsReview,
        isTerminal: true, retainsManifest: true);

    /// <summary>Execution finished and a sealed output manifest is available.</summary>
    public static readonly RemoteJobStateDefinition Succeeded = new(RemoteJobState.Succeeded, IntegrationTransferPhase.AwaitingArtifacts,
        isTerminal: true, retainsManifest: true, requiresManifest: true);

    /// <summary>Execution failed.</summary>
    public static readonly RemoteJobStateDefinition Failed = new(RemoteJobState.Failed, IntegrationTransferPhase.Failed, isTerminal: true);

    /// <summary>Execution stopped after cancellation.</summary>
    public static readonly RemoteJobStateDefinition Cancelled = new(RemoteJobState.Cancelled, IntegrationTransferPhase.Cancelled, isTerminal: true);

    /// <summary>Every state definition, one per <see cref="RemoteJobState"/> member.</summary>
    public static IReadOnlyList<RemoteJobStateDefinition> All { get; } = [Queued, Running, Waiting, Partial, Succeeded, Failed, Cancelled];

    #endregion

    #region Variables

    /// <summary>Persisted and generated identity of this state.</summary>
    public RemoteJobState State { get; }

    /// <summary>Transfer phase a newer snapshot in this state leads to.</summary>
    public IntegrationTransferPhase TransferPhase { get; }

    /// <summary>Whether execution has stopped; a stopped result can never be replaced.</summary>
    public bool IsTerminal { get; }

    /// <summary>Whether the snapshot carries a sealed output manifest revision to retain.</summary>
    public bool RetainsManifest { get; }

    /// <summary>Whether the snapshot must identify its sealed output manifest revision.</summary>
    public bool RequiresManifest { get; }

    #endregion

    #region Constructors

    private RemoteJobStateDefinition(
        RemoteJobState state,
        IntegrationTransferPhase transferPhase,
        bool isTerminal = false,
        bool retainsManifest = false,
        bool requiresManifest = false) {
        State = state;
        TransferPhase = transferPhase;
        IsTerminal = isTerminal;
        RetainsManifest = retainsManifest;
        RequiresManifest = requiresManifest;
    }

    #endregion

    #region Actions - Lookup

    /// <summary>Returns the definition of an executor state.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined state.</exception>
    public static RemoteJobStateDefinition For(RemoteJobState state) =>
        All.FirstOrDefault(definition => definition.State == state)
        ?? throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown remote job state.");

    #endregion
}
