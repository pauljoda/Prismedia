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
    ISubtitleSidecarDiscovery subtitleSidecars,
    ILogger<MediaUpgradePayloadInspector> logger) : IMediaUpgradePayloadInspector {
    public async Task<MediaUpgradePayloadInspection?> InspectAsync(
        string ownedContentPath,
        string candidateContentPath,
        CancellationToken cancellationToken) {
        try {
            var ownedFile = VideoUpgradeFileSelection.Find(ownedContentPath);
            var candidateFile = VideoUpgradeFileSelection.Find(candidateContentPath);
            if (ownedFile is null || candidateFile is null) {
                return null;
            }

            var ownedVideo = await mediaProbe.ProbeVideoAsync(ownedFile, cancellationToken);
            var ownedSubtitles = ownedVideo?.SubtitleStreams
                ?? await mediaProbe.ProbeSubtitleStreamsAsync(ownedFile, cancellationToken);
            var candidateVideo = await mediaProbe.ProbeVideoAsync(candidateFile, cancellationToken);
            var candidateSubtitles = candidateVideo?.SubtitleStreams
                ?? await mediaProbe.ProbeSubtitleStreamsAsync(candidateFile, cancellationToken);
            var sidecarDiscoveries = await subtitleSidecars.DiscoverAsync(
                [ownedFile, candidateFile],
                cancellationToken);
            if (sidecarDiscoveries.Count != 2 || sidecarDiscoveries.Any(discovery => !discovery.IsComplete)) {
                return null;
            }
            var ownedResolution = VideoPayloadProfileValidation.ResolutionTier(ownedVideo);
            var candidateResolution = VideoPayloadProfileValidation.ResolutionTier(candidateVideo);
            if (ownedResolution is null || candidateResolution is null) {
                return null;
            }

            return new MediaUpgradePayloadInspection(
                ownedResolution.Value,
                candidateResolution.Value,
                ownedSubtitles.Count > 0 || sidecarDiscoveries[0].Candidates.Count > 0,
                candidateSubtitles.Count > 0 || sidecarDiscoveries[1].Candidates.Count > 0,
                ownedVideo?.DurationSeconds,
                candidateVideo?.DurationSeconds,
                candidateVideo is null ? null : VideoPayloadProfileValidation.AudioLanguages(candidateVideo));
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logger.LogWarning(ex, "Could not inspect a downloaded media-upgrade payload.");
            return null;
        }
    }

}
