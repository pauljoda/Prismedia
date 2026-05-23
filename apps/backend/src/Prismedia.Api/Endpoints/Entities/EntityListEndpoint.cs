using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Endpoints;

internal static class EntityListEndpoint {
    internal static RouteGroupBuilder MapEntityListEndpoint(this RouteGroupBuilder group) {
        group.MapGet("/", async (
            string? query,
            string? cursor,
            bool? hideNsfw,
            int? limit,
            HttpContext httpContext,
            IEntityReadService entities,
            CancellationToken cancellationToken) => {
                if (!TryGetKind(httpContext.Request.Query["kind"].ToString(), out var kind, out var error)) {
                    return error;
                }

                return Results.Ok(await entities.ListAsync(
                    kind,
                    query,
                    cursor,
                    NsfwVisibility.ShouldHide(hideNsfw, httpContext),
                    limit,
                    cancellationToken));
            })
            .WithName("ListEntities")
            .Produces<EntityListResponse>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        return group;
    }

    private static bool TryGetKind(string? value, out string? kind, out IResult error) {
        kind = null;
        error = Results.Empty;
        if (string.IsNullOrWhiteSpace(value)) {
            return true;
        }

        if (EntityKindRegistry.TryGet(value, out var resolved)) {
            kind = resolved.ToCode();
            return true;
        }

        error = Results.BadRequest(new ApiProblem("invalid_entity_kind", $"Entity kind '{value}' is not recognized."));
        return false;
    }
}
