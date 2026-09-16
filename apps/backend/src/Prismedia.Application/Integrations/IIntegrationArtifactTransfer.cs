using Prismedia.Contracts.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>A stable artifact identity and current server-only retrieval authorization. The allowed origin is configured by the user, not chosen by a response redirect.</summary>
public sealed record IntegrationArtifactTransferRequest(Guid OperationId, string ArtifactId, string AllowedOrigin,
    HttpArtifactDelivery Delivery, long MaximumBytes);

/// <summary>Durable local evidence of exact bytes in private staging. The path is never a provider-chosen destination.</summary>
public sealed record VerifiedIntegrationArtifact(string ArtifactId, string Path, long SizeBytes, string Sha256, string FileName);

/// <summary>Retrieves exact artifact bytes into private staging with bounded size, credential scope, and resumable verification.</summary>
public interface IIntegrationArtifactTransfer {
    /// <summary>Returns verified bytes; retries reuse a verified receipt or resume a hash-pinned partial transfer. A receipt alone never proves the file still exists or is intact.</summary>
    Task<VerifiedIntegrationArtifact> TransferAsync(IntegrationArtifactTransferRequest request, CancellationToken cancellationToken);
}
