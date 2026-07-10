using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Scan;

/// <summary>
/// Shared wanted-tree binding performed before a video upsert. Both a full library scan and the
/// acquisition importer call this path so request-created series, seasons, and episodes are reused.
/// </summary>
internal static class VideoWantedBinding {
    public static async Task BindAsync(
        IAcquisitionHintApplier? acquisitionHints,
        VideoUpsertItem item,
        CancellationToken cancellationToken,
        Guid? acquisitionId = null) {
        if (acquisitionHints is null) {
            return;
        }

        if (item.Movie is { } movie) {
            await acquisitionHints.BindWantedEntityAsync(
                EntityKind.Movie, movie.FolderPath, cancellationToken, acquisitionId, requireExactPath: true);
        }

        if (item.Series is not { } seriesInfo) {
            return;
        }

        await acquisitionHints.BindWantedParentAsync(
            EntityKind.VideoSeries, seriesInfo.FolderPath, cancellationToken, acquisitionId);
        if (item.Season is { } seasonInfo) {
            // Seasons are structural children. Bind only by the parsed season position under the
            // already-bound series; a broad complete-series hint must not directly bind its requested
            // season Entity to whichever season folder happens to be processed first.
            await acquisitionHints.BindWantedChildBySortOrderAsync(
                EntityKind.VideoSeason,
                seriesInfo.FolderPath,
                seasonInfo.SeasonNumber,
                seasonInfo.FolderPath,
                cancellationToken);
            if (item.EpisodeNumber is { } episodeNumber) {
                await acquisitionHints.BindWantedChildBySortOrderAsync(
                    EntityKind.Video,
                    seasonInfo.FolderPath,
                    episodeNumber,
                    item.FilePath,
                    cancellationToken);
            }
        }

        // A single-episode acquisition can key its hint to the exact file rather than its season.
        await acquisitionHints.BindWantedEntityAsync(
            EntityKind.Video, item.FilePath, cancellationToken, acquisitionId, requireExactPath: true);
    }
}

/// <summary>Builds the downstream jobs shared by full video scans and synchronous TV imports.</summary>
internal static class VideoDownstreamJobPlanner {
    public static IReadOnlyList<EnqueueJobRequest> Build(
        LibrarySettingsData settings,
        Guid entityId,
        string sourcePath,
        DownstreamNeeds needs) {
        var label = Path.GetFileNameWithoutExtension(sourcePath);
        var entityIdText = entityId.ToString();
        var requests = new List<EnqueueJobRequest>(5);

        if (settings.AutoGenerateMetadata && needs.NeedsProbe) {
            requests.Add(EnqueueJobRequest.ForEntity(
                JobType.ProbeVideo, EntityKind.Video, entityIdText, label, JobPriorities.Probe));
        }

        if (FingerprintGating.ShouldFingerprint(settings, needs)) {
            requests.Add(EnqueueJobRequest.ForEntity(
                JobType.FingerprintVideo, EntityKind.Video, entityIdText, label, JobPriorities.Fingerprint));
        }

        if (needs.NeedsSubtitleExtraction) {
            requests.Add(EnqueueJobRequest.ForEntity(
                JobType.ExtractSubtitles, EntityKind.Video, entityIdText, label, JobPriorities.Sidecar));
        }

        var shouldGeneratePreview = settings.AutoGeneratePreview && needs.NeedsPreview;
        var shouldGenerateTrickplay = settings.GenerateTrickplay && needs.NeedsTrickplay;
        if (shouldGeneratePreview || shouldGenerateTrickplay) {
            requests.Add(EnqueueJobRequest.ForEntity(
                JobType.GeneratePreview, EntityKind.Video, entityIdText, label, JobPriorities.Preview));
        } else if (needs.NeedsGridThumbnail) {
            requests.Add(EnqueueJobRequest.ForEntity(
                JobType.GenerateGridThumbnail, EntityKind.Video, entityIdText, label, JobPriorities.Thumbnail));
        }

        return requests;
    }
}
