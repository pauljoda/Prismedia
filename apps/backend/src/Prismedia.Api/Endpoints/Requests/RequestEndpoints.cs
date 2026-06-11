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
            IRequestServiceInstanceStore store,
            CancellationToken cancellationToken) =>
            store.ListAsync(cancellationToken))
            .WithName("ListRequestServices")
            .WithSummary("Lists configured request service instances with credentials redacted.")
            .Produces<IReadOnlyList<RequestServiceInstanceSummary>>();

        group.MapPost("/services", async (
            RequestServiceInstanceSaveRequest request,
            IRequestServiceInstanceStore store,
            CancellationToken cancellationToken) =>
            ValidateSaveRequest(request) is { } problem
                ? Results.BadRequest(problem)
                : Results.Ok(await store.SaveAsync(request, cancellationToken)))
            .WithName("SaveRequestService")
            .WithSummary("Creates or updates a request service instance.")
            .Produces<RequestServiceInstanceSummary>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapPut("/services/{id:guid}", async (
            Guid id,
            RequestServiceInstanceSaveRequest request,
            IRequestServiceInstanceStore store,
            CancellationToken cancellationToken) =>
            ValidateSaveRequest(request) is { } problem
                ? Results.BadRequest(problem)
                : Results.Ok(await store.SaveAsync(request with { Id = id }, cancellationToken)))
            .WithName("UpdateRequestService")
            .WithSummary("Updates an existing request service instance.")
            .Produces<RequestServiceInstanceSummary>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapDelete("/services/{id:guid}", async (
            Guid id,
            IRequestServiceInstanceStore store,
            CancellationToken cancellationToken) =>
            await store.DeleteAsync(id, cancellationToken)
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
            RequestSearchService search,
            CancellationToken cancellationToken) =>
            search.SearchAsync(new RequestSearchRequest(
                query,
                DecodeMany<RequestMediaKind>(kinds),
                DecodeMany<RequestProviderKind>(sources)),
                cancellationToken))
            .WithName("SearchRequests")
            .WithSummary("Searches configured request providers for requestable external media.")
            .Produces<RequestSearchResponse>();

        group.MapGet("/details/{source}/{kind}/{externalId}", async (
            string source,
            string kind,
            string externalId,
            Guid? serviceId,
            RequestDetailService details,
            CancellationToken cancellationToken) => {
                var detail = await details.GetAsync(
                    source.DecodeAs<RequestProviderKind>(),
                    kind.DecodeAs<RequestMediaKind>(),
                    externalId,
                    serviceId,
                    cancellationToken);
                return detail is null
                    ? Results.NotFound(new ApiProblem(ApiProblemCodes.NotFound, "Request detail was not found."))
                    : Results.Ok(detail);
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

    private static ApiProblem? ValidateSaveRequest(RequestServiceInstanceSaveRequest request) {
        if (string.IsNullOrWhiteSpace(request.DisplayName)) {
            return new ApiProblem(ApiProblemCodes.RequestServiceInvalid, "A display name is required.");
        }

        return ValidateBaseUrl(request.BaseUrl);
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
