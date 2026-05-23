using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.System;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Api.Endpoints;

internal static class IdentifyBulkEndpoints {
    internal static RouteGroupBuilder MapIdentifyBulkEndpoints(this RouteGroupBuilder group) {
        group.MapPost("/bulk", (
            IdentifyBulkStartRequest request,
            bool? hideNsfw,
            HttpContext httpContext,
            IdentifySessionStore sessions,
            IServiceScopeFactory scopes,
            CancellationToken cancellationToken) => {
                if (request.EntityIds.Count == 0) {
                    return Results.BadRequest(new ApiProblem("empty_bulk_identify", "Bulk identify requires at least one entity."));
                }

                var session = sessions.Create(request.EntityIds, request.Provider);
                var shouldHideNsfw = NsfwVisibility.ShouldHide(hideNsfw, httpContext);
                _ = Task.Run(async () => {
                    using var scope = scopes.CreateScope();
                    var identify = scope.ServiceProvider.GetRequiredService<IdentifyPluginService>();
                    var results = new List<IdentifyBulkResult>();
                    foreach (var entityId in request.EntityIds) {
                        var response = await identify.IdentifyAsync(
                            entityId,
                            request.Provider,
                            request.Query,
                            shouldHideNsfw,
                            CancellationToken.None);
                        results.Add(new IdentifyBulkResult(entityId, response));
                    }

                    sessions.Complete(session.Id, results);
                }, cancellationToken);

                return Results.Accepted($"/api/identify/bulk/{session.Id}", session);
            })
            .WithName("StartBulkIdentify")
            .WithSummary("Starts a transient in-memory bulk identify review session.")
            .Produces<IdentifyBulkSession>(StatusCodes.Status202Accepted)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapGet("/bulk/{sessionId:guid}", (
            Guid sessionId,
            IdentifySessionStore sessions) => {
                var session = sessions.Get(sessionId);
                return session is null
                    ? Results.NotFound(new ApiProblem("identify_session_not_found", $"Identify session '{sessionId}' was not found."))
                    : Results.Ok(session);
            })
            .WithName("GetBulkIdentifySession")
            .WithSummary("Gets transient bulk identify session status and results.")
            .Produces<IdentifyBulkSession>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapDelete("/bulk/{sessionId:guid}", (
            Guid sessionId,
            IdentifySessionStore sessions) =>
            sessions.Close(sessionId)
                ? Results.NoContent()
                : Results.NotFound(new ApiProblem("identify_session_not_found", $"Identify session '{sessionId}' was not found.")))
            .WithName("CloseBulkIdentifySession")
            .WithSummary("Closes a transient bulk identify review session.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }
}
