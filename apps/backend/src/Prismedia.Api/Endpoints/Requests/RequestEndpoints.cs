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

                if (descriptor.IsContainer && request.SelectedChildIds.Count == 0) {
                    return Results.BadRequest(new ApiProblem(ApiProblemCodes.RequestInvalid, "Select at least one item to request."));
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
