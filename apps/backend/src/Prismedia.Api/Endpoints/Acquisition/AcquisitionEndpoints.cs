using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

public static class AcquisitionEndpoints {
    public static RouteGroupBuilder MapAcquisitionEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/acquisitions")
            .WithTags("Acquisitions");

        group.MapGet("/indexers", (
            IndexerConfigCommandService indexers,
            CancellationToken cancellationToken) =>
            indexers.ListAsync(cancellationToken))
            .WithName("ListIndexers")
            .WithSummary("Lists configured indexers with API keys redacted.")
            .Produces<IReadOnlyList<IndexerConfigSummary>>();

        group.MapPost("/indexers", async (
            IndexerConfigSaveRequest request,
            IndexerConfigCommandService indexers,
            CancellationToken cancellationToken) =>
            await SaveIndexerAsync(request, indexers, cancellationToken))
            .WithName("SaveIndexer")
            .WithSummary("Creates or updates an indexer configuration.")
            .Produces<IndexerConfigSummary>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapPut("/indexers/{id:guid}", async (
            Guid id,
            IndexerConfigSaveRequest request,
            IndexerConfigCommandService indexers,
            CancellationToken cancellationToken) =>
            await SaveIndexerAsync(request with { Id = id }, indexers, cancellationToken))
            .WithName("UpdateIndexer")
            .WithSummary("Updates an existing indexer configuration.")
            .Produces<IndexerConfigSummary>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapDelete("/indexers/{id:guid}", async (
            Guid id,
            IndexerConfigCommandService indexers,
            CancellationToken cancellationToken) =>
            await indexers.DeleteAsync(id, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound(new ApiProblem(ApiProblemCodes.NotFound, "Indexer was not found.")))
            .WithName("DeleteIndexer")
            .WithSummary("Deletes a configured indexer.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("/indexers/test", async (
            IndexerTestRequest request,
            IndexerConfigCommandService indexers,
            CancellationToken cancellationToken) => {
                try {
                    return Results.Ok(await indexers.TestAsync(request, cancellationToken));
                } catch (AcquisitionConfigurationException ex) {
                    return Results.BadRequest(new ApiProblem(ex.Code, ex.Message));
                }
            })
            .WithName("TestIndexer")
            .WithSummary("Tests connectivity for an indexer configuration.")
            .Produces<IndexerTestResponse>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapPost("/search", async (
            AcquisitionCreateRequest request,
            AcquisitionService acquisitions,
            CancellationToken cancellationToken) =>
            Results.Ok(await acquisitions.CreateAndSearchAsync(request, cancellationToken)))
            .WithName("CreateAcquisition")
            .WithSummary("Creates an acquisition and starts a background indexer search; poll the acquisition for scored candidates.")
            .Produces<AcquisitionSummary>();

        group.MapGet("/", (
            AcquisitionService acquisitions,
            CancellationToken cancellationToken) =>
            acquisitions.ListAsync(cancellationToken))
            .WithName("ListAcquisitions")
            .WithSummary("Lists acquisitions with their current status.")
            .Produces<IReadOnlyList<AcquisitionSummary>>();

        group.MapGet("/{id:guid}", async (
            Guid id,
            AcquisitionService acquisitions,
            CancellationToken cancellationToken) => {
                var detail = await acquisitions.GetAsync(id, cancellationToken);
                return detail is null
                    ? Results.NotFound(new ApiProblem(ApiProblemCodes.AcquisitionNotFound, "Acquisition was not found."))
                    : Results.Ok(detail);
            })
            .WithName("GetAcquisition")
            .WithSummary("Gets an acquisition with its scored release candidates for review.")
            .Produces<AcquisitionDetail>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<IResult> SaveIndexerAsync(
        IndexerConfigSaveRequest request,
        IndexerConfigCommandService indexers,
        CancellationToken cancellationToken) {
        try {
            return Results.Ok(await indexers.SaveAsync(request, cancellationToken));
        } catch (AcquisitionConfigurationException ex) {
            return Results.BadRequest(new ApiProblem(ex.Code, ex.Message));
        }
    }
}
