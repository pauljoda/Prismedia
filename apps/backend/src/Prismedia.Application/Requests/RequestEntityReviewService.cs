using Prismedia.Application.Entities;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>
/// Resolves an existing entity's canonical identities to capable plugins and returns the first review
/// proposal produced by the router's deterministic route order.
/// </summary>
public sealed class RequestEntityReviewService(
    IEntityExternalIdentityStore externalIdentities,
    IPluginIdentityRouter identityRouter,
    IPluginRequestReviewSource reviews) {
    /// <summary>
    /// Reviews an existing entity without parsing or constructing provider-qualified string ids.
    /// </summary>
    /// <param name="request">Local entity and request-flow kind to review.</param>
    /// <param name="hideNsfw">Whether NSFW plugins and content must remain hidden.</param>
    /// <param name="cancellationToken">Token used to cancel identity, routing, and plugin work.</param>
    /// <returns>
    /// The first proposal produced by a capable deterministic route, or null when the kind is unknown,
    /// the entity has no identities, no plugin can route them, or every route returns no match.
    /// </returns>
    public async Task<RequestReviewResponse?> ReviewAsync(
        RequestEntityReviewRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var descriptor = RequestKindRegistry.Find(request.Kind);
        if (descriptor is null) {
            return null;
        }

        var identities = (await externalIdentities.ListAsync(request.EntityId, cancellationToken))
            .Select(association => association.Identity)
            .Distinct()
            .ToArray();
        if (identities.Length == 0) {
            return null;
        }

        var routes = await identityRouter.ResolveAsync(
            descriptor.PluginKindCode,
            IdentifyAction.LookupId,
            identities,
            cancellationToken);
        foreach (var route in routes) {
            var review = await reviews.ReviewAsync(
                new RequestReviewRequest(request.Kind, route.PluginId, route.Identity),
                hideNsfw,
                cancellationToken);
            if (review is not null) {
                return review;
            }
        }

        return null;
    }
}
