using EntityFingerprint = Prismedia.Domain.Capabilities.CapabilityFingerprints.Item;

namespace Prismedia.Contracts.Entities;

/// <summary>API-facing fingerprint capability.</summary>
/// <param name="Items">Hash and fingerprint values associated with the entity.</param>
[CapabilityKind("fingerprints")]
public sealed record FingerprintsCapability(IReadOnlyList<EntityFingerprint> Items) : EntityCapability;
