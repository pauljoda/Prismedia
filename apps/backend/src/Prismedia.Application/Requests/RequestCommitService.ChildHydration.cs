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

/// <summary>
/// Starts direct acquisitions for still-wanted structural children after a whole-unit search proves
/// barren or an imported unit remains incomplete.
/// </summary>
public interface IMissingChildAcquisitionRequester {
    /// <summary>Requests each still-wanted direct child and reports how many gaps are covered.</summary>
    Task<(int Covered, int Missing)> RequestMissingChildrenAsync(
        Guid entityId,
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

    /// <summary>
    /// Requests every still-wanted descendant phantom under an entity — the structural-unit completeness
    /// fallback. Each gap enters the ordinary monitored, auto-grabbing request pipeline. Existing barren
    /// pre-download acquisitions are refreshed, while active transfers and manual import holds count as
    /// covered without duplication. Owned container children are recursed into so a partially complete
    /// subtree reaches all of its wanted descendants.
    /// </summary>
    public async Task<(int Covered, int Missing)> RequestMissingChildrenAsync(
        Guid entityId,
        CancellationToken cancellationToken) {
        var entity = await wanted.GetEntityAsync(entityId, cancellationToken);
        if (entity is null) {
            return (0, 0);
        }

        var descriptor = RequestKindRegistry.All.FirstOrDefault(candidate =>
            candidate is { Committable: true } && candidate.WantedEntityKind == entity.Kind);
        var child = descriptor is null ? null : RequestKindRegistry.ChildOf(descriptor);
        if (child is not { Committable: true }) {
            return (0, 0);
        }

        var missing = await wanted.ListWantedChildIdsAsync(entityId, child.WantedEntityKind, cancellationToken);
        if (missing.Count == 0 && descriptor!.MaterializeChildPhantoms) {
            await HydrateAsync(entityId, hideNsfw: true, cancellationToken);
            missing = await wanted.ListWantedChildIdsAsync(entityId, child.WantedEntityKind, cancellationToken);
        }

        var covered = 0;
        foreach (var childId in missing) {
            if (await acquisitions.EnsureOpenEntitySearchAsync(
                    childId,
                    child.BookRendition,
                    cancellationToken)) {
                covered++;
                continue;
            }

            var response = await RequestEntityAsync(childId, hideNsfw: true, cancellationToken);
            if (response is { Items.Count: > 0 }) {
                covered++;
            }
        }

        var total = missing.Count;
        if (RequestKindRegistry.ChildOf(child) is not { Committable: true }) {
            return (covered, total);
        }

        var wantedSet = missing.ToHashSet();
        foreach (var ownedChildId in await wanted.ListChildIdsAsync(
                     entityId,
                     child.WantedEntityKind,
                     cancellationToken)) {
            if (wantedSet.Contains(ownedChildId)) {
                continue;
            }

            var (subCovered, subMissing) = await RequestMissingChildrenAsync(ownedChildId, cancellationToken);
            covered += subCovered;
            total += subMissing;
        }

        return (covered, total);
    }
}
