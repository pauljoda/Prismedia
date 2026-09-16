using Prismedia.Application.Jobs.Ports;

namespace Prismedia.Application.Integrations;

/// <summary>Places verified bytes into the explicitly accepted library without replacing existing files.</summary>
public interface IIntegrationImportPlacement {
    /// <summary>Returns an exact final path; retries reuse matching bytes and reject a moved or unavailable destination.</summary>
    Task<string> PlaceAsync(Guid operationId, IntegrationTransferPlan plan, LibraryRootData root,
        VerifiedIntegrationArtifact artifact, CancellationToken cancellationToken);
}
