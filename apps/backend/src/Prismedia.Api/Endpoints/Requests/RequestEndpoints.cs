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
