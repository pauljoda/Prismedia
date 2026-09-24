namespace Prismedia.Application.Integrations;

/// <summary>Retrieves exact artifact bytes into private staging with bounded size, credential scope, and resumable verification.</summary>
public interface IIntegrationArtifactTransfer {
    #region Abstract Methods

    /// <summary>Returns verified bytes; retries reuse a verified receipt or resume a hash-pinned partial transfer. A receipt alone
    /// never proves the file still exists or is intact.</summary>
    Task<VerifiedIntegrationArtifact> TransferAsync(IntegrationArtifactTransferRequest request, CancellationToken cancellationToken);

    /// <summary>Reopens already verified bytes using persisted evidence only. Missing staging never initiates another remote
    /// download.</summary>
    Task<VerifiedIntegrationArtifact?> ReadVerifiedAsync(Guid operationId, string artifactId, string fileName, long sizeBytes,
        string sha256, CancellationToken cancellationToken);

    #endregion
}
