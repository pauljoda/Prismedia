using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Entities;

/// <summary>Reads current descriptive provenance and changes field protection under the entity mutation boundary.</summary>
public interface IMetadataFieldService {
    /// <summary>Lists supported scalar fields, including honest unknown history; null means the entity does not exist.</summary>
    Task<IReadOnlyList<MetadataFieldResponse>?> ReadAsync(Guid entityId, CancellationToken cancellationToken);
    /// <summary>Changes a lock without rewriting the value or its source; a stale revision is rejected.</summary>
    Task<MetadataFieldResponse?> SetLockAsync(Guid entityId, MetadataPatchField field, UpdateMetadataFieldLockRequest request, CancellationToken cancellationToken);
}
/// <summary>The field changed while a user was viewing its provenance.</summary>
public sealed class MetadataFieldConflictException() : Exception("This metadata field changed. Refresh its provenance before changing protection.");
