using Prismedia.Api.Security;
using Prismedia.Application.Security;
using Prismedia.Contracts.Security;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

public static class SecurityEndpoints {
    public static RouteGroupBuilder MapSecurityEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/security")
            .WithTags("Security");

        group.MapGet("/api-key", async (
            PrismediaSecurityService security,
            CancellationToken cancellationToken) =>
            Results.Ok(await security.GetApiKeyAsync(cancellationToken)))
            .WithName("GetApiKey")
            .WithSummary("Gets the current app API key.")
            .Produces<ApiKeyResponse>();

        group.MapPost("/api-key/regenerate", async (
            HttpContext httpContext,
            PrismediaSecurityService security,
            CancellationToken cancellationToken) => {
            var result = await security.RegenerateApiKeyAsync(cancellationToken);
            httpContext.AppendUiApiKeyCookie(result.ApiKey);
            return Results.Ok(result);
        })
            .WithName("RegenerateApiKey")
            .WithSummary("Regenerates the app API key and invalidates Jellyfin sessions.")
            .Produces<ApiKeyRegenerateResponse>();

        group.MapGet("/jellyfin-profiles", async (
            PrismediaSecurityService security,
            CancellationToken cancellationToken) =>
            Results.Ok(await security.ListProfilesAsync(cancellationToken)))
            .WithName("ListJellyfinProfiles")
            .WithSummary("Lists Jellyfin-compatible fake user profiles.")
            .Produces<JellyfinProfilesResponse>();

        group.MapPost("/jellyfin-profiles", async (
            JellyfinProfileCreateRequest request,
            PrismediaSecurityService security,
            CancellationToken cancellationToken) => {
            try {
                return Results.Ok(await security.CreateProfileAsync(request, cancellationToken));
            } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) {
                return Results.BadRequest(new ApiProblem(ApiProblemCodes.JellyfinProfileInvalid, ex.Message));
            }
        })
            .WithName("CreateJellyfinProfile")
            .WithSummary("Creates a Jellyfin-compatible fake user profile.")
            .Produces<JellyfinProfileResponse>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapPatch("/jellyfin-profiles/{profileId:guid}", async (
            Guid profileId,
            JellyfinProfileUpdateRequest request,
            PrismediaSecurityService security,
            CancellationToken cancellationToken) => {
            try {
                var profile = await security.UpdateProfileAsync(profileId, request, cancellationToken);
                return profile is null
                    ? Results.NotFound(new ApiProblem(ApiProblemCodes.JellyfinProfileNotFound, $"Profile '{profileId}' was not found."))
                    : Results.Ok(profile);
            } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) {
                return Results.BadRequest(new ApiProblem(ApiProblemCodes.JellyfinProfileInvalid, ex.Message));
            }
        })
            .WithName("UpdateJellyfinProfile")
            .WithSummary("Updates a Jellyfin-compatible fake user profile.")
            .Produces<JellyfinProfileResponse>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapDelete("/jellyfin-profiles/{profileId:guid}", async (
            Guid profileId,
            PrismediaSecurityService security,
            CancellationToken cancellationToken) =>
            await security.DeleteProfileAsync(profileId, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound(new ApiProblem(ApiProblemCodes.JellyfinProfileNotFound, $"Profile '{profileId}' was not found.")))
            .WithName("DeleteJellyfinProfile")
            .WithSummary("Deletes a Jellyfin-compatible fake user profile.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }
}
