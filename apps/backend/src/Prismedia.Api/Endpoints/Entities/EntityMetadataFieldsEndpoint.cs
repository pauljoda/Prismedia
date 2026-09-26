using Prismedia.Api.Security;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Endpoints;

internal static class EntityMetadataFieldsEndpoint {
    internal static RouteGroupBuilder MapEntityMetadataFieldsEndpoint(this RouteGroupBuilder group) {
        group.MapGet("/{id:guid}/metadata-fields", async (Guid id, IMetadataFieldService service, CancellationToken cancellationToken) =>
            await service.ReadAsync(id, cancellationToken) is { } fields ? Results.Ok(fields) : Missing())
            .RequireAdmin().WithName("GetEntityMetadataFields").Produces<IReadOnlyList<MetadataFieldResponse>>().Produces<ApiProblem>(404);
        group.MapPut("/{id:guid}/metadata-fields/{field}", async (Guid id, string field, UpdateMetadataFieldLockRequest request,
            IMetadataFieldService service, CancellationToken cancellationToken) => {
                try {
                    if (!field.TryDecodeAs<MetadataPatchField>(out var selected)) throw new ArgumentException("Unknown metadata field.");
                    return await service.SetLockAsync(id, selected, request, cancellationToken) is { } result ? Results.Ok(result) : Missing();
                } catch (ArgumentException error) { return Results.BadRequest(new ApiProblem(ApiProblemCodes.InvalidEntityMetadataPatch, error.Message)); }
                catch (MetadataFieldConflictException error) { return Results.Conflict(new ApiProblem(ApiProblemCodes.MetadataFieldConflict, error.Message)); }
            }).RequireAdmin().WithName("SetEntityMetadataFieldLock").Produces<MetadataFieldResponse>().Produces<ApiProblem>(400).Produces<ApiProblem>(404).Produces<ApiProblem>(409);
        return group;
    }
    private static IResult Missing() => Results.NotFound(new ApiProblem(ApiProblemCodes.EntityNotFound, "The entity is unavailable for metadata protection."));
}
