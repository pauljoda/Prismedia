using Prismedia.Application.Files;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

public sealed partial class AcquisitionService {
    #region Actions - Media import review

    private async Task<AcquisitionManualImportReview> GetMediaImportReviewAsync(
        AcquisitionImportContext import, DownloadPayload payload, AcquisitionImportFileLedger? ledger,
        CancellationToken cancellationToken) {
        var visibleFiles = ToReviewFiles(payload, canMapVideos: false, ledger);
        if (import.EntityId is not { } entityId) {
            return Unavailable("This acquisition no longer has a library item to import into.", visibleFiles);
        }
        if (import.ImportPlacementCheckpoint is not null || import.UpgradeOfAcquisitionId is not null) {
            return Unavailable("Retry the saved import or replacement before changing its file mappings.", visibleFiles);
        }

        var movie = import.Kind == EntityKind.Movie;
        var tracks = movie ? [] : await manualImportTargets!.GetRequestedAudioTracksAsync(entityId, cancellationToken);
        AcquisitionManualImportTarget[] targets = movie
            ? [new(entityId, import.Title)]
            : (tracks ?? []).OrderBy(track => track.Position).Select(track =>
                new AcquisitionManualImportTarget(track.EntityId, track.Title, track.Position + 1)).ToArray();
        if (targets.Length == 0) {
            return Unavailable("No requested tracks are available to map. Refresh the album metadata and retry.", visibleFiles);
        }

        var suggestionPlan = movie
            ? MovieImportPlanBuilder.Plan(payload.Files, new ImportTemplateContext(import.Title, import.Author, import.Year))
            : MusicImportPlanBuilder.Plan(payload.Files, import.Author ?? string.Empty, import.Title,
                requestedTracks: tracks, requireArtistMatch: import.Kind == EntityKind.AudioTrack);
        var suggestions = suggestionPlan.Items.ToDictionary(item => item.SourceRelativePath,
            item => movie ? entityId : item.TargetEntityId, FileSystemPathComparison.Comparer);
        var files = visibleFiles.Select(file => file with {
            CanMap = !file.IsDangerous && (movie
                ? MovieImportPlanBuilder.VideoExtensions.Contains(Path.GetExtension(file.SourceRelativePath))
                : MusicImportPlanBuilder.IsAudioFile(file.SourceRelativePath)),
            SuggestedTargetEntityId = suggestions.GetValueOrDefault(file.SourceRelativePath)
        }).ToArray();
        var warning = files.Any(file => file.IsDangerous)
            ? "This download contains potentially dangerous files. They cannot be selected. Review the media before importing."
            : null;
        return new(true, files, targets, movie
            ? "Choose the movie file to accept and import. Accepting overrides the quality warning; the selected video must still pass verification."
            : "Choose the downloaded audio for each requested track. Leave missing tracks unassigned. Each file can be assigned once.", warning);
    }

    #endregion
}
