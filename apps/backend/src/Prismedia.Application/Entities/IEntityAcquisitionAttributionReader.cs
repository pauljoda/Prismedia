using Prismedia.Contracts.Entities;

namespace Prismedia.Application.Entities;

/// <summary>Projects retained acquisition attribution after the caller has authorized access to the library entity.</summary>
public interface IEntityAcquisitionAttributionReader {
    /// <summary>Reads only exact, completed import receipts; returns null when no source statements were saved.</summary>
    Task<AcquisitionAttributionCapability?> ReadAsync(Guid entityId, CancellationToken cancellationToken);
}
