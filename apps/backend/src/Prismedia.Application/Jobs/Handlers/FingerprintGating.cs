using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Shared decision logic for whether an entity should have a fingerprint job enqueued.
/// A fingerprint job is only worthwhile when at least one enabled algorithm is still missing,
/// so that disabling MD5 (the expensive full-file read) actually skips the work rather than
/// re-running it on every scan.
/// </summary>
public static class FingerprintGating {
    /// <summary>
    /// Returns true when an enabled fingerprint algorithm is missing for the entity, using
    /// a precomputed <see cref="DownstreamNeeds"/> snapshot from a batched scan.
    /// </summary>
    public static bool ShouldFingerprint(LibrarySettingsData settings, DownstreamNeeds needs) =>
        (settings.AutoGenerateOshash && needs.MissingOshash) ||
        (settings.AutoGenerateMd5 && needs.MissingMd5);

    /// <summary>
    /// Returns true when an enabled fingerprint algorithm is missing for the entity, querying
    /// fingerprint presence on demand. Used by per-item scan handlers that have not batched needs.
    /// </summary>
    public static async Task<bool> ShouldFingerprintAsync(
        IDownstreamNeedsPersistence persistence,
        LibrarySettingsData settings,
        Guid entityId,
        CancellationToken cancellationToken) {
        if (settings.AutoGenerateOshash &&
            !await persistence.HasEntityFingerprintAsync(entityId, FingerprintAlgorithm.Oshash, cancellationToken)) {
            return true;
        }

        if (settings.AutoGenerateMd5 &&
            !await persistence.HasEntityFingerprintAsync(entityId, FingerprintAlgorithm.Md5, cancellationToken)) {
            return true;
        }

        return false;
    }
}
