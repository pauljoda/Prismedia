using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

internal static class EntityProgressEndpoint {
    internal static RouteGroupBuilder MapEntityProgressEndpoint(this RouteGroupBuilder group) {
        group.MapPatch("/{id:guid}/progress", async (
            Guid id,
            EntityProgressUpdateRequest request,
            HttpRequest httpRequest,
            EntityCapabilityService capabilities,
            CancellationToken cancellationToken) => {
                if (PrefersMinimalResponse(httpRequest)) {
                    var updated = await capabilities.UpdateProgressWithoutProjectionAsync(
                        id,
                        request.CurrentEntityId,
                        request.Unit,
                        request.Index,
                        request.Total,
                        request.Mode,
                        request.Completed,
                        request.Reset,
                        request.Location,
                        request.ActivitySeconds,
                        request.ActivityKind,
                        request.UtcOffsetMinutes,
                        cancellationToken);
                    return updated
                        ? Results.NoContent()
                        : EntityEndpointResults.ToResult(id, card: null);
                }

                return EntityEndpointResults.ToResult(id, await capabilities.UpdateProgressAsync(
                    id,
                    request.CurrentEntityId,
                    request.Unit,
                    request.Index,
                    request.Total,
                    request.Mode,
                    request.Completed,
                    request.Reset,
                    request.Location,
                    request.ActivitySeconds,
                    request.ActivityKind,
                    request.UtcOffsetMinutes,
                    cancellationToken));
            })
            .WithName("UpdateEntityProgress")
            .WithSummary("Update Entity Progress.")
            .Produces<EntityCard>()
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }

    private static bool PrefersMinimalResponse(HttpRequest request) =>
        request.Headers.TryGetValue("Prefer", out var values) &&
        values
            .SelectMany(value => value?.Split(',', StringSplitOptions.TrimEntries) ?? [])
            .Any(value => value.Equals("return=minimal", StringComparison.OrdinalIgnoreCase));
}
