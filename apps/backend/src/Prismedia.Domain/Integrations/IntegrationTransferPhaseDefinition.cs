using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>
/// The behavior of one <see cref="IntegrationTransferPhase"/>: which observations and byte steps it accepts,
/// whether it has passed the local import boundary, and whether it is finished.
/// </summary>
public sealed class IntegrationTransferPhaseDefinition {
    #region Static Variables

    /// <summary>The durable intent has not been sent.</summary>
    public static readonly IntegrationTransferPhaseDefinition PendingSubmission = new(IntegrationTransferPhase.PendingSubmission,
        "waiting to submit", awaitsSubmission: true, awaitsSourcePreparation: true);

    /// <summary>Submission may have reached the executor; operation lookup must reconcile it.</summary>
    public static readonly IntegrationTransferPhaseDefinition SubmissionUncertain = new(IntegrationTransferPhase.SubmissionUncertain,
        "unsure whether it was submitted", awaitsSubmission: true, awaitsSourcePreparation: true);

    /// <summary>A stable remote job is accepted and being monitored.</summary>
    public static readonly IntegrationTransferPhaseDefinition AwaitingRemote = new(IntegrationTransferPhase.AwaitingRemote,
        "waiting for remote work", awaitsSourcePreparation: true, awaitsRemoteExecution: true);

    /// <summary>Remote execution finished; a complete manifest must still be read.</summary>
    public static readonly IntegrationTransferPhaseDefinition AwaitingArtifacts = new(IntegrationTransferPhase.AwaitingArtifacts,
        "waiting for its output manifest", awaitsManifest: true, retrievesOutputs: true);

    /// <summary>Exact output bytes are being retrieved and verified.</summary>
    public static readonly IntegrationTransferPhaseDefinition Transferring = new(IntegrationTransferPhase.Transferring,
        "transferring verified bytes", retrievesOutputs: true, verifiesBytes: true);

    /// <summary>Verified bytes are being materialized in the local library.</summary>
    public static readonly IntegrationTransferPhaseDefinition Importing = new(IntegrationTransferPhase.Importing,
        "importing into the library", verifiesBytes: true, passedImportBoundary: true);

    /// <summary>Local imports are committed; only a remote receipt remains.</summary>
    public static readonly IntegrationTransferPhaseDefinition AwaitingAcknowledgement = new(IntegrationTransferPhase.AwaitingAcknowledgement,
        "waiting to acknowledge its outputs", passedImportBoundary: true);

    /// <summary>Local imports and the remote acknowledgement both completed.</summary>
    public static readonly IntegrationTransferPhaseDefinition Completed = new(IntegrationTransferPhase.Completed,
        "completed", passedImportBoundary: true, isTerminal: true, isSettled: true);

    /// <summary>Incomplete or expired remote outputs require a user decision.</summary>
    public static readonly IntegrationTransferPhaseDefinition NeedsReview = new(IntegrationTransferPhase.NeedsReview,
        "waiting for review", awaitsSourcePreparation: true, awaitsRemoteExecution: true, awaitsManifest: true,
        needsAttention: true);

    /// <summary>The executor failed before fulfillment completed.</summary>
    public static readonly IntegrationTransferPhaseDefinition Failed = new(IntegrationTransferPhase.Failed,
        "failed", isTerminal: true, needsAttention: true);

    /// <summary>Local fulfillment was cancelled before import.</summary>
    public static readonly IntegrationTransferPhaseDefinition Cancelled = new(IntegrationTransferPhase.Cancelled,
        "cancelled", isTerminal: true, isSettled: true);

    /// <summary>Every phase definition, one per <see cref="IntegrationTransferPhase"/> member.</summary>
    public static IReadOnlyList<IntegrationTransferPhaseDefinition> All { get; } = [
        PendingSubmission,
        SubmissionUncertain,
        AwaitingRemote,
        AwaitingArtifacts,
        Transferring,
        Importing,
        AwaitingAcknowledgement,
        Completed,
        NeedsReview,
        Failed,
        Cancelled
    ];

    /// <summary>Phases in which the transfer is finished and releases its active ownership.</summary>
    public static IReadOnlyList<IntegrationTransferPhase> Terminal { get; } = Codes(phase => phase.IsTerminal);

    /// <summary>Phases shown as finished history.</summary>
    public static IReadOnlyList<IntegrationTransferPhase> Settled { get; } = Codes(phase => phase.IsSettled);

    /// <summary>Phases that need a person to look at them.</summary>
    public static IReadOnlyList<IntegrationTransferPhase> NeedingAttention { get; } = Codes(phase => phase.NeedsAttention);

    #endregion

    #region Variables

    /// <summary>Persisted and generated identity of this phase.</summary>
    public IntegrationTransferPhase Phase { get; }

    /// <summary>Plain description used in transition errors, completing "A transfer that is …".</summary>
    public string Description { get; }

    /// <summary>Whether no remote job has been accepted yet, so submission may still be sent or recovered.</summary>
    public bool AwaitsSubmission { get; }

    /// <summary>Whether a source may still prepare or report on the exact requested selection.</summary>
    public bool AwaitsSourcePreparation { get; }

    /// <summary>Whether a remote executor's job snapshot may still advance the transfer.</summary>
    public bool AwaitsRemoteExecution { get; }

    /// <summary>Whether a sealed output manifest may still be accepted.</summary>
    public bool AwaitsManifest { get; }

    /// <summary>Whether remote outputs are being awaited or retrieved, so their absence needs review.</summary>
    public bool RetrievesOutputs { get; }

    /// <summary>Whether local byte evidence may still be recorded.</summary>
    public bool VerifiesBytes { get; }

    /// <summary>Whether local library placement has started, so the transfer can no longer be cancelled.</summary>
    public bool PassedImportBoundary { get; }

    /// <summary>Whether the transfer is finished.</summary>
    public bool IsTerminal { get; }

    /// <summary>Whether the transfer is finished history rather than a problem.</summary>
    public bool IsSettled { get; }

    /// <summary>Whether a person should look at the transfer.</summary>
    public bool NeedsAttention { get; }

    #endregion

    #region Constructors

    private IntegrationTransferPhaseDefinition(
        IntegrationTransferPhase phase,
        string description,
        bool awaitsSubmission = false,
        bool awaitsSourcePreparation = false,
        bool awaitsRemoteExecution = false,
        bool awaitsManifest = false,
        bool retrievesOutputs = false,
        bool verifiesBytes = false,
        bool passedImportBoundary = false,
        bool isTerminal = false,
        bool isSettled = false,
        bool needsAttention = false) {
        Phase = phase;
        Description = description;
        AwaitsSubmission = awaitsSubmission;
        AwaitsSourcePreparation = awaitsSourcePreparation;
        AwaitsRemoteExecution = awaitsRemoteExecution;
        AwaitsManifest = awaitsManifest;
        RetrievesOutputs = retrievesOutputs;
        VerifiesBytes = verifiesBytes;
        PassedImportBoundary = passedImportBoundary;
        IsTerminal = isTerminal;
        IsSettled = isSettled;
        NeedsAttention = needsAttention;
    }

    #endregion

    #region Actions - Lookup

    /// <summary>Returns the definition of a persisted phase.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined phase.</exception>
    public static IntegrationTransferPhaseDefinition For(IntegrationTransferPhase phase) =>
        All.FirstOrDefault(definition => definition.Phase == phase)
        ?? throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown transfer phase.");

    private static IReadOnlyList<IntegrationTransferPhase> Codes(Func<IntegrationTransferPhaseDefinition, bool> predicate) =>
        All.Where(predicate).Select(definition => definition.Phase).ToArray();

    #endregion
}
