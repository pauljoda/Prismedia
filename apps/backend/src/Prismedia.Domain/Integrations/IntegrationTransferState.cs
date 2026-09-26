using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>Rehydratable transfer state; operation identity is allocated before any remote side effect.</summary>
public sealed record IntegrationTransferState(Guid OperationId, Guid ConnectionId, string? InstanceId, long Revision,
    IntegrationTransferPhase Phase, string? JobId = null, long RemoteRevision = -1, string? ManifestRevision = null,
    IReadOnlyList<IntegrationArtifact>? Artifacts = null, IReadOnlyList<string>? VerifiedArtifactIds = null,
    IReadOnlyList<IntegrationArtifactImport>? Imports = null, Guid? ReceiptId = null,
    IntegrationTransferMode Mode = IntegrationTransferMode.RemoteExecutor, RemoteJobState? LastRemoteState = null,
    bool CancellationRequested = false, SourceAcquisitionState? LastSourceState = null,
    double? SourceProgress = null, string? SourceProblem = null);
