using Prismedia.Api.Security;
using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

/// <summary>Administrative endpoints for independent instances of installed integration plugins.</summary>
public static class ConnectionEndpoints {
    /// <summary>Maps typed connection configuration and read-only verification routes.</summary>
    public static RouteGroupBuilder MapConnectionEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/connections").RequireAdmin().WithTags("Connections");
        group.AddEndpointFilter<ConnectionProblemFilter>();
        group.MapGet("/", async (ConnectionService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(cancellationToken)))
            .WithName("ListConnections").Produces<IReadOnlyList<ConnectionResponse>>();
        group.MapGet("/{id:guid}", async (Guid id, ConnectionService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetAsync(id, cancellationToken)))
            .WithName("GetConnection").Produces<ConnectionResponse>().Produces<ApiProblem>(404);
        group.MapPost("/", async (CreateConnectionRequest request, ConnectionService service, CancellationToken cancellationToken) => {
            var response = await service.CreateAsync(request, cancellationToken);
            return Results.Created($"/api/connections/{response.Id}", response);
        }).WithName("CreateConnection").Produces<ConnectionResponse>(201).Produces<ApiProblem>(400);
        group.MapPut("/{id:guid}", async (Guid id, UpdateConnectionRequest request, ConnectionService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateAsync(id, request, cancellationToken)))
            .WithName("UpdateConnection").Produces<ConnectionResponse>().Produces<ApiProblem>(400).Produces<ApiProblem>(409);
        group.MapPost("/{id:guid}/test", async (Guid id, ConnectionService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ProbeAsync(id, cancellationToken)))
            .WithName("TestConnection").Produces<ConnectionResponse>().Produces<ApiProblem>(409);
        group.MapDelete("/{id:guid}", async (Guid id, long expectedRevision, ConnectionService service, CancellationToken cancellationToken) => {
            await service.DeleteAsync(id, expectedRevision, cancellationToken);
            return Results.NoContent();
        }).WithName("DeleteConnection").Produces(204).Produces<ApiProblem>(409);
        group.MapPost("/{id:guid}/browse", async (Guid id, BrowseConnectionRequest request,
            CatalogDiscoveryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.BrowseAsync(id, request, cancellationToken)))
            .WithName("BrowseConnection").Produces<DiscoveryPageResponse>().Produces<ApiProblem>(400);
        return group;
    }

    private sealed class ConnectionProblemFilter : IEndpointFilter {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) {
            try { return await next(context); }
            catch (ConnectionNotFoundException error) { return Results.NotFound(new ApiProblem(ApiProblemCodes.ConnectionNotFound, error.Message)); }
            catch (ConnectionConflictException error) { return Results.Conflict(new ApiProblem(ApiProblemCodes.ConnectionConflict, error.Message)); }
            catch (ArgumentException error) { return Results.BadRequest(new ApiProblem(ApiProblemCodes.ConnectionInvalid, error.Message)); }
            catch (ConnectionSecretUnavailableException error) { return Results.BadRequest(new ApiProblem(ApiProblemCodes.ConnectionUnavailable, error.Message)); }
            catch (IntegrationInvocationException error) { return Results.BadRequest(new ApiProblem(ApiProblemCodes.ConnectionUnavailable, error.Message)); }
        }
    }
}
