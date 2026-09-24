using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>Exact local source owners for one artifact, plus an optional containing gallery used as its library entrypoint.</summary>
public sealed record IntegrationArtifactImport(string ArtifactId, string Sha256, IReadOnlyList<Guid> EntityIds, Guid? ContainerEntityId = null);

/// <summary>Rehydratable transfer state; operation identity is allocated before any remote side effect.</summary>
public sealed record IntegrationTransferState(Guid OperationId, Guid ConnectionId, string? InstanceId, long Revision,
    IntegrationTransferPhase Phase, string? JobId = null, long RemoteRevision = -1, string? ManifestRevision = null,
    IReadOnlyList<IntegrationArtifact>? Artifacts = null, IReadOnlyList<string>? VerifiedArtifactIds = null,
    IReadOnlyList<IntegrationArtifactImport>? Imports = null, Guid? ReceiptId = null,
    IntegrationTransferMode Mode = IntegrationTransferMode.RemoteExecutor, RemoteJobState? LastRemoteState = null,
    bool CancellationRequested = false, SourceAcquisitionState? LastSourceState = null,
    double? SourceProgress = null, string? SourceProblem = null);

/// <summary>
/// Enforces submission recovery and the separation between remote completion, verified bytes, committed
/// imports, and acknowledgement. What each mode, phase, and observed state permits comes from its
/// definition: <see cref="IntegrationTransferModeDefinition"/>, <see cref="IntegrationTransferPhaseDefinition"/>,
/// <see cref="RemoteJobStateDefinition"/>, and <see cref="SourceAcquisitionStateDefinition"/>.
/// </summary>
public sealed class IntegrationTransfer {
    #region Static Variables

    private const int MaximumIdentifierLength = 512;
    private const int MaximumProblemLength = 4096;

    #endregion

    #region Variables

    /// <summary>Immutable state to persist with optimistic concurrency after each external boundary.</summary>
    public IntegrationTransferState State { get; private set; }

    /// <summary>Behavior of the current phase.</summary>
    public IntegrationTransferPhaseDefinition Phase => IntegrationTransferPhaseDefinition.For(State.Phase);

    /// <summary>Behavior of the execution mode.</summary>
    public IntegrationTransferModeDefinition Mode => IntegrationTransferModeDefinition.For(State.Mode);

    /// <summary>Source fulfillment can stop locally only before a persisted import boundary permits library placement.</summary>
    public bool CanCancelSource => Mode.SourceCancellablePhases.Contains(State.Phase);

    /// <summary>Remote work can be stopped until local import starts; ownership remains reserved until execution has stopped.</summary>
    public bool CanCancelRemote => !State.CancellationRequested && Mode.RemoteCancellablePhases.Contains(State.Phase);

    private RemoteJobStateDefinition? LastRemote =>
        State.LastRemoteState is { } remoteState ? RemoteJobStateDefinition.For(remoteState) : null;

    #endregion

    #region Constructors

    /// <summary>Rehydrates persisted progress, refusing a state without its identities or revision.</summary>
    /// <exception cref="ArgumentException">The state lacks its operation or connection identity, or its revision.</exception>
    public IntegrationTransfer(IntegrationTransferState state) {
        ArgumentNullException.ThrowIfNull(state);
        if (state.OperationId == Guid.Empty || state.ConnectionId == Guid.Empty || state.Revision < 1) {
            throw new ArgumentException("A transfer requires stable operation and connection identities and a positive revision.", nameof(state));
        }

        State = state;
    }

    #endregion

    #region Actions - Creation

    /// <summary>Creates a finite acquisition intent for a verified persistent executor installation.</summary>
    public static IntegrationTransfer Create(Guid operationId, Guid connectionId, string instanceId) {
        if (operationId == Guid.Empty || connectionId == Guid.Empty || string.IsNullOrWhiteSpace(instanceId)
            || instanceId.Length > MaximumIdentifierLength) {
            throw new ArgumentException("Stable operation, connection, and remote installation identities are required.");
        }

        return new(new(operationId, connectionId, instanceId, 1, IntegrationTransferPhase.PendingSubmission));
    }

    /// <summary>Creates direct source acquisition without inventing a remote job or installation identity.</summary>
    public static IntegrationTransfer CreateSourceDownload(Guid operationId, Guid connectionId) {
        if (operationId == Guid.Empty || connectionId == Guid.Empty) {
            throw new ArgumentException("Stable operation and connection identities are required.");
        }

        return new(new(operationId, connectionId, null, 1, IntegrationTransferPhase.Transferring,
            Mode: IntegrationTransferMode.SourceDownload));
    }

    /// <summary>Creates a durable exact-source request before any remote preparation is dispatched.</summary>
    public static IntegrationTransfer CreateSourceRequest(Guid operationId, Guid connectionId) {
        if (operationId == Guid.Empty || connectionId == Guid.Empty) {
            throw new ArgumentException("Stable operation and connection identities are required.");
        }

        return new(new(operationId, connectionId, null, 1, IntegrationTransferPhase.PendingSubmission,
            Mode: IntegrationTransferMode.SourceRequest));
    }

    #endregion

    #region Actions - Source

    /// <summary>Seals the exact downloaded source bytes after local verification; direct catalogs need not supply a pre-existing hash.</summary>
    public void AcceptSourceArtifact(IntegrationArtifact artifact) {
        RequireMode(Mode.IsSource, "accept a source artifact");
        var manifest = new IntegrationArtifactManifest(State.OperationId.ToString("N"), artifact.Sha256, true, 1, [artifact]);
        if (State.Artifacts is { } previous) {
            if (!previous.SequenceEqual(manifest.Artifacts)) {
                throw new InvalidOperationException("The accepted source bytes changed.");
            }

            return;
        }

        Require(State.Phase == IntegrationTransferPhase.Transferring, "accept source bytes");
        Change(State with {
            Artifacts = manifest.Artifacts,
            VerifiedArtifactIds = [artifact.Id],
            Imports = [],
            Phase = IntegrationTransferPhase.Importing
        });
    }

    /// <summary>Cancels source follow-up before local placement starts; it does not claim that source-owned preparation stopped.</summary>
    public void CancelSourceDownload() {
        RequireMode(Mode.IsSource, "cancel source follow-up");
        if (State.Phase == IntegrationTransferPhase.Cancelled) {
            return;
        }

        Require(CanCancelSource, "cancel source follow-up");
        Change(State with { Phase = IntegrationTransferPhase.Cancelled });
    }

    /// <summary>Persists the ambiguity fence before requesting exact source preparation.</summary>
    public void BeginSourceRequest() {
        RequireMode(Mode.PreparesAtSource, "request source preparation");
        Require(State.LastSourceState == SourceAcquisitionState.NotObserved && Phase.AwaitsSourcePreparation,
            "request source preparation");
        if (State.Phase == IntegrationTransferPhase.SubmissionUncertain) {
            return;
        }

        Change(State with { Phase = IntegrationTransferPhase.SubmissionUncertain });
    }

    /// <summary>Records bounded source-owned readiness without treating it as retained bytes or a local import.</summary>
    /// <exception cref="ArgumentException">The progress fraction or problem text is out of range.</exception>
    public void ObserveSource(SourceAcquisitionState sourceState, double? progress, string? problem) {
        RequireMode(Mode.PreparesAtSource, "observe source preparation");
        var observed = SourceAcquisitionStateDefinition.For(sourceState);
        if (progress is { } value && (!double.IsFinite(value) || value is < 0 or > 1)) {
            throw new ArgumentException("Source progress must be a fraction between 0 and 1.", nameof(progress));
        }

        if (problem?.Length > MaximumProblemLength) {
            throw new ArgumentException("The source problem description is too long.", nameof(problem));
        }

        Require(Phase.AwaitsSourcePreparation, "observe source preparation");
        var phase = observed.TransferPhase ?? State.Phase;
        problem = string.IsNullOrWhiteSpace(problem) ? null : problem.Trim();
        if (State.LastSourceState == sourceState && State.SourceProgress == progress && State.SourceProblem == problem
            && State.Phase == phase) {
            return;
        }

        Change(State with { LastSourceState = sourceState, SourceProgress = progress, SourceProblem = problem, Phase = phase });
    }

    #endregion

    #region Actions - Remote Execution

    /// <summary>Persist this state before issuing POST. A crash or timeout must recover by the same operation key.</summary>
    public void BeginSubmission() {
        RequireMode(!Mode.IsSource, "submit remote work");
        Require(Phase.AwaitsSubmission, "submit remote work");
        Change(State with { Phase = IntegrationTransferPhase.SubmissionUncertain });
    }

    /// <summary>Accepts only this operation's job on the original installation, including a recovered ambiguous submission.</summary>
    public void AcceptSubmission(Guid operationId, string instanceId, string jobId) {
        if (Mode.IsSource || operationId != State.OperationId || instanceId != State.InstanceId
            || string.IsNullOrWhiteSpace(jobId) || jobId.Length > MaximumIdentifierLength
            || State.JobId is not null && State.JobId != jobId) {
            throw new InvalidOperationException("The remote job identity does not match this transfer.");
        }

        if (State.JobId == jobId) {
            return;
        }

        Require(State.Phase == IntegrationTransferPhase.SubmissionUncertain, "accept a remote job");
        Change(State with { JobId = jobId, Phase = IntegrationTransferPhase.AwaitingRemote });
    }

    /// <summary>Observes a monotonic authoritative job snapshot. Execution success only permits manifest retrieval.</summary>
    public void Observe(string instanceId, string jobId, long revision, RemoteJobState remoteState, string? manifestRevision) {
        if (Mode.IsSource || instanceId != State.InstanceId || jobId != State.JobId || revision < 0) {
            throw new InvalidOperationException("The remote snapshot is invalid or belongs to another job.");
        }

        var observed = RemoteJobStateDefinition.For(remoteState);
        if (revision <= State.RemoteRevision) {
            return;
        }

        Require(Phase.AwaitsRemoteExecution, "observe remote work");
        if (LastRemote?.IsTerminal == true && (remoteState != State.LastRemoteState || manifestRevision != State.ManifestRevision)) {
            throw new InvalidOperationException("A terminal execution result and its sealed output revision cannot be replaced.");
        }

        if (observed.RequiresManifest && !IsManifestRevision(manifestRevision)) {
            throw new InvalidOperationException("A successful executor must identify its sealed manifest revision.");
        }

        Change(State with {
            RemoteRevision = revision,
            Phase = observed.TransferPhase,
            LastRemoteState = remoteState,
            ManifestRevision = observed.RetainsManifest ? manifestRevision : null
        });
    }

    #endregion

    #region Actions - Cancellation

    /// <summary>Persists a cancellation fence before any remote call. A never-submitted operation can stop immediately.</summary>
    public void RequestRemoteCancellation() {
        RequireMode(!Mode.IsSource, "request remote cancellation");
        if (State.Phase == IntegrationTransferPhase.Cancelled || State.CancellationRequested) {
            return;
        }

        Require(CanCancelRemote, "request remote cancellation");
        Change(State with {
            CancellationRequested = true,
            Phase = State.Phase == IntegrationTransferPhase.PendingSubmission ? IntegrationTransferPhase.Cancelled : State.Phase
        });
    }

    /// <summary>Accepts an executor's durable operation tombstone, which prevents an in-flight late submission from creating a job.</summary>
    public void ConfirmPreventedSubmission(string instanceId, Guid operationId) {
        Require(State.CancellationRequested && State.InstanceId == instanceId && State.OperationId == operationId
            && State.JobId is null && State.Phase == IntegrationTransferPhase.SubmissionUncertain,
            "confirm a prevented submission");
        Change(State with { Phase = IntegrationTransferPhase.Cancelled });
    }

    /// <summary>Confirms remote quiescence before releasing local ownership; natural success is preserved when it wins cancellation.</summary>
    public void ObserveCancellation(string instanceId, string jobId, long revision, RemoteJobState remoteState, string? manifestRevision) {
        RequireMode(!Mode.IsSource, "observe remote cancellation");
        Require(State.CancellationRequested, "observe remote cancellation");
        if (instanceId != State.InstanceId || jobId != State.JobId || revision < 0) {
            throw new InvalidOperationException("The cancellation snapshot is invalid or belongs to another job.");
        }

        var observed = RemoteJobStateDefinition.For(remoteState);
        if (State.Phase == IntegrationTransferPhase.Cancelled || revision < State.RemoteRevision) {
            return;
        }

        Require(!Phase.PassedImportBoundary, "stop after its import started");
        if (LastRemote?.IsTerminal == true && (State.LastRemoteState != remoteState || State.ManifestRevision != manifestRevision)) {
            throw new InvalidOperationException("Cancellation cannot rewrite a terminal execution result.");
        }

        if (observed.RequiresManifest && !IsManifestRevision(manifestRevision)) {
            throw new InvalidOperationException("Successful execution still requires its exact sealed output revision.");
        }

        Change(State with {
            RemoteRevision = revision,
            LastRemoteState = remoteState,
            ManifestRevision = manifestRevision,
            Phase = observed.IsTerminal ? IntegrationTransferPhase.Cancelled : State.Phase
        });
    }

    #endregion

    #region Actions - Outputs

    /// <summary>Freezes every artifact from the accepted manifest revision; partial results require a separate reviewed intent.</summary>
    public void AcceptManifest(IntegrationArtifactManifest manifest) {
        Require(LastRemote?.RequiresManifest == true && !State.CancellationRequested, "accept an output manifest");
        if (manifest.JobId != State.JobId || manifest.Revision != State.ManifestRevision) {
            throw new InvalidOperationException("The output manifest belongs to another job or revision.");
        }

        if (State.Artifacts is { } previous) {
            if (!previous.SequenceEqual(manifest.Artifacts)) {
                throw new InvalidOperationException("A sealed artifact manifest changed its contents.");
            }

            if (Phase.AwaitsManifest) {
                Change(State with {
                    Phase = State.VerifiedArtifactIds?.Count == previous.Count
                        ? IntegrationTransferPhase.Importing
                        : IntegrationTransferPhase.Transferring
                });
            }

            return;
        }

        Require(Phase.AwaitsManifest, "accept an output manifest");
        Change(State with {
            Artifacts = manifest.Artifacts.ToArray(),
            VerifiedArtifactIds = [],
            Imports = [],
            Phase = IntegrationTransferPhase.Transferring
        });
    }

    /// <summary>Holds missing remote outputs separately from execution success, retaining all accepted byte evidence for reconciliation.</summary>
    public void HoldUnavailableRemoteOutputs() {
        RequireMode(!Mode.IsSource, "hold unavailable remote outputs");
        if (State.Phase == IntegrationTransferPhase.NeedsReview) {
            return;
        }

        Require(Phase.RetrievesOutputs, "hold unavailable remote outputs");
        Change(State with { Phase = IntegrationTransferPhase.NeedsReview });
    }

    /// <summary>Records local byte evidence only when its size and SHA-256 match the frozen remote manifest.</summary>
    public void RecordVerified(string artifactId, long sizeBytes, string sha256) {
        Require(!State.CancellationRequested, "record verified bytes");
        var artifact = FindArtifact(artifactId);
        if (artifact.SizeBytes != sizeBytes || !artifact.Sha256.Equals(sha256, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("Local artifact bytes do not match the sealed manifest.");
        }

        var verified = State.VerifiedArtifactIds ?? [];
        if (verified.Contains(artifactId)) {
            return;
        }

        Require(Phase.VerifiesBytes, "record verified bytes");
        var next = verified.Append(artifactId).ToArray();
        Change(State with {
            VerifiedArtifactIds = next,
            Phase = next.Length == State.Artifacts!.Count ? IntegrationTransferPhase.Importing : IntegrationTransferPhase.Transferring
        });
    }

    /// <summary>Records already-committed local ownership. Repeating the same receipt is harmless; changing it is rejected.</summary>
    public void RecordImported(IntegrationArtifactImport imported) {
        var artifact = FindArtifact(imported.ArtifactId);
        if (State.VerifiedArtifactIds?.Contains(artifact.Id) != true || string.IsNullOrWhiteSpace(imported.Sha256)
            || artifact.Sha256 != imported.Sha256.ToLowerInvariant() || imported.EntityIds is null
            || imported.EntityIds.Any(id => id == Guid.Empty) || imported.ContainerEntityId == Guid.Empty
            || artifact.Role == IntegrationArtifactRole.Content && imported.EntityIds.Count == 0) {
            throw new InvalidOperationException("An import needs verified bytes and exact local source ownership.");
        }

        var imports = State.Imports ?? [];
        if (imports.FirstOrDefault(item => item.ArtifactId == imported.ArtifactId) is { } previous) {
            if (previous.ContainerEntityId != imported.ContainerEntityId || previous.Sha256 != imported.Sha256.ToLowerInvariant()
                || !previous.EntityIds.Order().SequenceEqual(imported.EntityIds.Distinct().Order())) {
                throw new InvalidOperationException("An already committed artifact import cannot be replaced.");
            }

            return;
        }

        Require(State.Phase == IntegrationTransferPhase.Importing, "record a committed import");
        var normalized = imported with {
            Sha256 = imported.Sha256.ToLowerInvariant(),
            EntityIds = imported.EntityIds.Distinct().Order().ToArray()
        };
        var next = imports.Append(normalized).ToArray();
        var complete = next.Length == State.Artifacts!.Count;
        var finished = Mode.AcknowledgesImports ? IntegrationTransferPhase.AwaitingAcknowledgement : IntegrationTransferPhase.Completed;
        Change(State with {
            Imports = next,
            ReceiptId = complete ? State.ReceiptId ?? Guid.NewGuid() : null,
            Phase = complete ? finished : IntegrationTransferPhase.Importing
        });
    }

    /// <summary>Completes the independently retryable acknowledgement without executing another download or import.</summary>
    public void RecordAcknowledgement(Guid receiptId) {
        if (!Mode.AcknowledgesImports || State.ReceiptId != receiptId || receiptId == Guid.Empty) {
            throw new InvalidOperationException("The acknowledgement belongs to another receipt.");
        }

        if (State.Phase == IntegrationTransferPhase.Completed) {
            return;
        }

        Require(State.Phase == IntegrationTransferPhase.AwaitingAcknowledgement, "record its acknowledgement");
        Change(State with { Phase = IntegrationTransferPhase.Completed });
    }

    private IntegrationArtifact FindArtifact(string id) =>
        State.Artifacts?.FirstOrDefault(artifact => artifact.Id == id)
        ?? throw new InvalidOperationException("The artifact is absent from the accepted manifest.");

    private static bool IsManifestRevision(string? manifestRevision) =>
        !string.IsNullOrWhiteSpace(manifestRevision) && manifestRevision.Length <= MaximumIdentifierLength;

    #endregion

    #region Actions - Transitions

    private void Require(bool allowed, string action) {
        if (!allowed) {
            throw new InvalidOperationException($"A transfer that is {Phase.Description} cannot {action}.");
        }
    }

    private void RequireMode(bool allowed, string action) {
        if (!allowed) {
            throw new InvalidOperationException($"A {Mode.Description} cannot {action}.");
        }
    }

    private void Change(IntegrationTransferState next) => State = next with { Revision = State.Revision + 1 };

    #endregion
}
