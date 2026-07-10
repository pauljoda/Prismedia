namespace Prismedia.Application.Plugins;

/// <summary>
/// Why a persisted entity may or may not enter Identify. Identify operates only on real library
/// media: a Wanted placeholder is metadata awaiting acquisition, and generated artwork is not
/// source media.
/// </summary>
public enum IdentifyTargetEligibilityStatus {
    /// <summary>The entity no longer exists.</summary>
    Missing,

    /// <summary>The entity is a request-created placeholder awaiting source media.</summary>
    Wanted,

    /// <summary>The entity's structural subtree owns no source-media file binding.</summary>
    NoSourceMedia,

    /// <summary>The entity or one of its structural descendants owns source media and may enter Identify.</summary>
    Eligible
}

/// <summary>
/// Eligibility snapshot for one persisted Identify target.
/// </summary>
/// <param name="EntityId">Entity whose eligibility was evaluated.</param>
/// <param name="Status">Current eligibility outcome.</param>
public sealed record IdentifyTargetEligibility(
    Guid EntityId,
    IdentifyTargetEligibilityStatus Status) {
    /// <summary>Whether the target may enter or continue Identify.</summary>
    public bool IsEligible => Status == IdentifyTargetEligibilityStatus.Eligible;

    /// <summary>
    /// Throws the canonical exception for a missing or ineligible target. Callers that intentionally
    /// skip ineligible batch members can inspect <see cref="IsEligible"/> instead.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The entity no longer exists.</exception>
    /// <exception cref="IdentifyTargetNotEligibleException">The entity has no identifiable source media.</exception>
    public void EnsureEligible() {
        if (IsEligible) {
            return;
        }

        if (Status == IdentifyTargetEligibilityStatus.Missing) {
            throw new KeyNotFoundException($"Entity '{EntityId}' was not found.");
        }

        throw new IdentifyTargetNotEligibleException(this);
    }
}

/// <summary>Raised when Identify is requested for a Wanted or source-media-free entity.</summary>
public sealed class IdentifyTargetNotEligibleException : InvalidOperationException {
    /// <summary>Creates an exception for the supplied eligibility result.</summary>
    public IdentifyTargetNotEligibleException(IdentifyTargetEligibility eligibility)
        : base(MessageFor(eligibility)) {
        Eligibility = eligibility;
    }

    /// <summary>The eligibility result that prevented Identify.</summary>
    public IdentifyTargetEligibility Eligibility { get; }

    private static string MessageFor(IdentifyTargetEligibility eligibility) =>
        eligibility.Status switch {
            IdentifyTargetEligibilityStatus.Wanted =>
                $"Entity '{eligibility.EntityId}' is Wanted and cannot be identified until source media is on disk.",
            IdentifyTargetEligibilityStatus.NoSourceMedia =>
                $"Entity '{eligibility.EntityId}' has no source media on disk to identify.",
            _ => $"Entity '{eligibility.EntityId}' is not eligible for Identify."
        };
}

/// <summary>
/// Reads Identify eligibility from the persistence boundary using the canonical source-backed Entity
/// subtree projection. Related entities never make a target eligible; structural descendants do.
/// </summary>
public interface IIdentifyTargetEligibilityService {
    /// <summary>Evaluates one persisted entity.</summary>
    Task<IdentifyTargetEligibility> EvaluateAsync(Guid entityId, CancellationToken cancellationToken);

    /// <summary>Evaluates a batch in one persistence round-trip, returning one result per distinct id.</summary>
    Task<IReadOnlyDictionary<Guid, IdentifyTargetEligibility>> EvaluateManyAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken);
}
