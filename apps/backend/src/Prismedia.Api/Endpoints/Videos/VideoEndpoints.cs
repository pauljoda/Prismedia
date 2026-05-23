using Prismedia.Application.Videos;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

public static class VideoEndpoints {
    public static IEndpointRouteBuilder MapVideoEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapVideoCatalogEndpoints();

        var group = routes.MapGroup("/api/videos")
            .WithTags("Videos");

        group.MapGet("/{id:guid}/subtitles/{trackId:guid}", StreamSubtitleAsync)
            .WithName("GetVideoSubtitle")
            .WithSummary("Gets one normalized WebVTT subtitle track.")
            .Produces(StatusCodes.Status200OK)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/subtitles/{trackId:guid}/source", StreamSubtitleSourceAsync)
            .WithName("GetVideoSubtitleSource")
            .WithSummary("Gets one preserved ASS/SSA subtitle source.")
            .Produces(StatusCodes.Status200OK)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<IResult> StreamSubtitleAsync(
        Guid id,
        Guid trackId,
        IVideoSubtitleAssetService subtitles,
        CancellationToken cancellationToken) {
        var subtitle = await subtitles.GetSubtitleAsync(id, trackId, cancellationToken);
        if (subtitle is null) {
            return Results.NotFound(new ApiProblem(
                "video_subtitle_not_found",
                $"Subtitle track '{trackId}' for video '{id}' was not found."));
        }

        return Results.File(File.OpenRead(subtitle.Path), subtitle.ContentType);
    }

    private static async Task<IResult> StreamSubtitleSourceAsync(
        Guid id,
        Guid trackId,
        IVideoSubtitleAssetService subtitles,
        CancellationToken cancellationToken) {
        var subtitle = await subtitles.GetSubtitleSourceAsync(id, trackId, cancellationToken);
        if (subtitle is null) {
            return Results.NotFound(new ApiProblem(
                "video_subtitle_source_not_found",
                $"Subtitle source '{trackId}' for video '{id}' was not found."));
        }

        return Results.File(File.OpenRead(subtitle.Path), subtitle.ContentType);
    }
}
