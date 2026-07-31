using Prismedia.Api.Mapping;
using Prismedia.Api.Security;
using Prismedia.Application.Entities;
using Prismedia.Application.Videos;
using Prismedia.Contracts.Media;
using Prismedia.Contracts.Playback;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

/// <summary>Native Prismedia video planning, streaming, and session routes.</summary>
internal static class VideoPlaybackEndpoints {
    internal static IEndpointRouteBuilder MapVideoPlaybackEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup(VideoPlaybackProtocol.RoutePrefix)
            .WithTags("Playback");

        group.MapPost("/videos/{entityId:guid}/plan", CreatePlanAsync)
            .WithName("CreateVideoPlaybackPlan")
            .WithSummary("Create a video playback plan.")
            .Produces<VideoPlaybackPlanResponse>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapGet("/videos/{entityId:guid}/stream", StreamSourceAsync)
            .WithName("GetVideoPlaybackSource")
            .WithSummary("Stream the original video source.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status206PartialContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);
        group.MapMethods("/videos/{entityId:guid}/stream", [HttpMethods.Head], StreamSourceAsync)
            .ExcludeFromDescription();

        group.MapGet("/videos/{entityId:guid}/hls/{**asset}", StreamHlsAssetAsync)
            .WithName("GetVideoPlaybackHlsAsset")
            .WithSummary("Stream an HLS playback asset.")
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);
        group.MapMethods("/videos/{entityId:guid}/hls/{**asset}", [HttpMethods.Head], StreamHlsAssetAsync)
            .ExcludeFromDescription();

        group.MapGet("/videos/{entityId:guid}/trickplay/{width:int}/tiles.m3u8", GetTrickplayPlaylistAsync)
            .WithName("GetVideoPlaybackTrickplayPlaylist")
            .WithSummary("Get a video trickplay playlist.")
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);
        group.MapGet("/videos/{entityId:guid}/trickplay/{width:int}/{index:int}.jpg", GetTrickplayTileAsync)
            .WithName("GetVideoPlaybackTrickplayTile")
            .WithSummary("Get a video trickplay tile.")
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        MapSessionEndpoint(group, "/sessions/start", "StartVideoPlaybackSession", static (service, command, token) =>
            service.StartAsync(command, token));
        MapSessionEndpoint(group, "/sessions/progress", "ProgressVideoPlaybackSession", static (service, command, token) =>
            service.ProgressAsync(command, token));
        MapSessionEndpoint(group, "/sessions/ping", "PingVideoPlaybackSession", static (service, command, token) =>
            service.PingAsync(command, token));
        MapSessionEndpoint(group, "/sessions/stop", "StopVideoPlaybackSession", static (service, command, token) =>
            service.StopAsync(command, token));

        return routes;
    }

    private static async Task<IResult> CreatePlanAsync(
        Guid entityId,
        VideoPlaybackPlanRequest request,
        IVideoPlaybackPlanService playback,
        IEntityReadService entities,
        HttpContext httpContext,
        CancellationToken cancellationToken) {
        if (!await IsVisibleAsync(entityId, entities, httpContext, cancellationToken)) {
            return PlaybackNotFound(entityId);
        }

        var query = request.ToApplication();
        if (httpContext.GetPrismediaAuth() is { ViaCookie: false, Token.Length: > 0 } auth) {
            query = query with { AccessToken = auth.Token };
        }

        var plan = await playback.CreatePlanAsync(entityId, query, cancellationToken);
        return plan is null
            ? Results.NotFound(new ApiProblem(
                ApiProblemCodes.PlaybackSourceNotFound,
                $"Entity '{entityId}' has no playable video source."))
            : Results.Ok(plan.ToContract());
    }

    private static async Task<IResult> StreamSourceAsync(
        Guid entityId,
        IVideoSourceService sourceFiles,
        IEntityReadService entities,
        HttpContext httpContext,
        CancellationToken cancellationToken) {
        if (!await IsVisibleAsync(entityId, entities, httpContext, cancellationToken)) {
            return StreamNotFound(entityId);
        }

        var source = await sourceFiles.GetSourceAsync(entityId, cancellationToken);
        return source is null
            ? StreamNotFound(entityId)
            : Results.File(File.OpenRead(source.Path), source.ContentType, enableRangeProcessing: true);
    }

    private static async Task<IResult> StreamHlsAssetAsync(
        Guid entityId,
        string asset,
        int? audioStreamIndex,
        bool? copyAudio,
        IHlsAssetService hlsAssets,
        IEntityReadService entities,
        HttpContext httpContext,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(asset) ||
            !await IsVisibleAsync(entityId, entities, httpContext, cancellationToken)) {
            return HlsNotFound(entityId, asset);
        }

        var hlsAsset = await hlsAssets.GetAssetAsync(
            entityId,
            asset,
            audioStreamIndex,
            cancellationToken,
            copyAudio == true);
        if (hlsAsset is null) {
            return HlsNotFound(entityId, asset);
        }

        httpContext.Response.Headers.CacheControl = hlsAsset.CacheControl;
        return Results.File(File.OpenRead(hlsAsset.Path), hlsAsset.ContentType, enableRangeProcessing: false);
    }

    private static async Task<IResult> GetTrickplayPlaylistAsync(
        Guid entityId,
        int width,
        ITrickplayService trickplay,
        IEntityReadService entities,
        HttpContext httpContext,
        CancellationToken cancellationToken) {
        if (!await IsVisibleAsync(entityId, entities, httpContext, cancellationToken)) {
            return TrickplayNotFound(entityId, width);
        }

        var playlist = await trickplay.GetPlaylistAsync(entityId, width, cancellationToken);
        if (playlist is null) {
            return TrickplayNotFound(entityId, width);
        }

        httpContext.Response.Headers.CacheControl = playlist.CacheControl;
        return Results.Text(playlist.Content, MediaContentTypes.HlsPlaylist);
    }

    private static async Task<IResult> GetTrickplayTileAsync(
        Guid entityId,
        int width,
        int index,
        ITrickplayService trickplay,
        IEntityReadService entities,
        HttpContext httpContext,
        CancellationToken cancellationToken) {
        if (!await IsVisibleAsync(entityId, entities, httpContext, cancellationToken)) {
            return TrickplayTileNotFound(entityId, width, index);
        }

        var tile = await trickplay.GetTileAsync(entityId, width, index, cancellationToken);
        if (tile is null) {
            return TrickplayTileNotFound(entityId, width, index);
        }

        httpContext.Response.Headers.CacheControl = tile.CacheControl;
        return Results.File(File.OpenRead(tile.Path), tile.ContentType, enableRangeProcessing: false);
    }

    private static void MapSessionEndpoint(
        RouteGroupBuilder group,
        string pattern,
        string name,
        Func<IPlaybackSessionService, VideoPlaybackSessionCommand, CancellationToken, Task> observe) =>
        group.MapPost(pattern, async (
            VideoPlaybackSessionRequest request,
            IPlaybackSessionService sessions,
            IEntityReadService entities,
            HttpContext httpContext,
            CancellationToken cancellationToken) => {
                if (!await IsVisibleAsync(request.EntityId, entities, httpContext, cancellationToken)) {
                    return PlaybackNotFound(request.EntityId);
                }

                await observe(sessions, request.ToApplication(), cancellationToken);
                return Results.NoContent();
            })
            .WithName(name)
            .WithSummary($"{name.Replace("VideoPlaybackSession", " video playback session", StringComparison.Ordinal)}.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

    private static async Task<bool> IsVisibleAsync(
        Guid entityId,
        IEntityReadService entities,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        await entities.GetAsync(entityId, NsfwVisibility.ShouldHide(null, httpContext), cancellationToken) is not null;

    private static IResult PlaybackNotFound(Guid entityId) =>
        Results.NotFound(new ApiProblem(ApiProblemCodes.PlaybackItemNotFound, $"Entity '{entityId}' was not found."));

    private static IResult StreamNotFound(Guid entityId) =>
        Results.NotFound(new ApiProblem(ApiProblemCodes.VideoStreamNotFound, $"Video stream '{entityId}' was not found."));

    private static IResult HlsNotFound(Guid entityId, string? asset) =>
        Results.NotFound(new ApiProblem(ApiProblemCodes.VideoHlsNotFound, $"Video HLS asset '{asset}' for '{entityId}' was not found."));

    private static IResult TrickplayNotFound(Guid entityId, int width) =>
        Results.NotFound(new ApiProblem(ApiProblemCodes.VideoTrickplayNotFound, $"Trickplay width '{width}' for '{entityId}' was not found."));

    private static IResult TrickplayTileNotFound(Guid entityId, int width, int index) =>
        Results.NotFound(new ApiProblem(ApiProblemCodes.VideoTrickplayTileNotFound, $"Trickplay tile '{index}' at width '{width}' for '{entityId}' was not found."));
}
