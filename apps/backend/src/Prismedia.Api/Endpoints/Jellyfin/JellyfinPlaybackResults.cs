using Prismedia.Api.Mapping;
using Prismedia.Application.Videos;
using Prismedia.Contracts.Playback;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

internal static class JellyfinPlaybackResults {
    internal static async Task<IResult> GetPlaybackInfoAsync(
        Guid itemId,
        IPlaybackInfoService playback,
        PlaybackInfoRequest? request,
        CancellationToken cancellationToken) {
        var info = await playback.GetPlaybackInfoAsync(itemId, request?.ToApplication(), cancellationToken);
        return info is null
            ? Results.NotFound(new ApiProblem("playback_source_not_found", $"Item '{itemId}' has no playable source."))
            : Results.Ok(info.ToContract());
    }

    internal static async Task<IResult> StreamVideoAsync(
        Guid itemId,
        IVideoSourceService sourceFiles,
        CancellationToken cancellationToken) {
        var source = await sourceFiles.GetSourceAsync(itemId, cancellationToken);
        if (source is null) {
            return Results.NotFound(new ApiProblem("video_stream_not_found", $"Video stream '{itemId}' was not found."));
        }

        if (!source.DirectPlayable) {
            return Results.Json(
                new ApiProblem("video_stream_not_direct_playable", "Direct playback is not available for this container."),
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        return Results.File(File.OpenRead(source.Path), source.ContentType, enableRangeProcessing: true);
    }

    internal static async Task<IResult> StreamHlsAssetAsync(
        Guid itemId,
        string asset,
        int? audioStreamIndex,
        IHlsAssetService hlsAssets,
        HttpContext httpContext,
        CancellationToken cancellationToken) {
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
        HttpContext httpContext,
        CancellationToken cancellationToken) {
        var playlist = await trickplay.GetPlaylistAsync(itemId, width, cancellationToken);
        if (playlist is null) {
            return Results.NotFound(new ApiProblem("video_trickplay_not_found", $"Trickplay width '{width}' for '{itemId}' was not found."));
        }

        httpContext.Response.Headers.CacheControl = playlist.CacheControl;
        return Results.Text(playlist.Content, "application/vnd.apple.mpegurl");
    }

    internal static async Task<IResult> GetTrickplayTileAsync(
        Guid itemId,
        int width,
        int index,
        ITrickplayService trickplay,
        HttpContext httpContext,
        CancellationToken cancellationToken) {
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
        CancellationToken cancellationToken) {
        var result = await sessions.MarkPlayedAsync(itemId, cancellationToken);
        return result is null
            ? Results.NotFound(new ApiProblem("playback_item_not_found", $"Item '{itemId}' was not found."))
            : Results.Ok(result.ToContract());
    }

    internal static async Task<IResult> MarkUnplayedAsync(
        Guid itemId,
        IPlaybackSessionService sessions,
        CancellationToken cancellationToken) {
        var result = await sessions.MarkUnplayedAsync(itemId, cancellationToken);
        return result is null
            ? Results.NotFound(new ApiProblem("playback_item_not_found", $"Item '{itemId}' was not found."))
            : Results.Ok(result.ToContract());
    }
}
