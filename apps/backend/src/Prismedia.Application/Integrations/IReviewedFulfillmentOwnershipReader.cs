using Prismedia.Contracts.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Projects current local ownership for canonical manager-review work without changing library state.</summary>
public interface IReviewedFulfillmentOwnershipReader {
    /// <summary>
    /// Returns active ownership overlapping the exact reviewed work. Finite container targets remain
    /// independent so ownership of one episode does not claim an unselected or disjoint episode.
    /// </summary>
    Task<IReadOnlyList<ReviewedFulfillmentOwnership>> ListAsync(
        ManagedLookupInput work,
        CancellationToken cancellationToken);
}
