using Prismedia.Application.Jobs.Ports;

namespace Prismedia.Application.Integrations;

/// <summary>Frozen gallery intent and the exact verified staging evidence for every ordered member.</summary>
public sealed record IntegrationGalleryPlacementRequest(Guid OperationId, IntegrationTransferPlan Plan, LibraryRootData Root,
    IntegrationGalleryOutputSet Outputs, IReadOnlyList<VerifiedIntegrationArtifact> VerifiedArtifacts);
