using Prismedia.Api.Security;
using Prismedia.Api.Mapping;
using Prismedia.Application.Entities;
using Prismedia.Application.Videos;
using Prismedia.Contracts.Media;
using Prismedia.Contracts.Playback;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

internal static class JellyfinPlaybackResults {
    internal static async Task<IResult> GetPlaybackInfoAsync(
        Guid itemId,
        IPlaybackInfoService playback,
        IEntityReadService entities,
        HttpContext httpContext,
        PlaybackInfoRequest? request,
        CancellationToken cancellationToken) {
        if (!await IsVisibleAsync(itemId, entities, httpContext, cancellationToken)) {
            return Results.NotFound(new ApiProblem("playback_item_not_found", $"Item '{itemId}' was not found."));
        }

        var appRequest = request?.ToApplication() ?? new PlaybackInfoQuery();
        if (httpContext.GetPrismediaAuth() is { Kind: PrismediaAuthKind.JellyfinSession } auth) {
            appRequest = appRequest with { AccessToken = auth.Token };
        }

        var info = await playback.GetPlaybackInfoAsync(itemId, appRequest, cancellationToken);
        return info is null
            ? Results.NotFound(new ApiProblem("playback_source_not_found", $"Item '{itemId}' has no playable source."))
            : Results.Ok(info.ToContract());
    }

    internal static async Task<IResult> StreamVideoAsync(
        Guid itemId,
        IVideoSourceService sourceFiles,
        IEntityReadService entities,
        HttpContext httpContext,
        CancellationToken cancellationToken) {
        if (!await IsVisibleAsync(itemId, entities, httpContext, cancellationToken)) {
            return Results.NotFound(new ApiProblem("video_stream_not_found", $"Video stream '{itemId}' was not found."));
        }

        var source = await sourceFiles.GetSourceAsync(itemId, cancellationToken);
        if (source is null) {
            return Results.NotFound(new ApiProblem("video_stream_not_found", $"Video stream '{itemId}' was not found."));
        }

        return Results.File(File.OpenRead(source.Path), source.ContentType, enableRangeProcessing: true);
    }

    internal static async Task<IResult> StreamHlsAssetAsync(
        Guid itemId,
        string asset,
        int? audioStreamIndex,
        IHlsAssetService hlsAssets,
        IEntityReadService entities,
        HttpContext httpContext,
        CancellationToken cancellationToken) {
        if (!await IsVisibleAsync(itemId, entities, httpContext, cancellationToken)) {
            return Results.NotFound(new ApiProblem("video_hls_not_found", $"Video HLS asset '{asset}' for '{itemId}' was not found."));
        }

        var hlsAsset = await hlsAssets.GetAssetAsync(itemId, asset, audioStreamIndex, cancellationToken);
        if (hlsAsset is null) {
            return Results.NotFound(new ApiProblem("video_hls_not_found", $"Video HLS asset '{asset}' for '{itemId}' was not found."));
        }

        httpContext.Response.Headers.CacheControl = hlsAsset.CacheControl;
        return Results.File(File.OpenRead(hlsAsset.Path), hlsAsset.ContentType, enableRangeProcessing: false);
    }

    internal static async Task<IResult> GetTrickplayPlaylistAsync(
        Guid itemId,
        int width,
        ITrickplayService trickplay,
        IEntityReadService entities,
        HttpContext httpContext,
        CancellationToken cancellationToken) {
        if (!await IsVisibleAsync(itemId, entities, httpContext, cancellationToken)) {
            return Results.NotFound(new ApiProblem("video_trickplay_not_found", $"Trickplay width '{width}' for '{itemId}' was not found."));
        }

        var playlist = await trickplay.GetPlaylistAsync(itemId, width, cancellationToken);
        if (playlist is null) {
            return Results.NotFound(new ApiProblem("video_trickplay_not_found", $"Trickplay width '{width}' for '{itemId}' was not found."));
        }

        httpContext.Response.Headers.CacheControl = playlist.CacheControl;
        return Results.Text(playlist.Content, MediaContentTypes.HlsPlaylist);
    }

    internal static async Task<IResult> GetTrickplayTileAsync(
        Guid itemId,
        int width,
        int index,
        ITrickplayService trickplay,
        IEntityReadService entities,
        HttpContext httpContext,
        CancellationToken cancellationToken) {
        if (!await IsVisibleAsync(itemId, entities, httpContext, cancellationToken)) {
            return Results.NotFound(new ApiProblem("video_trickplay_tile_not_found", $"Trickplay tile '{index}' for '{itemId}' was not found."));
        }

        var tile = await trickplay.GetTileAsync(itemId, width, index, cancellationToken);
        if (tile is null) {
            return Results.NotFound(new ApiProblem("video_trickplay_tile_not_found", $"Trickplay tile '{index}' for '{itemId}' was not found."));
        }

        httpContext.Response.Headers.CacheControl = tile.CacheControl;
        return Results.File(File.OpenRead(tile.Path), tile.ContentType, enableRangeProcessing: false);
    }

    internal static async Task<IResult> MarkPlayedAsync(
        Guid itemId,
        IPlaybackSessionService sessions,
        IEntityReadService entities,
        HttpContext httpContext,
        CancellationToken cancellationToken) {
        if (!await IsVisibleAsync(itemId, entities, httpContext, cancellationToken)) {
            return Results.NotFound(new ApiProblem("playback_item_not_found", $"Item '{itemId}' was not found."));
        }

        var result = await sessions.MarkPlayedAsync(itemId, cancellationToken);
        return result is null
            ? Results.NotFound(new ApiProblem("playback_item_not_found", $"Item '{itemId}' was not found."))
            : Results.Ok(result.ToContract());
    }

    internal static async Task<IResult> MarkUnplayedAsync(
        Guid itemId,
        IPlaybackSessionService sessions,
        IEntityReadService entities,
        HttpContext httpContext,
        CancellationToken cancellationToken) {
        if (!await IsVisibleAsync(itemId, entities, httpContext, cancellationToken)) {
            return Results.NotFound(new ApiProblem("playback_item_not_found", $"Item '{itemId}' was not found."));
        }

        var result = await sessions.MarkUnplayedAsync(itemId, cancellationToken);
        return result is null
            ? Results.NotFound(new ApiProblem("playback_item_not_found", $"Item '{itemId}' was not found."))
            : Results.Ok(result.ToContract());
    }

    internal static async Task<bool> IsVisibleAsync(
        Guid itemId,
        IEntityReadService entities,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        httpContext.HasAuthorizedPlaybackSession(itemId) ||
        await entities.GetAsync(itemId, NsfwVisibility.ShouldHide(null, httpContext), cancellationToken) is not null;
}
