using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

internal static class EntityEndpointResults {
    internal static async Task<IResult> GetEntityAsync(
        Guid id,
        bool hideNsfw,
        IEntityReadService entities,
        CancellationToken cancellationToken) {
        var entity = await entities.GetAsync(id, hideNsfw, cancellationToken);
        return entity is null
            ? Results.NotFound(new ApiProblem("entity_not_found", $"Entity '{id}' was not found."))
            : Results.Ok(entity);
    }

    internal static IResult ToResult(Guid id, EntityCard? card) =>
        card is null
            ? Results.NotFound(new ApiProblem("entity_not_found", $"Entity '{id}' was not found."))
            : Results.Ok(card);
}
