using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs.Handlers.Scan;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Music import engine: places the album release's audio files (and cover art) under
/// <c>{Artist}/{Album}/</c> in the first audio-enabled library root, preserving disc-folder structure,
/// writes the identify hint keyed on the album folder, and chains an audio scan — which binds the album
/// and artist folders to their wanted entities via the acquisition hint. The profile's import mode controls
/// whether the payload is moved, copied, or hardlinked before cleanup or seeding watch.
/// </summary>
[AcquisitionStrategy(AcquisitionNamingFamily.Music)]
public sealed partial class MusicAcquisitionImportEngine(
    IAcquisitionStore acquisitions,
    IBookAcquisitionProfileStore profiles,
    ILibraryScanRootPersistence roots,
    IDownloadPayloadReader payloads,
    IImportFileMover mover,
    DownloadClientCleanupService torrents,
    IImportTargetIndex targets,
    IAcquisitionBlocklistStore blocklist,
    IAcquisitionHistoryStore history,
    IImportedEntityMaterializer materializer,
    ILogger<MusicAcquisitionImportEngine> logger,
    IMonitorStore? monitors = null) : IAcquisitionImportEngine {

    public async Task ImportAsync(JobContext context, AcquisitionImportContext import, CancellationToken cancellationToken) {
        var profile = await profiles.GetImportProfileAsync(import.ProfileId, EntityKind.AudioLibrary, cancellationToken);

        if (import.ImportPlacementCheckpoint is { } durableCheckpoint) {
            var checkpointRoot = await ResolveCheckpointRootAsync(durableCheckpoint, cancellationToken);
            if (checkpointRoot is null) {
                await acquisitions.SetStatusAsync(
                    import.Id,
                    AcquisitionStatus.ManualImportRequired,
                    "The saved album import targets a library root that moved, was disabled, or no longer accepts audio. Review the partial import before retrying.",
                    cancellationToken);
                return;
            }
            if (!ImportPlacementExecution.MatchesTransfer(durableCheckpoint, import)) {
                await acquisitions.SetStatusAsync(
                    import.Id,
                    AcquisitionStatus.ManualImportRequired,
                    "This album import checkpoint belongs to a different download attempt and was not reused. Review the partial files before retrying.",
                    cancellationToken);
                return;
            }

            ImportPlacementCheckpoint? resumed;
            var replacementAlreadySwapped = import.UpgradeOfAcquisitionId is not null
                && AlbumReplacementAlreadySwapped(durableCheckpoint);
            if (replacementAlreadySwapped) {
                resumed = durableCheckpoint;
            } else {
                resumed = await ImportPlacementExecution.ExecuteAsync(
                    acquisitions,
                    mover,
                    import.Id,
                    durableCheckpoint,
                    cancellationToken);
            }
            if (resumed is null) {
                return;
            }

            if (import.UpgradeOfAcquisitionId is not null) {
                await FinalizeAlbumReplacementAsync(
                    context,
                    import,
                    checkpointRoot,
                    resumed,
                    cancellationToken);
                return;
            }

            await FinalizeImportAsync(
                context,
                import,
                checkpointRoot,
                resumed.HintPath,
                ImportPlacementExecution.MediaPaths(resumed),
                ImportPlacementExecution.MediaEntityTargets(resumed),
                resumed.ImportMode,
                resumed.SuccessMessage,
                resumed.DiscardRemainingPayload,
                cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(import.ContentPath) || payloads.Read(import.ContentPath) is not { } payload) {
            await Fail(import.Id, "The completed download reported no content path.", cancellationToken);
            return;
        }

        var artist = string.IsNullOrWhiteSpace(import.Author) ? "Unknown Artist" : import.Author;
        var albumTitle = AlbumTitleOf(import);
        IReadOnlyList<RequestedAudioTrack>? requestedTracks = null;
        if (import.UpgradeOfAcquisitionId is null && import.EntityId is { } requestedEntityId) {
            requestedTracks = await targets.GetRequestedAudioTracksAsync(requestedEntityId, cancellationToken);
        }
        var rawPlan = MusicImportPlanBuilder.Plan(
            payload.Files,
            artist,
            albumTitle,
            profile?.PathTemplate,
            import.Year,
            requestedTracks);
        if (rawPlan.Blocked) {
            await acquisitions.SetStatusAsync(
                import.Id, AcquisitionStatus.ManualImportRequired,
                requestedTracks is null
                    ? "The download contains no supported audio files."
                    : "None of the downloaded audio files uniquely matched the requested tracks.",
                cancellationToken);
            return;
        }

        var discardRemainingPayload = payload.Files.Count(file => MusicImportPlanBuilder.IsAudioFile(file.RelativePath))
            > rawPlan.Items.Count(item => MusicImportPlanBuilder.IsAudioFile(item.SourceRelativePath));

        if (import.UpgradeOfAcquisitionId is not null) {
            await ReplaceExistingAlbumAsync(
                context,
                import,
                payload,
                rawPlan,
                profile,
                discardRemainingPayload,
                cancellationToken);
            return;
        }

        // An album (or its artist) that already lives on disk merges into the existing folders — a
        // template-derived parallel artist/album folder would mint duplicates on scan.
        if (import.EntityId is { } linkedEntityId
            && await targets.GetAlbumTargetAsync(linkedEntityId, cancellationToken) is { } target
            && ExistingAlbumFolderOf(target, artist, import, profile) is { } albumTarget) {
            var existingRoot = await ImportRootResolution.ResolveOwningAsync(
                roots,
                albumTarget,
                static candidate => candidate.ScanAudio,
                cancellationToken);
            if (existingRoot is null) {
                await Fail(import.Id, "The existing album is outside every enabled audio library root.", cancellationToken);
                return;
            }

            await ImportIntoExistingAlbumAsync(
                context,
                import,
                payload,
                rawPlan,
                albumTarget,
                target,
                existingRoot,
                profile,
                discardRemainingPayload,
                cancellationToken);
            return;
        }

        var root = await ImportRootResolution.ResolveAsync(
            roots, import.TargetLibraryRootId, profile?.TargetLibraryRootId, static candidate => candidate.ScanAudio, cancellationToken);
        if (root is null) {
            await Fail(import.Id, "No enabled audio library root exists to import the album into.", cancellationToken);
            return;
        }

        var plan = ImportTargetResolver.Resolve(payload.ContentRoot, root.Path, rawPlan);
        if (plan.Blocked) {
            await acquisitions.SetStatusAsync(
                import.Id, AcquisitionStatus.ManualImportRequired,
                "The download contains no supported audio files.", cancellationToken);
            return;
        }

        var importMode = profile?.ImportMode ?? ImportMode.Move;
        // The hint and final path key on the ALBUM folder (not a disc subfolder a track landed in), so
        // the audio scan's album upsert path matches the bind exactly.
        var albumFolder = Path.GetFullPath(Path.Combine(root.Path, MusicImportPlanBuilder.AlbumFolderRelative(artist, albumTitle, profile?.PathTemplate, import.Year)));
        var units = ImportPlacementExecution.ReserveUnits(
            payload.ContentRoot,
            plan.Items
                .Select(item => (item, IsMedia: MusicImportPlanBuilder.IsAudioFile(item.SourceAbsolutePath)))
                .ToArray(),
            mover);
        var checkpoint = CreateCheckpoint(
            import,
            context,
            root,
            importMode,
            albumFolder,
            "Imported into the library.",
            units);
        checkpoint = checkpoint with { DiscardRemainingPayload = discardRemainingPayload };
        if (!await acquisitions.TryCreateImportPlacementCheckpointAsync(import.Id, checkpoint, cancellationToken)) {
            logger.LogInformation(
                "Album import checkpoint for {Id} was superseded before placement; skipping stale work.",
                import.Id);
            return;
        }

        await context.ReportProgressAsync(40, "Moving files", cancellationToken);
        var completed = await ImportPlacementExecution.ExecuteAsync(
            acquisitions,
            mover,
            import.Id,
            checkpoint,
            cancellationToken);
        if (completed is null) {
            return;
        }

        await FinalizeImportAsync(
            context,
            import,
            root,
            completed.HintPath,
            ImportPlacementExecution.MediaPaths(completed),
            ImportPlacementExecution.MediaEntityTargets(completed),
            completed.ImportMode,
            completed.SuccessMessage,
            completed.DiscardRemainingPayload,
            cancellationToken);
    }

    /// <summary>
    /// The merged path for an existing album/artist: plan items re-anchor onto the existing album folder,
    /// tracks the album already owns are dropped (track names carry no reliable quality — never replace),
    /// and a payload with nothing new fails with the release blocklisted.
    /// </summary>
    private async Task ImportIntoExistingAlbumAsync(
        JobContext context,
        AcquisitionImportContext import,
        DownloadPayload payload,
        ImportPlan rawPlan,
        string albumFolder,
        AlbumDiskTarget target,
        LibraryRootData root,
        BookImportProfile? profile,
        bool discardRemainingPayload,
        CancellationToken cancellationToken) {
        var merged = MusicExistingTargetMerge.Plan(rawPlan.Items, albumFolder, target.ExistingRelativeFiles);

        var placeNew = merged.Where(item => item.Action == MergeFileAction.PlaceNew).ToArray();
        // Cover art alone is not an acquisition result. Gate on new audio before ANY companion file is
        // placed, otherwise a release containing only already-owned tracks can mutate artwork and then
        // fail materialization with an empty media set.
        if (!placeNew.Any(item => MusicImportPlanBuilder.IsAudioFile(item.SourceRelativePath))) {
            var selected = await acquisitions.GetSelectedReleaseAsync(import.Id, cancellationToken);
            await MergedImportExecution.FailNothingUsableAsync(
                acquisitions, blocklist, history, torrents, logger, import, selected,
                hasFormatChange: false, formatChangeMessage: string.Empty, cancellationToken);
            return;
        }

        var importMode = profile?.ImportMode ?? ImportMode.Move;
        var units = ImportPlacementExecution.ReserveUnits(
            payload.ContentRoot,
            placeNew.Select(item => {
                var sourceAbsolute = Path.GetFullPath(Path.Combine(payload.ContentRoot, item.SourceRelativePath));
                return (
                    new ResolvedImportItem(
                        sourceAbsolute,
                        Path.GetFullPath(item.TargetAbsolutePath),
                        item.TargetEntityId),
                    IsMedia: MusicImportPlanBuilder.IsAudioFile(item.SourceRelativePath));
            }).ToArray(),
            mover);
        var placed = placeNew.Length;
        var skipped = merged.Count - placed;
        var message = skipped == 0
            ? "Imported into the existing album."
            : $"Imported {placed} of {merged.Count} file(s) into the existing album; {skipped} already existed.";
        var checkpoint = CreateCheckpoint(
            import,
            context,
            root,
            importMode,
            Path.GetFullPath(albumFolder),
            message,
            units);
        checkpoint = checkpoint with {
            ImportFileLedger = AcquisitionImportFileLedger.Create(checkpoint, merged),
            DiscardRemainingPayload = discardRemainingPayload || placeNew.Length < merged.Count
        };
        if (!await acquisitions.TryCreateImportPlacementCheckpointAsync(import.Id, checkpoint, cancellationToken)) {
            logger.LogInformation(
                "Existing-album import checkpoint for {Id} was superseded before placement; skipping stale work.",
                import.Id);
            return;
        }

        await context.ReportProgressAsync(40, "Merging into the existing album", cancellationToken);
        var completed = await ImportPlacementExecution.ExecuteAsync(
            acquisitions,
            mover,
            import.Id,
            checkpoint,
            cancellationToken);
        if (completed is null) {
            return;
        }

        await FinalizeImportAsync(
            context,
            import,
            root,
            completed.HintPath,
            ImportPlacementExecution.MediaPaths(completed),
            ImportPlacementExecution.MediaEntityTargets(completed),
            completed.ImportMode,
            completed.SuccessMessage,
            completed.DiscardRemainingPayload,
            cancellationToken);
    }

    private static ImportPlacementCheckpoint CreateCheckpoint(
        AcquisitionImportContext import,
        JobContext context,
        LibraryRootData root,
        ImportMode importMode,
        string albumFolder,
        string successMessage,
        IReadOnlyList<ImportPlacementCheckpointUnit> units) =>
        new(
            import.Kind,
            root.Id,
            Path.GetFullPath(root.Path),
            ImportPlacementExecution.PayloadRootPath(import.ContentPath
                ?? throw new InvalidOperationException("A fresh album import requires its payload path.")),
            importMode,
            Path.GetFullPath(albumFolder),
            Path.GetFullPath(albumFolder),
            successMessage,
            units,
            string.IsNullOrWhiteSpace(import.ClientItemId) ? null : import.ClientItemId,
            Guid.NewGuid(),
            context.Job.Id);

    /// <summary>The shared success tail: hint, final source path, scan chain, torrent handling, and the imported mark.</summary>
    private async Task FinalizeImportAsync(
        JobContext context,
        AcquisitionImportContext import,
        LibraryRootData root,
        string albumFolder,
        IReadOnlyList<string> placedMediaPaths,
        IReadOnlyDictionary<string, Guid> requestedTrackIdsByPath,
        ImportMode importMode,
        string message,
        bool discardRemainingPayload,
        CancellationToken cancellationToken) {
        // The owned quality is the audio-ladder code (and PROPER/REPACK revision) from the selected release.
        // An album is multi-file, so its monitor fulfills on import (no single-file swap); the code is captured
        // for display only.
        var selected = await acquisitions.GetSelectedReleaseAsync(import.Id, cancellationToken);
        var ownedMediaQuality = selected is null ? null : MediaQualityLadder.Detect(import.Kind, selected.Title).Code;
        var ownedMediaRevision = selected is null ? 1 : ReleaseRevisionDetection.Detect(selected.Title);
        var ownedFormatScore = await OwnedFormatScore.ComputeAsync(profiles, import.ProfileId, EntityKind.AudioLibrary, selected, cancellationToken);

        await acquisitions.WriteImportHintAsync(import.Id, albumFolder, import, BookQualityRank.Floor, cancellationToken);
        await acquisitions.SetFinalSourcePathAsync(import.Id, albumFolder, cancellationToken);

        await context.ReportProgressAsync(80, "Cataloging imported album", cancellationToken);
        var materialized = await materializer.MaterializeAsync(
            import.Kind,
            context,
            new ImportedEntityMaterializationRequest(
                import.Id,
                import.EntityId,
                root,
                placedMediaPaths,
                RequestedAudioTrackIdsByPath: requestedTrackIdsByPath),
            cancellationToken);
        await ImportedEntityReconciliation.EnqueueAsync(
            context,
            materialized,
            AcquisitionFinalizeJobPayload.Create(
                import.Id,
                BookQualityRank.Floor,
                message,
                ownedMediaQuality,
                ownedMediaRevision,
                ownedFormatScore,
                materialized.TouchedAncestorIds),
            cancellationToken);

        await torrents.HandleImportedAsync(import, importMode, discardRemainingPayload, cancellationToken);

        await QueueMissingChildFallbackAsync(context, import, cancellationToken);
    }

    private Task Fail(Guid acquisitionId, string message, CancellationToken cancellationToken) =>
        acquisitions.SetStatusAsync(acquisitionId, AcquisitionStatus.Failed, message, cancellationToken);
}
