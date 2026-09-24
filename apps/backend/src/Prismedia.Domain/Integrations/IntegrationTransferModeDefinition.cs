using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>
/// The behavior of one <see cref="IntegrationTransferMode"/>: who executes the acquisition, which phases may
/// still be cancelled, and how a completed import finishes.
/// </summary>
public sealed class IntegrationTransferModeDefinition {
    #region Static Variables

    /// <summary>A connected executor owns the durable remote job and retained outputs.</summary>
    public static readonly IntegrationTransferModeDefinition RemoteExecutor = new(IntegrationTransferMode.RemoteExecutor,
        "remote executor transfer",
        isSource: false,
        preparesAtSource: false,
        acknowledgesImports: true,
        sourceCancellablePhases: [],
        remoteCancellablePhases: [
            IntegrationTransferPhase.PendingSubmission,
            IntegrationTransferPhase.SubmissionUncertain,
            IntegrationTransferPhase.AwaitingRemote,
            IntegrationTransferPhase.AwaitingArtifacts,
            IntegrationTransferPhase.Transferring,
            IntegrationTransferPhase.NeedsReview
        ]);

    /// <summary>Prismedia retrieves a direct full-content offer from a connected source.</summary>
    public static readonly IntegrationTransferModeDefinition SourceDownload = new(IntegrationTransferMode.SourceDownload,
        "direct source download",
        isSource: true,
        preparesAtSource: false,
        acknowledgesImports: false,
        sourceCancellablePhases: [IntegrationTransferPhase.Transferring],
        remoteCancellablePhases: []);

    /// <summary>A connected source prepares one exact selection before Prismedia retrieves it as a direct offer.</summary>
    public static readonly IntegrationTransferModeDefinition SourceRequest = new(IntegrationTransferMode.SourceRequest,
        "prepared source request",
        isSource: true,
        preparesAtSource: true,
        acknowledgesImports: false,
        sourceCancellablePhases: [
            IntegrationTransferPhase.PendingSubmission,
            IntegrationTransferPhase.SubmissionUncertain,
            IntegrationTransferPhase.AwaitingRemote,
            IntegrationTransferPhase.NeedsReview,
            IntegrationTransferPhase.Transferring
        ],
        remoteCancellablePhases: []);

    /// <summary>Every mode definition, one per <see cref="IntegrationTransferMode"/> member.</summary>
    public static IReadOnlyList<IntegrationTransferModeDefinition> All { get; } = [RemoteExecutor, SourceDownload, SourceRequest];

    #endregion

    #region Variables

    /// <summary>Persisted and generated identity of this mode.</summary>
    public IntegrationTransferMode Mode { get; }

    /// <summary>Plain name used in transition errors, completing "A … cannot …".</summary>
    public string Description { get; }

    /// <summary>Whether Prismedia retrieves a source offer itself instead of a remote executor retaining outputs.</summary>
    public bool IsSource { get; }

    /// <summary>Whether the source prepares the exact selection before its bytes can be retrieved.</summary>
    public bool PreparesAtSource { get; }

    /// <summary>Whether completed local imports still need a remote acknowledgement.</summary>
    public bool AcknowledgesImports { get; }

    /// <summary>Phases in which source follow-up can stop locally before placement.</summary>
    public IReadOnlyList<IntegrationTransferPhase> SourceCancellablePhases { get; }

    /// <summary>Phases in which a remote executor's work can still be stopped.</summary>
    public IReadOnlyList<IntegrationTransferPhase> RemoteCancellablePhases { get; }

    #endregion

    #region Constructors

    private IntegrationTransferModeDefinition(
        IntegrationTransferMode mode,
        string description,
        bool isSource,
        bool preparesAtSource,
        bool acknowledgesImports,
        IReadOnlyList<IntegrationTransferPhase> sourceCancellablePhases,
        IReadOnlyList<IntegrationTransferPhase> remoteCancellablePhases) {
        Mode = mode;
        Description = description;
        IsSource = isSource;
        PreparesAtSource = preparesAtSource;
        AcknowledgesImports = acknowledgesImports;
        SourceCancellablePhases = sourceCancellablePhases;
        RemoteCancellablePhases = remoteCancellablePhases;
    }

    #endregion

    #region Actions - Lookup

    /// <summary>Returns the definition of a persisted mode.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined mode.</exception>
    public static IntegrationTransferModeDefinition For(IntegrationTransferMode mode) =>
        All.FirstOrDefault(definition => definition.Mode == mode)
        ?? throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown transfer mode.");

    #endregion
}
