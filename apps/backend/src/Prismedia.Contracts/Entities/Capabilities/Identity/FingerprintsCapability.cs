namespace Prismedia.Contracts.Entities;

/// <summary>API-facing hash or fingerprint value.</summary>
/// <param name="Algorithm">Stable algorithm code, such as md5, oshash, or phash.</param>
/// <param name="Value">Hash or fingerprint value.</param>
public sealed record EntityFingerprint(string Algorithm, string Value);

/// <summary>API-facing fingerprint capability.</summary>
/// <param name="Items">Hash and fingerprint values associated with the entity.</param>
[CapabilityKind("fingerprints")]
public sealed record FingerprintsCapability(IReadOnlyList<EntityFingerprint> Items) : EntityCapability;
