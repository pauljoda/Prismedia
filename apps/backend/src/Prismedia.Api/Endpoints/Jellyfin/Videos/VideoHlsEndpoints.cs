using Prismedia.Application.Entities;
using Prismedia.Application.Videos;
using Prismedia.Contracts.Jellyfin;

namespace Prismedia.Api.Endpoints;

internal static class VideoHlsEndpoints {
    internal static IEndpointRouteBuilder MapJellyfinVideoHlsEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapGet($"/Videos/{{itemId:guid}}/{JellyfinProtocol.Hls.MasterPlaylist}", (
            Guid itemId,
            int? audioStreamIndex,
            bool? copyAudio,
            IHlsAssetService hlsAssets,
            IEntityReadService entities,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            JellyfinPlaybackResults.StreamHlsAssetAsync(itemId, JellyfinProtocol.Hls.MasterPlaylist, audioStreamIndex, copyAudio == true, hlsAssets, entities, httpContext, cancellationToken))
            .WithName("GetJellyfinVideoMasterPlaylist")
            .WithSummary("Get Jellyfin Video Master Playlist.")
            .WithTags("Jellyfin Videos");

        routes.MapMethods($"/Videos/{{itemId:guid}}/{JellyfinProtocol.Hls.MasterPlaylist}", [HttpMethods.Head], (
            Guid itemId,
            int? audioStreamIndex,
            bool? copyAudio,
            IHlsAssetService hlsAssets,
            IEntityReadService entities,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            JellyfinPlaybackResults.StreamHlsAssetAsync(itemId, JellyfinProtocol.Hls.MasterPlaylist, audioStreamIndex, copyAudio == true, hlsAssets, entities, httpContext, cancellationToken))
            .ExcludeFromDescription();

        routes.MapGet($"/Videos/{{itemId:guid}}/{JellyfinProtocol.Hls.MainPlaylist}", (
            Guid itemId,
            int? audioStreamIndex,
            bool? copyAudio,
            IHlsAssetService hlsAssets,
            IEntityReadService entities,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            JellyfinPlaybackResults.StreamHlsAssetAsync(itemId, JellyfinProtocol.Hls.MainPlaylist, audioStreamIndex, copyAudio == true, hlsAssets, entities, httpContext, cancellationToken))
            .WithName("GetJellyfinVideoVariantPlaylist")
            .WithSummary("Get Jellyfin Video Variant Playlist.")
            .WithTags("Jellyfin Videos");

        routes.MapMethods($"/Videos/{{itemId:guid}}/{JellyfinProtocol.Hls.MainPlaylist}", [HttpMethods.Head], (
            Guid itemId,
            int? audioStreamIndex,
            bool? copyAudio,
            IHlsAssetService hlsAssets,
            IEntityReadService entities,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            JellyfinPlaybackResults.StreamHlsAssetAsync(itemId, JellyfinProtocol.Hls.MainPlaylist, audioStreamIndex, copyAudio == true, hlsAssets, entities, httpContext, cancellationToken))
            .ExcludeFromDescription();

        routes.MapGet("/Videos/{itemId:guid}/hls/{playlistId}/{segmentId}.{container}", (
            Guid itemId,
            string playlistId,
            string segmentId,
            string container,
            int? audioStreamIndex,
            bool? copyAudio,
            IHlsAssetService hlsAssets,
            IEntityReadService entities,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            JellyfinPlaybackResults.StreamHlsAssetAsync(itemId, $"v/{playlistId}/{segmentId}.{container}", audioStreamIndex, copyAudio == true, hlsAssets, entities, httpContext, cancellationToken))
            .WithName("GetJellyfinVideoHlsSegment")
            .WithSummary("Get Jellyfin Video Hls Segment.")
            .WithTags("Jellyfin Videos");

        routes.MapMethods("/Videos/{itemId:guid}/hls/{playlistId}/{segmentId}.{container}", [HttpMethods.Head], (
            Guid itemId,
            string playlistId,
            string segmentId,
            string container,
            int? audioStreamIndex,
            bool? copyAudio,
            IHlsAssetService hlsAssets,
            IEntityReadService entities,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            JellyfinPlaybackResults.StreamHlsAssetAsync(itemId, $"v/{playlistId}/{segmentId}.{container}", audioStreamIndex, copyAudio == true, hlsAssets, entities, httpContext, cancellationToken))
            .ExcludeFromDescription();

        routes.MapGet("/Videos/{itemId:guid}/v/{playlistId}/{segmentId}.{container}", (
            Guid itemId,
            string playlistId,
            string segmentId,
            string container,
            int? audioStreamIndex,
            bool? copyAudio,
            IHlsAssetService hlsAssets,
            IEntityReadService entities,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            JellyfinPlaybackResults.StreamHlsAssetAsync(itemId, $"v/{playlistId}/{segmentId}.{container}", audioStreamIndex, copyAudio == true, hlsAssets, entities, httpContext, cancellationToken))
            .WithName("GetJellyfinVideoHlsRelativeAsset")
            .WithSummary("Get Jellyfin Video Hls Relative Asset.")
            .WithTags("Jellyfin Videos");

        routes.MapMethods("/Videos/{itemId:guid}/v/{playlistId}/{segmentId}.{container}", [HttpMethods.Head], (
            Guid itemId,
            string playlistId,
            string segmentId,
            string container,
            int? audioStreamIndex,
            bool? copyAudio,
            IHlsAssetService hlsAssets,
            IEntityReadService entities,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            JellyfinPlaybackResults.StreamHlsAssetAsync(itemId, $"v/{playlistId}/{segmentId}.{container}", audioStreamIndex, copyAudio == true, hlsAssets, entities, httpContext, cancellationToken))
            .ExcludeFromDescription();

        return routes;
    }
}
