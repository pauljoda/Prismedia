using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Persistence.Entities;

/// <summary>Current value provenance and user protection for one supported scalar metadata field.</summary>
public sealed class EntityMetadataFieldRow {
    public Guid EntityId { get; set; }
    public MetadataPatchField Field { get; set; }
    public MetadataValueOrigin Origin { get; set; }
    public string? ProviderId { get; set; }
    public DateTimeOffset? ObservedAt { get; set; }
    public decimal? Confidence { get; set; }
    public bool IsCleared { get; set; }
    public bool IsLocked { get; set; }
    public long Revision { get; set; }
    /// <summary>Rehydrates the domain evidence without synthesizing missing history.</summary>
    public MetadataFieldEvidence Evidence() => new(Origin, ProviderId, ObservedAt, Confidence, IsCleared, IsLocked, Revision);
    /// <summary>Copies a validated domain transition to its EF persistence record.</summary>
    public void Apply(MetadataFieldEvidence evidence) {
        Origin = evidence.Origin; ProviderId = evidence.ProviderId; ObservedAt = evidence.ObservedAt;
        Confidence = evidence.Confidence; IsCleared = evidence.IsCleared; IsLocked = evidence.IsLocked; Revision = evidence.Revision;
    }
}
