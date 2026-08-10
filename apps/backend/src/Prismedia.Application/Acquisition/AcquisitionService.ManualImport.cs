using Prismedia.Application.Files;
using Prismedia.Application.Jobs;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>Manual import review and submission use cases.</summary>
public sealed partial class AcquisitionService {
    /// <summary>
    /// Rejects the exact release currently held for manual import, removes its transfer and downloaded
    /// data, and replaces the acquisition with a fresh monitored search. The selected-release snapshot is
    /// the authority for the blocklist identity, so a similarly titled candidate is never guessed from the
    /// current search results.
    /// </summary>
    public async Task<bool> RejectManualImportAsync(
        Guid id,
        CancellationToken cancellationToken) {
        var detail = await store.GetAsync(id, cancellationToken);
        if (detail is null) {
            return false;
        }

        var canResumeRejection = detail.Summary.Status == AcquisitionStatus.Stopping
            && await store.GetTeardownClaimAsync(id, cancellationToken) is {
                Intent: AcquisitionTeardownIntent.Reacquire,
                OriginalStatus: AcquisitionStatus.ManualImportRequired
            };
        if (detail.Summary.Status != AcquisitionStatus.ManualImportRequired && !canResumeRejection) {
            throw InvalidManualMapping("Only a download held for manual import can be rejected here.");
        }

        var selected = await store.GetSelectedReleaseAsync(id, cancellationToken)
            ?? throw InvalidManualMapping("The downloaded release identity is no longer available. Remove the acquisition to start over without blocklisting it.");
        await blocklist.AddAsync(
            new BlocklistAddRequest(
                selected.Identity,
                BlocklistReason.Manual,
                selected.Title,
                selected.IndexerName,
                selected.InfoHash,
                id,
                "Rejected during manual import review."),
            cancellationToken);

        return await DeleteAsync(id, cancellationToken, preserveWantedLoop: true);
    }

    /// <summary>
    /// Builds a file-to-Entity mapping review for a television acquisition held for manual import.
    /// Automatic numbering and title matches are suggestions only; ambiguous rows stay unselected.
    /// </summary>
    public async Task<AcquisitionManualImportReview> GetManualImportReviewAsync(
        Guid id,
        CancellationToken cancellationToken) {
        var detail = await store.GetAsync(id, cancellationToken);
        if (detail is null) {
            throw new AcquisitionConfigurationException(
                ApiProblemCodes.AcquisitionNotFound,
                "Acquisition was not found.");
        }

        if (detail.Summary.Status != AcquisitionStatus.ManualImportRequired) {
            return Unavailable("File mapping is available when an import needs manual review.");
        }

        if (manualImportPayloads is null || manualImportTargets is null) {
            return Unavailable("Manual file mapping is unavailable in this runtime.");
        }

        var import = await store.GetImportContextAsync(id, cancellationToken);
        if (import is null) {
            return Unavailable("The acquisition import context is no longer available.");
        }

        var payload = string.IsNullOrWhiteSpace(import.ContentPath)
            ? null
            : manualImportPayloads.Read(import.ContentPath);
        if (payload is null) {
            return Unavailable("The downloaded payload is no longer available for review.");
        }
        var visibleFiles = ToReviewFiles(payload, canMapVideos: false);

        if (import.CheckpointProtocol != AcquisitionCheckpointProtocol.Television) {
            return Unavailable(
                "These downloaded files can be reviewed here, but this kind does not support per-file mapping yet.",
                visibleFiles);
        }

        if (DangerousFileDetection.FindDangerousFile(payload.Files.Select(file => file.RelativePath)) is { } dangerous) {
            return Unavailable(
                $"This download contains a potentially dangerous file ({Path.GetFileName(dangerous)}) and cannot be imported. Block the release and search again.",
                visibleFiles);
        }

        if (import.EntityId is not { } entityId || import.SeasonNumber is not { } seasonNumber) {
            return Unavailable(
                "This acquisition has no season episode list to map against.",
                visibleFiles);
        }

        var episodes = await manualImportTargets.GetSeasonEpisodeTitlesAsync(
            entityId,
            seasonNumber,
            cancellationToken);
        var targetEpisodes = episodes
            .Where(episode => episode.EntityId is not null)
            .OrderBy(episode => episode.Episode)
            .ToArray();
        if (targetEpisodes.Length == 0) {
            return Unavailable("No episode Entities are available for this season.", visibleFiles);
        }

        var suggestions = payload.Files
            .Where(file => TvImportPlanBuilder.IsVideoFile(file.RelativePath))
            .Select(file => new {
                file.RelativePath,
                Inferred = TvImportPlanBuilder.InferEpisode(file.RelativePath, seasonNumber, episodes)
            })
            .Where(item => item.Inferred is not null)
            .Select(item => new {
                item.RelativePath,
                Target = targetEpisodes.FirstOrDefault(target => target.Episode == item.Inferred!.Value.Episode)
            })
            .Where(item => item.Target?.EntityId is not null)
            .ToArray();
        var duplicateTargetIds = suggestions
            .GroupBy(item => item.Target!.EntityId!.Value)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();
        var suggestionBySource = suggestions
            .Where(item => !duplicateTargetIds.Contains(item.Target!.EntityId!.Value))
            .ToDictionary(
                item => item.RelativePath,
                item => item.Target!.EntityId!.Value,
                FileSystemPathComparison.Comparer);

        var files = ToReviewFiles(payload, canMapVideos: true)
            .Select(file => file with {
                SuggestedTargetEntityId = suggestionBySource.TryGetValue(file.SourceRelativePath, out var suggestion)
                    ? suggestion
                    : null
            })
            .ToArray();
        var targets = targetEpisodes
            .Select(episode => new AcquisitionManualImportTarget(
                episode.EntityId!.Value,
                episode.Title,
                episode.Episode))
            .ToArray();
        return new AcquisitionManualImportReview(
            true,
            files,
            targets,
            "Choose the downloaded file that contains each expected episode. One file may satisfy several episodes; leave episodes unassigned when they are not present.");
    }

    /// <summary>
    /// Validates an explicit file mapping against the current payload and season graph, then queues the
    /// ordinary crash-safe TV importer with those choices captured in its durable job payload.
    /// </summary>
    public async Task<AcquisitionDetail?> SubmitManualImportAsync(
        Guid id,
        AcquisitionManualImportRequest request,
        CancellationToken cancellationToken) {
        var review = await GetManualImportReviewAsync(id, cancellationToken);
        if (!review.Available) {
            throw InvalidManualMapping(review.Message ?? "This acquisition cannot be mapped manually.");
        }

        var selections = request.Selections ?? [];
        if (selections.Count == 0) {
            throw InvalidManualMapping("Map at least one downloaded file before importing.");
        }

        var import = await store.GetImportContextAsync(id, cancellationToken)
            ?? throw InvalidManualMapping("The acquisition import context is no longer available.");
        if (import.SeasonNumber is not { } seasonNumber) {
            throw InvalidManualMapping("The acquisition no longer identifies a season.");
        }

        var files = review.Files
            .Where(file => file.CanMap)
            .ToDictionary(file => file.SourceRelativePath, FileSystemPathComparison.Comparer);
        var targets = review.Targets.ToDictionary(target => target.EntityId);
        var usedTargets = new HashSet<Guid>();
        var mappings = new List<ManualImportFileMapping>(selections.Count);
        foreach (var selection in selections) {
            if (!files.TryGetValue(selection.SourceRelativePath, out var file)) {
                throw InvalidManualMapping("One or more selected files are not available in this download.");
            }
            if (!targets.TryGetValue(selection.TargetEntityId, out var target)
                || target.Position is not { } episodeNumber
                || !usedTargets.Add(selection.TargetEntityId)) {
                throw InvalidManualMapping("Each episode may be mapped only once and must belong to this season.");
            }
            mappings.Add(new ManualImportFileMapping(
                file.SourceRelativePath,
                selection.TargetEntityId,
                seasonNumber,
                episodeNumber));
        }

        var importJob = await queue.EnqueueAsync(
            new EnqueueJobRequest(
                JobType.AcquisitionImport,
                PayloadJson: AcquisitionJobPayload.Serialize(
                    id,
                    manualRetry: true,
                    manualFileMappings: mappings),
                TargetEntityId: id.ToString(),
                TargetLabel: import.Title,
                Origin: JobGraphOrigin.Interactive,
                GraphRootEntityKind: import.Kind.ToCode(),
                GraphRootEntityId: import.EntityId?.ToString()),
            cancellationToken);
        if (importJob.GraphId is { } graphId) {
            await store.SetJobGraphIdAsync(id, graphId, cancellationToken);
        }
        return await store.GetAsync(id, cancellationToken);
    }

    private static IReadOnlyList<AcquisitionManualImportFile> ToReviewFiles(
        DownloadPayload payload,
        bool canMapVideos) => payload.Files
        .Select(file => new AcquisitionManualImportFile(
            file.RelativePath,
            Path.GetFileName(file.RelativePath),
            file.SizeBytes,
            canMapVideos && TvImportPlanBuilder.IsVideoFile(file.RelativePath)))
        .ToArray();

    private static AcquisitionManualImportReview Unavailable(
        string message,
        IReadOnlyList<AcquisitionManualImportFile>? files = null) =>
        new(false, files ?? [], [], message);

    private static AcquisitionConfigurationException InvalidManualMapping(string message) =>
        new(ApiProblemCodes.AcquisitionInvalid, message);
}
