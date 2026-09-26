using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Checks media structure before verified transport bytes enter a library.</summary>
public interface IIntegrationMediaVerifier {
    #region Abstract Methods

    /// <summary>Rejects unsupported, unreadable, oversized, or unsafe media structures without changing staged bytes.</summary>
    Task VerifyAsync(VerifiedIntegrationArtifact artifact, EntityKind kind, CancellationToken cancellationToken);

    #endregion
}
