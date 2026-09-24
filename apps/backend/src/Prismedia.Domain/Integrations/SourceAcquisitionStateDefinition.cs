using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>
/// The behavior of one <see cref="SourceAcquisitionState"/>: the transfer phase an observation leads to, the
/// progress it reports, and what the source must supply with it.
/// </summary>
public sealed class SourceAcquisitionStateDefinition {
    #region Static Variables

    /// <summary>The exact item has neither visible download activity nor a complete retrievable file.</summary>
    public static readonly SourceAcquisitionStateDefinition NotObserved = new(SourceAcquisitionState.NotObserved,
        transferPhase: null, "Requesting selected publication", basePercent: 10);

    /// <summary>The source has queued the exact item.</summary>
    public static readonly SourceAcquisitionStateDefinition Queued = new(SourceAcquisitionState.Queued,
        IntegrationTransferPhase.AwaitingRemote, "Selected publication is queued at the source", basePercent: 10);

    /// <summary>The source is downloading the exact item.</summary>
    public static readonly SourceAcquisitionStateDefinition Downloading = new(SourceAcquisitionState.Downloading,
        IntegrationTransferPhase.AwaitingRemote, "Source is preparing selected publication", basePercent: 10);

    /// <summary>The source reports a complete file and confirms its retrieval endpoint is available.</summary>
    public static readonly SourceAcquisitionStateDefinition Ready = new(SourceAcquisitionState.Ready,
        IntegrationTransferPhase.Transferring, "Selected publication is ready for transfer", basePercent: 50,
        offerAccess: AcquisitionAccessKind.Download);

    /// <summary>The source reports an unsuccessful attempt; the accepted local intent remains available for review.</summary>
    public static readonly SourceAcquisitionStateDefinition Failed = new(SourceAcquisitionState.Failed,
        IntegrationTransferPhase.NeedsReview, "Source preparation needs review", basePercent: 10, requiresProblem: true);

    /// <summary>Every state definition, one per <see cref="SourceAcquisitionState"/> member.</summary>
    public static IReadOnlyList<SourceAcquisitionStateDefinition> All { get; } = [NotObserved, Queued, Downloading, Ready, Failed];

    #endregion

    #region Variables

    /// <summary>Persisted and generated identity of this state.</summary>
    public SourceAcquisitionState State { get; }

    /// <summary>Transfer phase this observation leads to, or null to stay in the current phase.</summary>
    public IntegrationTransferPhase? TransferPhase { get; }

    /// <summary>Job progress message for this observation.</summary>
    public string ProgressMessage { get; }

    /// <summary>Job progress percent when the source reports no fraction of its own.</summary>
    public int BasePercent { get; }

    /// <summary>Access the source offer must carry in this state.</summary>
    public AcquisitionAccessKind OfferAccess { get; }

    /// <summary>Whether the source must explain this state.</summary>
    public bool RequiresProblem { get; }

    #endregion

    #region Constructors

    private SourceAcquisitionStateDefinition(
        SourceAcquisitionState state,
        IntegrationTransferPhase? transferPhase,
        string progressMessage,
        int basePercent,
        AcquisitionAccessKind offerAccess = AcquisitionAccessKind.Request,
        bool requiresProblem = false) {
        State = state;
        TransferPhase = transferPhase;
        ProgressMessage = progressMessage;
        BasePercent = basePercent;
        OfferAccess = offerAccess;
        RequiresProblem = requiresProblem;
    }

    #endregion

    #region Actions - Lookup

    /// <summary>Returns the definition of an observed source state.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined state.</exception>
    public static SourceAcquisitionStateDefinition For(SourceAcquisitionState state) =>
        All.FirstOrDefault(definition => definition.State == state)
        ?? throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown source acquisition state.");

    #endregion
}
