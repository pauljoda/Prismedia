using Prismedia.Application.Videos;

namespace Prismedia.Api.Endpoints;

internal static class VideoHlsEndpoints {
    internal static IEndpointRouteBuilder MapJellyfinVideoHlsEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapGet("/Videos/{itemId:guid}/master.m3u8", (
            Guid itemId,
            int? audioStreamIndex,
            IHlsAssetService hlsAssets,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            JellyfinPlaybackResults.StreamHlsAssetAsync(itemId, "master.m3u8", audioStreamIndex, hlsAssets, httpContext, cancellationToken))
            .WithName("GetJellyfinVideoMasterPlaylist")
            .WithSummary("Get Jellyfin Video Master Playlist.")
            .WithTags("Jellyfin Videos");

        routes.MapMethods("/Videos/{itemId:guid}/master.m3u8", [HttpMethods.Head], (
            Guid itemId,
            int? audioStreamIndex,
            IHlsAssetService hlsAssets,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            JellyfinPlaybackResults.StreamHlsAssetAsync(itemId, "master.m3u8", audioStreamIndex, hlsAssets, httpContext, cancellationToken))
            .ExcludeFromDescription();

        routes.MapGet("/Videos/{itemId:guid}/hls/{playlistId}/{segmentId}.{container}", (
            Guid itemId,
            string playlistId,
            string segmentId,
            string container,
            int? audioStreamIndex,
            IHlsAssetService hlsAssets,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            JellyfinPlaybackResults.StreamHlsAssetAsync(itemId, $"v/{playlistId}/{segmentId}.{container}", audioStreamIndex, hlsAssets, httpContext, cancellationToken))
            .WithName("GetJellyfinVideoHlsSegment")
            .WithSummary("Get Jellyfin Video Hls Segment.")
            .WithTags("Jellyfin Videos");

        routes.MapMethods("/Videos/{itemId:guid}/hls/{playlistId}/{segmentId}.{container}", [HttpMethods.Head], (
            Guid itemId,
            string playlistId,
            string segmentId,
            string container,
            int? audioStreamIndex,
            IHlsAssetService hlsAssets,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            JellyfinPlaybackResults.StreamHlsAssetAsync(itemId, $"v/{playlistId}/{segmentId}.{container}", audioStreamIndex, hlsAssets, httpContext, cancellationToken))
            .ExcludeFromDescription();

        routes.MapGet("/Videos/{itemId:guid}/v/{playlistId}/{segmentId}.{container}", (
            Guid itemId,
            string playlistId,
            string segmentId,
            string container,
            int? audioStreamIndex,
            IHlsAssetService hlsAssets,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            JellyfinPlaybackResults.StreamHlsAssetAsync(itemId, $"v/{playlistId}/{segmentId}.{container}", audioStreamIndex, hlsAssets, httpContext, cancellationToken))
            .WithName("GetJellyfinVideoHlsRelativeAsset")
            .WithSummary("Get Jellyfin Video Hls Relative Asset.")
            .WithTags("Jellyfin Videos");

        routes.MapMethods("/Videos/{itemId:guid}/v/{playlistId}/{segmentId}.{container}", [HttpMethods.Head], (
            Guid itemId,
            string playlistId,
            string segmentId,
            string container,
            int? audioStreamIndex,
            IHlsAssetService hlsAssets,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            JellyfinPlaybackResults.StreamHlsAssetAsync(itemId, $"v/{playlistId}/{segmentId}.{container}", audioStreamIndex, hlsAssets, httpContext, cancellationToken))
            .ExcludeFromDescription();

        return routes;
    }
}
