using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

/// <summary>The single write route for reading and listening progress.</summary>
internal static class EntityProgressEndpoint {
    #region Actions - Routes

    /// <summary>Maps <c>PATCH /{id}/progress</c> on the Entity route group.</summary>
    internal static RouteGroupBuilder MapEntityProgressEndpoint(this RouteGroupBuilder group) {
        group.MapPatch("/{id:guid}/progress", async (
            Guid id,
            EntityProgressUpdateRequest request,
            HttpRequest httpRequest,
            EntityCapabilityService capabilities,
            CancellationToken cancellationToken) => {
                var result = await capabilities.ReportProgressAsync(
                    id,
                    new EntityProgressReport(
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
                        request.Modality,
                        request.Listening),
                    cancellationToken);
                if (result.Status == EntityProgressReportStatus.Invalid) {
                    return Results.BadRequest(new ApiProblem(
                        ApiProblemCodes.InvalidProgress,
                        result.Error ?? "The progress report is invalid."));
                }
                if (result.OwnerId is not { } ownerId) {
                    return EntityEndpointResults.ToResult(id, card: null);
                }

                return PrefersMinimalResponse(httpRequest)
                    ? Results.NoContent()
                    : EntityEndpointResults.ToResult(id, await capabilities.ReadProgressOwnerAsync(ownerId, cancellationToken));
            })
            .WithName("UpdateEntityProgress")
            .WithSummary("Update Entity Progress.")
            .Produces<EntityCard>()
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }

    private static bool PrefersMinimalResponse(HttpRequest request) =>
        request.Headers.TryGetValue("Prefer", out var values) &&
        values
            .SelectMany(value => value?.Split(',', StringSplitOptions.TrimEntries) ?? [])
            .Any(value => value.Equals("return=minimal", StringComparison.OrdinalIgnoreCase));

    #endregion
}
