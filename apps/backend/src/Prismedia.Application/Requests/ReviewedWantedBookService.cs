using Prismedia.Application.Entities;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>Saves one reviewed Open Library work without claiming either Book rendition's fulfillment.</summary>
public sealed class ReviewedWantedBookService(
    IWantedEntityWriter wanted,
    IWantedSuppressionStore suppressions,
    IPluginIdentityRouter routes,
    IEntityLifecycleMutationLease lifecycle) {
    /// <summary>Reuses the exact work identity and applies selected metadata without starting acquisition or monitoring.</summary>
    public async Task<PreparedWantedBookResponse> PrepareAsync(
        ReviewedRequestCommitRequest request,
        CancellationToken token) {
        var review = Validate(request);
        var workId = review.Proposal.Patch!.ExternalIds[ExternalIdProviders.OpenLibraryWork];
        var workIdentity = new ExternalIdentity(ExternalIdProviders.OpenLibraryWork, workId);
        var exactRoutes = (await routes.ResolveAsync(
                EntityKind.Book.ToCode(), IdentifyAction.LookupId, [review.ExternalIdentity], token))
            .Where(route => route.Identity == review.ExternalIdentity
                && string.Equals(route.PluginId, review.PluginId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (exactRoutes.Length != 1)
            throw new RequestCommitValidationException(
                "The exact metadata plugin route is unavailable. Review this Book again through an enabled plugin.");

        var title = review.Proposal.Patch.Title ?? review.ExternalIdentity.Value;
        if (string.IsNullOrWhiteSpace(title) || title.Length > 512)
            throw new RequestCommitValidationException("The reviewed Book needs a valid title.");
        var book = await wanted.EnsureAsync(EntityKind.Book, workIdentity, title.Trim(),
            parentEntityId: null, matchTitleKindWide: false, token);
        if (book.HasFile) return new(book.EntityId, title.Trim(), true);
        if (!await lifecycle.ExecuteAsync(book.EntityId, async ct => {
            await wanted.ApplyProposalWithDeferredArtworkAsync(book.EntityId, review.Proposal, ct);
            await wanted.BindProviderIdentityAsync(book.EntityId, exactRoutes[0], ct);
            await suppressions.ClearAsync([review.ExternalIdentity, workIdentity], ct);
        }, token)) throw new EntityLifecycleMutationConflictException(book.EntityId);
        return new(book.EntityId, title.Trim(), false);
    }

    /// <summary>Validates one reviewed Book without writing it, for connected-manager preflight.</summary>
    public static (string WorkId, string Title) ReviewWork(ReviewedRequestCommitRequest request) {
        var review = Validate(request);
        var workId = review.Proposal.Patch!.ExternalIds[ExternalIdProviders.OpenLibraryWork];
        var title = review.Proposal.Patch.Title ?? review.ExternalIdentity.Value;
        if (string.IsNullOrWhiteSpace(title) || title.Length > 512)
            throw new RequestCommitValidationException("The reviewed Book needs a valid title.");
        return (workId, title.Trim());
    }

    private static RequestReviewResponse Validate(ReviewedRequestCommitRequest request) {
        if (request.Kind is not (RequestMediaKind.Book or RequestMediaKind.Audiobook)
            || string.IsNullOrWhiteSpace(request.PluginId)
            || request.Review is not { EntityKind: EntityKind.Book, Proposal.TargetKind: EntityKind.Book }
            || request.Proposal is not { TargetKind: EntityKind.Book }
            || request.Review.Proposal.Patch is null
            || request.BookRenditions is not null
            || request.SelectedProposalIds is not { Count: 1 }
            || request.SelectedProposalIds[0] != request.Review.Proposal.ProposalId
            || request.ProposalRevision != request.Review.Revision
            || RequestProposalRevision.Compute(request.Review.Proposal) != request.Review.Revision
            || !request.Review.Proposal.Patch!.ExternalIds.TryGetValue(
                ExternalIdProviders.OpenLibraryWork, out var workId)
            || string.IsNullOrWhiteSpace(workId)
            || workId.Length > 128)
            throw new RequestCommitValidationException(
                "Submit one complete reviewed Book with an exact Open Library work identity.");
        var selected = ReviewedRequestProposalValidator.Validate(request, request.Review, request.Proposal);
        if (selected.Proposal.Patch?.ExternalIds.GetValueOrDefault(ExternalIdProviders.OpenLibraryWork) != workId)
            throw new RequestCommitValidationException("Keep the reviewed Open Library work identity when saving this Book.");
        return selected;
    }
}
