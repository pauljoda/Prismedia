using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Checks publication structure before verified transport bytes enter a library.</summary>
public interface IIntegrationPublicationVerifier {
    /// <summary>Rejects unsupported, unreadable, oversized, or unsafe publication structures without changing staged bytes.</summary>
    Task VerifyAsync(VerifiedIntegrationArtifact artifact, EntityKind kind, CancellationToken cancellationToken);
}
