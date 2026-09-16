using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>Exact imported output and the local entities that own it. Artwork/sidecars may have no separate source entity.</summary>
public sealed record IntegrationArtifactImport(string ArtifactId, string Sha256, IReadOnlyList<Guid> EntityIds);

/// <summary>Rehydratable transfer state; operation identity is allocated before any remote side effect.</summary>
public sealed record IntegrationTransferState(Guid OperationId, Guid ConnectionId, string? InstanceId, long Revision,
    IntegrationTransferPhase Phase, string? JobId = null, long RemoteRevision = -1, string? ManifestRevision = null,
    IReadOnlyList<IntegrationArtifact>? Artifacts = null, IReadOnlyList<string>? VerifiedArtifactIds = null,
    IReadOnlyList<IntegrationArtifactImport>? Imports = null, Guid? ReceiptId = null,
    IntegrationTransferMode Mode = IntegrationTransferMode.RemoteExecutor);

/// <summary>Enforces submission recovery and the separation between remote completion, verified bytes, committed imports, and acknowledgement.</summary>
public sealed class IntegrationTransfer(IntegrationTransferState state) {
    /// <summary>Immutable state to persist with optimistic concurrency after each external boundary.</summary>
    public IntegrationTransferState State { get; private set; } = state;
    /// <summary>Direct retrieval can stop only before a persisted import boundary permits library placement.</summary>
    public bool CanCancelSource => State.Mode == IntegrationTransferMode.SourceDownload && State.Phase == IntegrationTransferPhase.Transferring;

    /// <summary>Creates a finite acquisition intent for a verified persistent executor installation.</summary>
    public static IntegrationTransfer Create(Guid operationId, Guid connectionId, string instanceId) {
        if (operationId == Guid.Empty || connectionId == Guid.Empty || string.IsNullOrWhiteSpace(instanceId) || instanceId.Length > 512)
            throw new ArgumentException("Stable operation, connection, and remote installation identities are required.");
        return new(new(operationId, connectionId, instanceId, 1, IntegrationTransferPhase.PendingSubmission));
    }

    /// <summary>Creates direct source acquisition without inventing a remote job or installation identity.</summary>
    public static IntegrationTransfer CreateSourceDownload(Guid operationId, Guid connectionId) {
        if (operationId == Guid.Empty || connectionId == Guid.Empty) throw new ArgumentException("Stable operation and connection identities are required.");
        return new(new(operationId, connectionId, null, 1, IntegrationTransferPhase.Transferring, Mode: IntegrationTransferMode.SourceDownload));
    }

    /// <summary>Seals the exact downloaded source bytes after local verification; direct catalogs need not supply a pre-existing hash.</summary>
    public void AcceptSourceArtifact(IntegrationArtifact artifact) {
        if (State.Mode != IntegrationTransferMode.SourceDownload) throw InvalidTransition();
        var manifest = new IntegrationArtifactManifest(State.OperationId.ToString("N"), artifact.Sha256, true, 1, [artifact]);
        if (State.Artifacts is { } previous) {
            if (!previous.SequenceEqual(manifest.Artifacts)) throw new InvalidOperationException("The accepted source bytes changed.");
            return;
        }
        if (State.Phase != IntegrationTransferPhase.Transferring) throw InvalidTransition();
        Change(State with { Artifacts = manifest.Artifacts, VerifiedArtifactIds = [artifact.Id], Imports = [],
            Phase = IntegrationTransferPhase.Importing });
    }

    /// <summary>Cancels direct retrieval before local placement starts; persisted cancellation fences any stale downloading worker.</summary>
    public void CancelSourceDownload() {
        if (State.Mode != IntegrationTransferMode.SourceDownload) throw InvalidTransition();
        if (State.Phase == IntegrationTransferPhase.Cancelled) return;
        if (!CanCancelSource) throw InvalidTransition();
        Change(State with { Phase = IntegrationTransferPhase.Cancelled });
    }

    /// <summary>Persist this state before issuing POST. A crash or timeout must recover by the same operation key.</summary>
    public void BeginSubmission() {
        if (State.Mode != IntegrationTransferMode.RemoteExecutor || State.Phase is not (IntegrationTransferPhase.PendingSubmission or IntegrationTransferPhase.SubmissionUncertain)) throw InvalidTransition();
        Change(State with { Phase = IntegrationTransferPhase.SubmissionUncertain });
    }

    /// <summary>Accepts only this operation's job on the original installation, including a recovered ambiguous submission.</summary>
    public void AcceptSubmission(Guid operationId, string instanceId, string jobId) {
        if (State.Mode != IntegrationTransferMode.RemoteExecutor || operationId != State.OperationId || instanceId != State.InstanceId || string.IsNullOrWhiteSpace(jobId) || jobId.Length > 512
            || State.JobId is not null && State.JobId != jobId) throw new InvalidOperationException("The remote job identity does not match this transfer.");
        if (State.JobId == jobId) return;
        if (State.Phase != IntegrationTransferPhase.SubmissionUncertain) throw InvalidTransition();
        Change(State with { JobId = jobId, Phase = IntegrationTransferPhase.AwaitingRemote });
    }

    /// <summary>Observes a monotonic authoritative job snapshot. Execution success only permits manifest retrieval.</summary>
    public void Observe(string instanceId, string jobId, long revision, RemoteJobState remoteState, string? manifestRevision) {
        if (State.Mode != IntegrationTransferMode.RemoteExecutor || instanceId != State.InstanceId || jobId != State.JobId || revision < 0 || !Enum.IsDefined(remoteState))
            throw new InvalidOperationException("The remote snapshot is invalid or belongs to another job.");
        if (revision <= State.RemoteRevision) return;
        if (State.Phase is not (IntegrationTransferPhase.AwaitingRemote or IntegrationTransferPhase.NeedsReview))
            throw InvalidTransition();
        if (remoteState == RemoteJobState.Succeeded && (string.IsNullOrWhiteSpace(manifestRevision) || manifestRevision.Length > 512))
            throw new InvalidOperationException("A successful executor must identify its sealed manifest revision.");
        var phase = remoteState switch {
            RemoteJobState.Queued or RemoteJobState.Running => IntegrationTransferPhase.AwaitingRemote,
            RemoteJobState.Succeeded => IntegrationTransferPhase.AwaitingArtifacts,
            RemoteJobState.Partial or RemoteJobState.Expired => IntegrationTransferPhase.NeedsReview,
            RemoteJobState.Failed => IntegrationTransferPhase.Failed,
            RemoteJobState.Cancelled => IntegrationTransferPhase.Cancelled,
            _ => throw InvalidTransition()
        };
        Change(State with { RemoteRevision = revision, Phase = phase, ManifestRevision = remoteState == RemoteJobState.Succeeded ? manifestRevision : null });
    }

    /// <summary>Freezes every artifact from the accepted manifest revision; partial results require a separate reviewed intent.</summary>
    public void AcceptManifest(IntegrationArtifactManifest manifest) {
        if (manifest.JobId != State.JobId || manifest.Revision != State.ManifestRevision)
            throw new InvalidOperationException("The output manifest belongs to another job or revision.");
        if (State.Artifacts is { } previous) {
            if (!previous.SequenceEqual(manifest.Artifacts)) throw new InvalidOperationException("A sealed artifact manifest changed its contents.");
            return;
        }
        if (State.Phase != IntegrationTransferPhase.AwaitingArtifacts) throw InvalidTransition();
        Change(State with { Artifacts = manifest.Artifacts.ToArray(), VerifiedArtifactIds = [], Imports = [], Phase = IntegrationTransferPhase.Transferring });
    }

    /// <summary>Records local byte evidence only when its size and SHA-256 match the frozen remote manifest.</summary>
    public void RecordVerified(string artifactId, long sizeBytes, string sha256) {
        var artifact = FindArtifact(artifactId);
        if (artifact.SizeBytes != sizeBytes || !artifact.Sha256.Equals(sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Local artifact bytes do not match the sealed manifest.");
        var verified = State.VerifiedArtifactIds ?? [];
        if (verified.Contains(artifactId)) return;
        if (State.Phase is not (IntegrationTransferPhase.Transferring or IntegrationTransferPhase.Importing)) throw InvalidTransition();
        var next = verified.Append(artifactId).ToArray();
        Change(State with { VerifiedArtifactIds = next,
            Phase = next.Length == State.Artifacts!.Count ? IntegrationTransferPhase.Importing : IntegrationTransferPhase.Transferring });
    }

    /// <summary>Records already-committed local ownership. Repeating the same receipt is harmless; changing it is rejected.</summary>
    public void RecordImported(IntegrationArtifactImport imported) {
        var artifact = FindArtifact(imported.ArtifactId);
        if (State.VerifiedArtifactIds?.Contains(artifact.Id) != true || string.IsNullOrWhiteSpace(imported.Sha256) || artifact.Sha256 != imported.Sha256.ToLowerInvariant()
            || imported.EntityIds is null || imported.EntityIds.Any(id => id == Guid.Empty)
            || artifact.Role == IntegrationArtifactRole.Content && imported.EntityIds.Count == 0)
            throw new InvalidOperationException("An import needs verified bytes and exact local source ownership.");
        var imports = State.Imports ?? [];
        if (imports.FirstOrDefault(item => item.ArtifactId == imported.ArtifactId) is { } previous) {
            if (previous.Sha256 != imported.Sha256.ToLowerInvariant() || !previous.EntityIds.Order().SequenceEqual(imported.EntityIds.Distinct().Order()))
                throw new InvalidOperationException("An already committed artifact import cannot be replaced.");
            return;
        }
        if (State.Phase != IntegrationTransferPhase.Importing) throw InvalidTransition();
        var next = imports.Append(imported with { Sha256 = imported.Sha256.ToLowerInvariant(), EntityIds = imported.EntityIds.Distinct().Order().ToArray() }).ToArray();
        var complete = next.Length == State.Artifacts!.Count;
        Change(State with { Imports = next, ReceiptId = complete ? State.ReceiptId ?? Guid.NewGuid() : null,
            Phase = complete ? State.Mode == IntegrationTransferMode.RemoteExecutor ? IntegrationTransferPhase.AwaitingAcknowledgement : IntegrationTransferPhase.Completed : IntegrationTransferPhase.Importing });
    }

    /// <summary>Completes the independently retryable acknowledgement without executing another download or import.</summary>
    public void RecordAcknowledgement(Guid receiptId) {
        if (State.Mode != IntegrationTransferMode.RemoteExecutor || State.ReceiptId != receiptId || receiptId == Guid.Empty) throw new InvalidOperationException("The acknowledgement belongs to another receipt.");
        if (State.Phase == IntegrationTransferPhase.Completed) return;
        if (State.Phase != IntegrationTransferPhase.AwaitingAcknowledgement) throw InvalidTransition();
        Change(State with { Phase = IntegrationTransferPhase.Completed });
    }

    private IntegrationArtifact FindArtifact(string id) => State.Artifacts?.FirstOrDefault(artifact => artifact.Id == id)
        ?? throw new InvalidOperationException("The artifact is absent from the accepted manifest.");
    private void Change(IntegrationTransferState next) => State = next with { Revision = State.Revision + 1 };
    private static InvalidOperationException InvalidTransition() => new("This operation is not valid at the current transfer phase.");
}
