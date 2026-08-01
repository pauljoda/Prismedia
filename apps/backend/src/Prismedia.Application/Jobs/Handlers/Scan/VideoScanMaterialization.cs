using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs.Handlers;
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
            await acquisitionHints.BindWantedFolderAsync(
                EntityKind.Movie, movie.FolderPath, cancellationToken, acquisitionId, requireExactPath: true);
        }

        if (item.Series is not { } seriesInfo) {
            return;
        }

        await acquisitionHints.BindWantedParentFolderAsync(
            EntityKind.VideoSeries, seriesInfo.FolderPath, cancellationToken, acquisitionId);
        if (item.Season is { } seasonInfo) {
            // Seasons are structural children. Bind only by the parsed season position under the
            // already-bound series; a broad complete-series hint must not directly bind its requested
            // season Entity to whichever season folder happens to be processed first.
            await acquisitionHints.BindWantedChildFolderBySortOrderAsync(
                EntityKind.VideoSeason,
                seriesInfo.FolderPath,
                seasonInfo.SeasonNumber,
                seasonInfo.FolderPath,
                cancellationToken);
            if (item.EpisodeNumber is { } episodeNumber) {
                await acquisitionHints.BindWantedChildFileBySortOrderAsync(
                    EntityKind.VideoEpisode,
                    seasonInfo.FolderPath,
                    episodeNumber,
                    item.FilePath,
                    cancellationToken);
            }
        }

        // A single-episode acquisition can key its hint to the exact file rather than its season.
        await acquisitionHints.BindWantedFileAsync(
            EntityKind.VideoEpisode, item.FilePath, cancellationToken, acquisitionId, requireExactPath: true);
    }
}

/// <summary>Builds video processing jobs for ordinary scans and acquisition-readiness materialization.</summary>
internal static class VideoDownstreamJobPlanner {
    public static IReadOnlyList<EnqueueJobRequest> Build(
        LibrarySettingsData settings,
        Guid entityId,
        string sourcePath,
        DownstreamNeeds needs,
        EntityKind kind) =>
        BuildCore(
            settings,
            entityId,
            sourcePath,
            needs,
            kind);

    private static IReadOnlyList<EnqueueJobRequest> BuildCore(
        LibrarySettingsData settings,
        Guid entityId,
        string sourcePath,
        DownstreamNeeds needs,
        EntityKind kind) {
        var label = Path.GetFileNameWithoutExtension(sourcePath);
        return EntityProcessingPlanRequests.ForEntity(kind, entityId, label, settings, needs);
    }
}
