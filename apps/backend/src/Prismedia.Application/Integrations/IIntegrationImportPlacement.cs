using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Places verified bytes into the explicitly accepted library without replacing existing files.</summary>
public interface IIntegrationImportPlacement {
    #region Abstract Methods

    /// <summary>Returns exact already-published bytes after validating the frozen destination, or null when no final file exists.</summary>
    Task<VerifiedIntegrationArtifact?> ReadPlacedAsync(Guid operationId, IntegrationTransferPlan plan, LibraryRootData root,
        IntegrationArtifact artifact, CancellationToken cancellationToken);

    /// <summary>Returns an exact final path; retries reuse matching bytes and reject a moved or unavailable destination.</summary>
    Task<string> PlaceAsync(Guid operationId, IntegrationTransferPlan plan, LibraryRootData root,
        VerifiedIntegrationArtifact artifact, CancellationToken cancellationToken);

    #endregion
}
