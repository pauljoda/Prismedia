namespace Prismedia.Application.Requests;

/// <summary>
/// Outcome of a post-request structural hydration. A non-null result means the Entity kind participates
/// in child hydration; <see cref="Hydrated"/> distinguishes a successful provider expansion from a
/// best-effort miss, while <see cref="Enrichment"/> lets the same provider response fill acquisition
/// metadata without a duplicate lookup.
/// </summary>
public sealed record RequestChildHydrationResult(
    bool Hydrated,
    RequestMetadataEnrichment? Enrichment);

/// <summary>
/// Expands an acquired structural unit into its wanted child graph after the interactive request has
/// returned. Album acquisitions use this seam to materialize tracks without delaying an artist commit.
/// </summary>
public interface IRequestChildHydrator {
    /// <summary>
    /// Hydrates structural children for a supported Entity. Returns null when the Entity kind does not
    /// participate in child hydration, otherwise a best-effort result for the exact persisted plugin route.
    /// </summary>
    Task<RequestChildHydrationResult?> HydrateAsync(
        Guid entityId,
        bool hideNsfw,
        CancellationToken cancellationToken);
}

public sealed partial class RequestCommitService {
    /// <inheritdoc />
    public async Task<RequestChildHydrationResult?> HydrateAsync(
        Guid entityId,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var entity = await wanted.GetEntityAsync(entityId, cancellationToken);
        var descriptor = entity is null
            ? null
            : RequestKindRegistry.FindChildMaterializingUnit(entity.Kind);
        if (entity is null || descriptor is null) {
            return null;
        }
        if (entity.ProviderIdentity is not { } route) {
            return new RequestChildHydrationResult(Hydrated: false, Enrichment: null);
        }

        var review = await proposals.ResolveFreshReviewAsync(
            descriptor,
            route,
            hideNsfw,
            cancellationToken);
        if (review?.Proposal.Patch is not { } patch) {
            return new RequestChildHydrationResult(Hydrated: false, Enrichment: null);
        }

        var prepared = new PreparedPhantomDescendants(
            review.Proposal,
            ResolveReviewedStructuralChildren(review.Proposal, review.Targets));
        await EnsurePhantomDescendantsAsync(
            descriptor,
            route.Identity,
            entityId,
            route.PluginId,
            prepared,
            hideNsfw,
            cancellationToken);

        return new RequestChildHydrationResult(
            Hydrated: true,
            new RequestMetadataEnrichment(
                patch.Description,
                RequestProposalReading.BestImage(review.Proposal),
                RequestProposalReading.YearFromDates(patch.Dates)));
    }
}
