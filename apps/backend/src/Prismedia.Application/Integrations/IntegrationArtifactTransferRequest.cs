using Prismedia.Contracts.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>A stable artifact identity and current server-only retrieval authorization. The allowed origin is configured by the
/// user, not chosen by a response redirect.</summary>
public sealed record IntegrationArtifactTransferRequest(Guid OperationId, string ArtifactId, string AllowedOrigin,
    HttpArtifactDelivery Delivery, long MaximumBytes);
