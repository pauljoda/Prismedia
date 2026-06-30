using Prismedia.Application.Requests;
using Prismedia.Contracts.Requests;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Endpoints;

public static class RequestEndpoints {
    public static RouteGroupBuilder MapRequestEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/requests")
            .WithTags("Requests");

        group.MapGet("/services", (
            RequestServiceInstanceCommandService services,
            CancellationToken cancellationToken) =>
            services.ListAsync(cancellationToken))
            .WithName("ListRequestServices")
            .WithSummary("Lists configured request service instances with credentials redacted.")
            .Produces<IReadOnlyList<RequestServiceInstanceSummary>>();

        group.MapPost("/services", async (
            RequestServiceInstanceSaveRequest request,
            RequestServiceInstanceCommandService services,
            CancellationToken cancellationToken) =>
            await SaveRequestServiceAsync(request, services, cancellationToken))
            .WithName("SaveRequestService")
            .WithSummary("Creates or updates a request service instance.")
            .Produces<RequestServiceInstanceSummary>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapPut("/services/{id:guid}", async (
            Guid id,
            RequestServiceInstanceSaveRequest request,
            RequestServiceInstanceCommandService services,
            CancellationToken cancellationToken) =>
            await SaveRequestServiceAsync(request with { Id = id }, services, cancellationToken))
            .WithName("UpdateRequestService")
            .WithSummary("Updates an existing request service instance.")
            .Produces<RequestServiceInstanceSummary>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapDelete("/services/{id:guid}", async (
            Guid id,
            RequestServiceInstanceCommandService services,
            CancellationToken cancellationToken) =>
            await services.DeleteAsync(id, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound(new ApiProblem(ApiProblemCodes.NotFound, "Request service instance was not found.")))
            .WithName("DeleteRequestService")
            .WithSummary("Deletes a configured request service instance.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("/services/test", async (
            RequestServiceTestRequest request,
            RequestServiceTestService tester,
            CancellationToken cancellationToken) =>
            ValidateBaseUrl(request.BaseUrl) is { } problem
                ? Results.BadRequest(problem)
                : Results.Ok(await tester.TestAsync(request, cancellationToken)))
            .WithName("TestRequestService")
            .WithSummary("Tests connectivity for a request service configuration and returns its selectable options on success. A successful test gates saving the service.")
            .Produces<RequestServiceTestResponse>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

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
            .WithSummary("Searches configured request providers for requestable external media. Adults-only certifications are filtered out when hideNsfw is set.")
            .Produces<RequestSearchResponse>();

        group.MapGet("/history", (
            RequestHistoryService history,
            CancellationToken cancellationToken) =>
            history.ListAsync(cancellationToken))
            .WithName("ListRequestHistory")
            .WithSummary("Lists submitted request history with statuses refreshed live from each upstream service.")
            .Produces<RequestHistoryResponse>();

        group.MapDelete("/history/{id:guid}", async (
            Guid id,
            RequestHistoryService history,
            CancellationToken cancellationToken) =>
            await history.DeleteAsync(id, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound(new ApiProblem(ApiProblemCodes.NotFound, "Request history entry was not found.")))
            .WithName("DeleteRequestHistoryEntry")
            .WithSummary("Deletes a request history entry.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

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
            .WithSummary("Gets rich detail metadata for a requestable external item.")
            .Produces<RequestDetailResponse>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("/", async (
            RequestSubmitRequest request,
            RequestSubmitService submit,
            CancellationToken cancellationToken) => {
                var response = await submit.SubmitAsync(request, cancellationToken);
                return response is null
                    ? Results.NotFound(new ApiProblem(ApiProblemCodes.NotFound, "Request service instance was not found."))
                    : Results.Ok(response);
            })
            .WithName("SubmitRequest")
            .WithSummary("Submits a media request to the selected upstream service instance.")
            .Produces<RequestSubmitResponse>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<IResult> SaveRequestServiceAsync(
        RequestServiceInstanceSaveRequest request,
        RequestServiceInstanceCommandService services,
        CancellationToken cancellationToken) {
        try {
            return Results.Ok(await services.SaveAsync(request, cancellationToken));
        } catch (RequestServiceConfigurationException ex) {
            return Results.BadRequest(new ApiProblem(ex.Code, ex.Message));
        }
    }

    private static ApiProblem? ValidateBaseUrl(string value) {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var baseUrl) ||
            (baseUrl.Scheme != Uri.UriSchemeHttp && baseUrl.Scheme != Uri.UriSchemeHttps)) {
            return new ApiProblem(ApiProblemCodes.RequestServiceInvalid, "The base URL must be an absolute http or https URL.");
        }

        return null;
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
