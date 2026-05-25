using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Capabilities;

/// <summary>
/// Mutable fingerprint capability for stable hashes or perceptual fingerprints.
/// </summary>
public sealed class CapabilityFingerprints(IEnumerable<CapabilityFingerprints.Item>? items = null)
    : CollectionCapability<CapabilityFingerprints.Item>(items) {
    /// <summary>
    /// One hash or fingerprint value associated with an entity or one of its files.
    /// </summary>
    /// <param name="Algorithm">Hash algorithm used to produce this fingerprint.</param>
    /// <param name="Value">Hash or fingerprint value.</param>
    public sealed record Item(FingerprintAlgorithm Algorithm, string Value);

    /// <summary>
    /// Adds a fingerprint, replacing any existing value for the same algorithm.
    /// </summary>
    /// <param name="algorithm">Hash algorithm.</param>
    /// <param name="value">Hash or fingerprint value.</param>
    public void Set(FingerprintAlgorithm algorithm, string value) {
        Upsert(item => item.Algorithm == algorithm, new Item(algorithm, value));
    }
}
