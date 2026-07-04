using Prismedia.Application.Acquisition;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Requests;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Endpoints;

public static class RequestEndpoints {
    public static RouteGroupBuilder MapRequestEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/requests")
            .WithTags("Requests");

        group.MapGet("/search", (
            string query,
            string[]? kinds,
            string[]? sources,
            bool? hideNsfw,
            RequestSearchService search,
            CancellationToken cancellationToken) =>
            search.SearchAsync(new RequestSearchRequest(
                query,
                DecodeMany<RequestMediaKind>(kinds),
                DecodeMany<RequestProviderKind>(sources),
                hideNsfw ?? false),
                cancellationToken))
            .WithName("SearchRequests")
            .WithSummary("Searches Prismedia's plugin metadata providers for requestable books and authors. Adults-only results are filtered out when hideNsfw is set.")
            .Produces<RequestSearchResponse>();

        group.MapGet("/details/{source}/{kind}/{externalId}", async (
            string source,
            string kind,
            string externalId,
            Guid? serviceId,
            bool? hideNsfw,
            HttpContext httpContext,
            RequestDetailService details,
            CancellationToken cancellationToken) => {
                try {
                    var detail = await details.GetAsync(
                        source.DecodeAs<RequestProviderKind>(),
                        kind.DecodeAs<RequestMediaKind>(),
                        externalId,
                        serviceId,
                        NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                        cancellationToken);
                    return detail is null
                        ? Results.NotFound(new ApiProblem(ApiProblemCodes.NotFound, "Request detail was not found."))
                        : Results.Ok(detail);
                } catch (InvalidOperationException ex) {
                    // Provider lookups throw when the external id resolves to nothing upstream.
                    return Results.NotFound(new ApiProblem(ApiProblemCodes.NotFound, ex.Message));
                }
            })
            .WithName("GetRequestDetail")
            .WithSummary("Gets rich detail metadata for a requestable external item, including its selectable child works.")
            .Produces<RequestDetailResponse>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("/commit", async (
            RequestCommitRequest request,
            bool? hideNsfw,
            HttpContext httpContext,
            RequestCommitService commits,
            CancellationToken cancellationToken) => {
                if (string.IsNullOrWhiteSpace(request.ExternalId)) {
                    return Results.BadRequest(new ApiProblem(ApiProblemCodes.RequestInvalid, "A provider-qualified external id is required."));
                }

                var descriptor = RequestKindRegistry.Find(request.Kind);
                if (descriptor is null || !descriptor.Committable) {
                    return Results.BadRequest(new ApiProblem(ApiProblemCodes.RequestInvalid, "This kind can't be requested yet."));
                }

                // A container commit needs either an explicit child selection or a monitoring preset that
                // derives one (Future/None legitimately select nothing now — the container watch handles
                // the rest). Only an empty selection with no preset is a mistake to reject.
                if (descriptor.IsContainer && request.SelectedChildIds.Count == 0 && request.Preset is null) {
                    return Results.BadRequest(new ApiProblem(ApiProblemCodes.RequestInvalid, "Select at least one item to request, or choose a monitoring preset."));
                }

                var response = await commits.CommitAsync(request, NsfwVisibility.ShouldHide(hideNsfw, httpContext), cancellationToken);
                return response is null
                    ? Results.NotFound(new ApiProblem(ApiProblemCodes.NotFound, "The requested item could not be resolved from its provider."))
                    : Results.Ok(response);
            })
            .WithName("CommitRequest")
            .WithSummary("Commits a reviewed request: creates the wanted library entities for the picked items up front and starts one acquisition per requested book.")
            .Produces<RequestCommitResponse>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("/commit-entity", async (
            RequestEntityCommitRequest request,
            bool? hideNsfw,
            HttpContext httpContext,
            RequestCommitService commits,
            CancellationToken cancellationToken) => {
                var response = await commits.RequestEntityAsync(
                    request.EntityId,
                    NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                    cancellationToken,
                    new Prismedia.Application.Acquisition.AcquisitionTargeting(request.TargetLibraryRootId, request.ProfileId));
                return response is null
                    ? Results.NotFound(new ApiProblem(ApiProblemCodes.NotFound, "The entity could not be requested — it may be gone, not a requestable kind, or unresolvable from its providers."))
                    : Results.Ok(response);
            })
            .WithName("CommitEntityRequest")
            .WithSummary("Requests an existing library entity (a wanted placeholder's Search-for-release): the server resolves its provider identity and starts the auto-grabbing acquisition.")
            .Produces<RequestCommitResponse>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("/remove-wanted", async (
            WantedRemovalRequest request,
            RequestCommitService commits,
            CancellationToken cancellationToken) => {
                if (request.EntityIds.Count == 0) {
                    return Results.BadRequest(new ApiProblem(ApiProblemCodes.RequestInvalid, "Select at least one wanted item to remove."));
                }

                var removed = await commits.RemoveWantedAsync(request.EntityIds, cancellationToken);
                return Results.Ok(new WantedRemovalResponse(removed));
            })
            .WithName("RemoveWanted")
            .WithSummary("Removes wanted placeholders: deletes each (tearing down in-flight downloads) and blacklists it from discovery; requesting it again later clears the blacklist entry.")
            .Produces<WantedRemovalResponse>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapPost("/sync-container", async (
            RequestEntityCommitRequest request,
            RequestCommitService commits,
            MonitorService monitors,
            CancellationToken cancellationToken) => {
                // The manual counterpart to the daily sweep for one container: discover new works now.
                var synced = await commits.SyncContainerAsync(request.EntityId, cancellationToken);
                if (!synced) {
                    return Results.NotFound(new ApiProblem(ApiProblemCodes.NotFound, "The container could not be synced — it may be gone, not a followable kind, or unresolvable from its providers."));
                }

                await monitors.MarkEntitySearchedAsync(request.EntityId, cancellationToken);
                return Results.NoContent();
            })
            .WithName("SyncContainerRequest")
            .WithSummary("Immediately re-syncs a followed author/artist from its provider, surfacing newly discovered works as wanted placeholders.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }

    private static IReadOnlyList<TEnum> DecodeMany<TEnum>(IReadOnlyList<string>? values)
        where TEnum : struct, Enum =>
        values is null
            ? []
            : values
                .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(value => value.DecodeAs<TEnum>())
                .ToArray();
}
