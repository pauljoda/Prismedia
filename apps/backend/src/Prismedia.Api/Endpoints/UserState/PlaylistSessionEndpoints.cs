using System.Text.Json.Nodes;
using Prismedia.Application.UserState;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

internal static class PlaylistSessionEndpoints {
    internal static RouteGroupBuilder MapPlaylistSessionEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/playlist-session")
            .WithTags("User State");

        group.MapGet("", async (
            UserStateService userState,
            CancellationToken cancellationToken) => {
                var valueJson = await userState.GetPlaylistSessionJsonAsync(cancellationToken);
                return Results.Text(
                    string.IsNullOrWhiteSpace(valueJson) ? "null" : valueJson,
                    "application/json");
            })
            .WithName("GetPlaylistSession")
            .WithSummary("Gets the current browser playlist session.");

        group.MapPut("", async (
            HttpRequest request,
            UserStateService userState,
            CancellationToken cancellationToken) => {
                JsonNode? node;
                try {
                    node = await JsonNode.ParseAsync(request.Body, cancellationToken: cancellationToken);
                } catch {
                    return Results.BadRequest(new ApiProblem("playlist_session_invalid", "Playlist session payload must be valid JSON."));
                }

                if (node is not JsonObject session) {
                    return Results.BadRequest(new ApiProblem("playlist_session_invalid", "Playlist session payload must be a JSON object."));
                }

                var valueJson = await userState.SavePlaylistSessionAsync(session, cancellationToken);
                return Results.Text(valueJson, "application/json");
            })
            .WithName("PutPlaylistSession")
            .WithSummary("Stores the current browser playlist session.");

        group.MapDelete("", async (
            UserStateService userState,
            CancellationToken cancellationToken) => {
                await userState.ClearPlaylistSessionAsync(cancellationToken);
                return Results.Ok(new { ok = true });
            })
            .WithName("DeletePlaylistSession")
            .WithSummary("Clears the current browser playlist session.");

        return group;
    }
}
