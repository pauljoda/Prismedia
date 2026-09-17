using Prismedia.Contracts.Entities;

namespace Prismedia.Application.Entities;

/// <summary>Emits saved external-library provenance supplied by an authorized detail-read boundary.</summary>
[EntityCapabilityProjector(210)]
internal sealed class ExternalLibraryProvenanceCapabilityProjector : EntityCapabilityProjector<ExternalLibraryProvenanceCapability> {
    public override ExternalLibraryProvenanceCapability? Project(EntityCapabilityProjectionContext context) =>
        context.ExternalLibraryProvenance;
}
