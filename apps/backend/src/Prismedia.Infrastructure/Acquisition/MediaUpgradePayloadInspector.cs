using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs.Ports;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// Probes the actual single-video payloads participating in an atomic upgrade. Release titles remain a
/// cheap search gate, but their claimed resolution and subtitle hints are never trusted at replacement time.
/// </summary>
public sealed class MediaUpgradePayloadInspector(
    IMediaProbe mediaProbe,
    ILogger<MediaUpgradePayloadInspector> logger) : IMediaUpgradePayloadInspector {
    public async Task<MediaUpgradePayloadInspection?> InspectAsync(
        string ownedContentPath,
        string candidateContentPath,
        CancellationToken cancellationToken) {
        try {
            var ownedFile = FindSingleVideo(ownedContentPath);
            var candidateFile = FindSingleVideo(candidateContentPath);
            if (ownedFile is null || candidateFile is null) {
                return null;
            }

            var ownedVideo = await mediaProbe.ProbeVideoAsync(ownedFile, cancellationToken);
            var ownedSubtitles = await mediaProbe.ProbeSubtitleStreamsAsync(ownedFile, cancellationToken);
            var candidateVideo = await mediaProbe.ProbeVideoAsync(candidateFile, cancellationToken);
            var candidateSubtitles = await mediaProbe.ProbeSubtitleStreamsAsync(candidateFile, cancellationToken);
            var ownedResolution = ResolutionTier(ownedVideo);
            var candidateResolution = ResolutionTier(candidateVideo);
            if (ownedResolution is null || candidateResolution is null) {
                return null;
            }

            return new MediaUpgradePayloadInspection(
                ownedResolution.Value,
                candidateResolution.Value,
                ownedSubtitles.Count > 0,
                candidateSubtitles.Count > 0);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logger.LogWarning(ex, "Could not inspect a downloaded media-upgrade payload.");
            return null;
        }
    }

    private static int? ResolutionTier(VideoProbeData? video) {
        if (video?.Width is not > 0 || video.Height is not > 0) {
            return null;
        }

        var longEdge = Math.Max(video.Width.Value, video.Height.Value);
        return longEdge switch {
            >= 3_000 => 2160,
            >= 1_600 => 1080,
            >= 1_100 => 720,
            _ => 480
        };
    }

    private static string? FindSingleVideo(string path) {
        if (File.Exists(path)) {
            return MovieImportPlanBuilder.VideoExtensions.Contains(Path.GetExtension(path)) ? path : null;
        }

        if (!Directory.Exists(path)) {
            return null;
        }

        var videos = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Where(file => MovieImportPlanBuilder.VideoExtensions.Contains(Path.GetExtension(file)))
            .Take(2)
            .ToArray();
        return videos.Length == 1 ? videos[0] : null;
    }
}
