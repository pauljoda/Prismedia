using Prismedia.Application.Jobs.Ports;

namespace Prismedia.Application.Integrations;

/// <summary>Frozen gallery intent and the exact verified staging evidence for every ordered member.</summary>
public sealed record IntegrationGalleryPlacementRequest(Guid OperationId, IntegrationTransferPlan Plan, LibraryRootData Root,
    IntegrationGalleryOutputSet Outputs, IReadOnlyList<VerifiedIntegrationArtifact> VerifiedArtifacts);

/// <summary>One atomically published gallery folder and its exact artifact-to-file mapping.</summary>
public sealed record PlacedIntegrationGallery(string FolderPath, IReadOnlyDictionary<string, string> Files);

/// <summary>Publishes an entire verified image group without exposing partial galleries or replacing existing content.</summary>
public interface IIntegrationGalleryPlacement {
    /// <summary>Retries reuse only an exact existing group; conflicting or additional content requires review.</summary>
    Task<PlacedIntegrationGallery> PlaceAsync(IntegrationGalleryPlacementRequest request, CancellationToken cancellationToken);
}
