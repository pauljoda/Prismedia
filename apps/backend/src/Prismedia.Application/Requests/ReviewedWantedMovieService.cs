using System.Globalization;
using Prismedia.Application.Entities;
using Prismedia.Application.Integrations;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>Saves a reviewed movie as a wanted library item before a separate external-fulfillment decision.</summary>
public sealed class ReviewedWantedMovieService(IWantedEntityWriter wanted, IWantedSuppressionStore suppressions,
    IPluginIdentityRouter routes, IEntityLifecycleMutationLease lifecycle) : IManagedWantedWorkPreparer {
    /// <inheritdoc />
    public RequestMediaKind Kind => RequestMediaKind.Movie;

    /// <inheritdoc />
    public async Task<ManagedWantedWork> PrepareForManagerAsync(ReviewedRequestCommitRequest request, bool managerOrigin,
        CancellationToken token) {
        var prepared = managerOrigin
            ? await PrepareFromManagerAsync(request, request.Review!, token)
            : await PrepareAsync(request, token);
        return new(prepared.EntityId, MissingTargetEntityIds: null, prepared.HasFile);
    }

    /// <summary>Reuses exact movie identity, applies reviewed metadata, and never creates an acquisition, monitor, or manager action.</summary>
    public async Task<PreparedWantedMovieResponse> PrepareAsync(ReviewedRequestCommitRequest request, CancellationToken token) {
        var review = Validate(request);
        var exactRoutes = (await routes.ResolveAsync(EntityKind.Movie.ToCode(), IdentifyAction.LookupId, [review.ExternalIdentity], token))
            .Where(route => route.Identity == review.ExternalIdentity && string.Equals(route.PluginId, review.PluginId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exactRoutes.Length != 1) throw Invalid("The exact metadata plugin route is unavailable. Review this movie again through an enabled plugin.");
        return await MaterializeAsync(request, review, exactRoutes[0], token);
    }

    /// <summary>Saves a movie from a fresh connection-scoped manager review without claiming a metadata-plugin binding.</summary>
    public async Task<PreparedWantedMovieResponse> PrepareFromManagerAsync(
        ReviewedRequestCommitRequest request,
        RequestReviewResponse canonicalReview,
        CancellationToken token) {
        request = request with { Review = canonicalReview };
        var review = Validate(request);
        return await MaterializeAsync(request, review, route: null, token);
    }

    /// <summary>Validates a complete movie review and derives manager lookup evidence without writing.</summary>
    public async Task<ReviewedWantedPlan> ReviewForManagerAsync(
        ReviewedRequestCommitRequest request,
        bool managerOrigin,
        CancellationToken token) {
        var review = Validate(request);
        var selection = ReviewedRequestSelectionResolver.Resolve(
            RequestKindRegistry.Find(RequestMediaKind.Movie)!,
            review,
            request.SelectedProposalIds,
            null);
        if (!selection.SelectRoot || selection.Nodes.Count != 0) throw Invalid("Select the reviewed movie itself.");
        if (!managerOrigin) {
            var exactRoutes = (await routes.ResolveAsync(
                    EntityKind.Movie.ToCode(),
                    IdentifyAction.LookupId,
                    [review.ExternalIdentity],
                    token))
                .Where(route => route.Identity == review.ExternalIdentity
                    && string.Equals(route.PluginId, review.PluginId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (exactRoutes.Length != 1)
                throw Invalid("The exact metadata plugin route is unavailable. Review this movie again through an enabled plugin.");
        }
        var title = review.Proposal.Patch.Title ?? review.ExternalIdentity.Value;
        if (string.IsNullOrWhiteSpace(title) || title.Length > 512) throw Invalid("The reviewed movie needs a valid title.");
        var provider = ManagedFulfillmentPolicy.For(EntityKind.Movie).IdentityProviders[0];
        return new(title.Trim(), new(EntityKind.Movie,
            new Dictionary<string, string> { [provider] = review.ExternalIdentity.Value }));
    }

    private async Task<PreparedWantedMovieResponse> MaterializeAsync(
        ReviewedRequestCommitRequest request,
        RequestReviewResponse review,
        PluginIdentityRoute? route,
        CancellationToken token) {
        var selection = ReviewedRequestSelectionResolver.Resolve(RequestKindRegistry.Find(RequestMediaKind.Movie)!, review, request.SelectedProposalIds, null);
        if (!selection.SelectRoot || selection.Nodes.Count != 0) throw Invalid("Select the reviewed movie itself.");
        var title = review.Proposal.Patch.Title ?? request.Review!.Proposal.Patch.Title ?? review.ExternalIdentity.Value;
        if (string.IsNullOrWhiteSpace(title) || title.Length > 512) throw Invalid("The reviewed movie needs a valid title.");
        var entity = await wanted.EnsureAsync(EntityKind.Movie, review.ExternalIdentity, title, null, false, token);
        if (entity.HasFile) return new(entity.EntityId, title, true);
        if (!await lifecycle.ExecuteAsync(entity.EntityId, async ct => {
            if (route is not null && !await wanted.BindProviderIdentityAsync(entity.EntityId, route, ct))
                throw Invalid("The selected metadata route changed before saving. Review the movie again.");
            await wanted.ApplyProposalWithDeferredArtworkAsync(entity.EntityId, review.Proposal, ct);
            await suppressions.ClearAsync([review.ExternalIdentity], ct);
        }, token)) throw new EntityLifecycleMutationConflictException(entity.EntityId);
        return new(entity.EntityId, title, false);
    }

    private static RequestReviewResponse Validate(ReviewedRequestCommitRequest request) {
        if (request.Kind != RequestMediaKind.Movie || request.RootExternalIdentity?.Namespace != ExternalIdProviders.Tmdb
            || !int.TryParse(request.RootExternalIdentity.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0
            || id.ToString(CultureInfo.InvariantCulture) != request.RootExternalIdentity.Value
            || string.IsNullOrWhiteSpace(request.PluginId) || request.Review is not { EntityKind: EntityKind.Movie, Proposal.TargetKind: EntityKind.Movie }
            || request.Proposal is not { TargetKind: EntityKind.Movie } || request.SelectedProposalIds is null
            || request.SelectedProposalIds.Count != 1 || request.SelectedProposalIds[0] != request.Review.Proposal.ProposalId
            || request.Review.Proposal.Patch.ExternalIds.GetValueOrDefault(ExternalIdProviders.Tmdb) != request.RootExternalIdentity.Value
            || request.ProposalRevision != request.Review.Revision || RequestProposalRevision.Compute(request.Review.Proposal) != request.Review.Revision)
            throw Invalid("Submit the complete reviewed movie and its exact TMDB identity before choosing external fulfillment.");
        return ReviewedRequestProposalValidator.Validate(request, request.Review!, request.Proposal!);
    }
    private static RequestCommitValidationException Invalid(string message) => new(message);
}
