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
        return ToResult(id, entity);
    }

    /// <summary>Reads an Entity after an operation that is already constrained to a known kind.</summary>
    internal static async Task<IResult> GetEntityAsync(
        Guid id,
        string expectedKind,
        bool hideNsfw,
        IEntityReadService entities,
        CancellationToken cancellationToken) {
        var entity = await entities.GetAsync(id, expectedKind, hideNsfw, cancellationToken);
        return ToResult(id, entity);
    }

    internal static IResult ToResult(Guid id, EntityCard? card) =>
        card is null
            ? Results.NotFound(new ApiProblem(ApiProblemCodes.EntityNotFound, $"Entity '{id}' was not found."))
            : Results.Ok(card);
}
