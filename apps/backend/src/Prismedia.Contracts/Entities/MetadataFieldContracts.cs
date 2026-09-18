using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Entities;

/// <summary>Current value or section provenance and enrichment protection. Unknown origin does not imply a provider or user authored an older value.</summary>
public sealed record MetadataFieldResponse(MetadataPatchField Field, MetadataValueOrigin Origin, string? ProviderId,
    DateTimeOffset? ObservedAt, decimal? Confidence, bool IsCleared, bool IsLocked, long Revision);
/// <summary>Changes one field's enrichment lock using the last observed evidence revision.</summary>
public sealed record UpdateMetadataFieldLockRequest(long ExpectedRevision, bool IsLocked);
