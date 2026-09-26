using Prismedia.Contracts.Entities;

namespace Prismedia.Application.Entities;

/// <summary>Projects saved external-library ownership after the caller has authorized the Entity detail.</summary>
public interface IEntityExternalLibraryProvenanceReader {
    /// <summary>Reads the effective mapped library and any exact retained holding association without contacting its source.</summary>
    Task<ExternalLibraryProvenanceCapability?> ReadAsync(Guid entityId, CancellationToken cancellationToken);
}
