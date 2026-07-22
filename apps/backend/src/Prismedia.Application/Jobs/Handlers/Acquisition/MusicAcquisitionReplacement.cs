using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Handlers.Scan;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

public sealed partial class MusicAcquisitionImportEngine {
    /// <summary>
    /// Makes an imported structural unit's monitor immediately eligible to fill any provider tracks
    /// that targeted materialization left wanted, rather than waiting for the periodic monitor interval.
    /// </summary>
    private async Task QueueMissingChildFallbackAsync(
        JobContext context,
        AcquisitionImportContext import,
        CancellationToken cancellationToken) {
        if (monitors is null || RequestKindRegistry.FindChildMaterializingUnit(import.Kind) is null) {
            return;
        }

        await monitors.MarkSearchDueByAcquisitionAsync(import.Id, cancellationToken);
        await context.EnqueueIfNeededAsync(
            new EnqueueJobRequest(
                JobType.MonitoredSearch,
                TargetLabel: "Fill missing imported tracks",
                Priority: JobPriorities.RequestEnrichment),
            cancellationToken);
    }

    /// <summary>
    /// The existing album folder to merge into: the album's own on-disk folder when it has one, else a
    /// template-named album folder inside the existing artist folder. Null keeps template placement.
    /// </summary>
    private static string? ExistingAlbumFolderOf(
        AlbumDiskTarget target,
        string artist,
        AcquisitionImportContext import,
        BookImportProfile? profile) {
        if (target.AlbumFolderPath is { } albumFolder && Directory.Exists(albumFolder)) {
            return albumFolder;
        }

        if (target.ArtistFolderPath is { } artistFolder && Directory.Exists(artistFolder)) {
            var albumSegment = MusicImportPlanBuilder
                .AlbumFolderRelative(artist, AlbumTitleOf(import), profile?.PathTemplate, import.Year)
                .Split('/')[^1];
            return Path.Combine(artistFolder, albumSegment);
        }

        return null;
    }

    /// <summary>Album context for either a whole-album acquisition or an individual track fallback.</summary>
    private static string AlbumTitleOf(AcquisitionImportContext import) =>
        import.Kind == EntityKind.AudioTrack && !string.IsNullOrWhiteSpace(import.Series)
            ? import.Series
            : import.Title;

    private async Task<LibraryRootData?> ResolveCheckpointRootAsync(
        ImportPlacementCheckpoint checkpoint,
        CancellationToken cancellationToken) {
        var root = await roots.GetLibraryRootAsync(checkpoint.LibraryRootId, cancellationToken);
        return root is { Enabled: true, ScanAudio: true }
            && FileSystemPathComparison.Equals(
                Path.GetFullPath(root.Path),
                checkpoint.LibraryRootPath)
                ? root
                : null;
    }

    /// <summary>
    /// Stages every file from a reviewed album release into an attempt-scoped sibling folder. Only after
    /// the complete payload is durable does finalization atomically exchange that folder with the owned
    /// album, so a failed download or partial placement never removes the current copy.
    /// </summary>
    private async Task ReplaceExistingAlbumAsync(
        JobContext context,
        AcquisitionImportContext import,
        DownloadPayload payload,
        ImportPlan rawPlan,
        BookImportProfile? profile,
        CancellationToken cancellationToken) {
        if (import.EntityId is not { } entityId
            || await targets.GetAlbumTargetAsync(entityId, cancellationToken) is not { AlbumFolderPath: { } albumFolder }
            || !Directory.Exists(albumFolder)) {
            await Fail(import.Id, "The owned album folder could not be resolved for replacement.", cancellationToken);
            return;
        }

        var root = await ImportRootResolution.ResolveOwningAsync(
            roots,
            albumFolder,
            static candidate => candidate.ScanAudio,
            cancellationToken);
        if (root is null) {
            await Fail(import.Id, "The existing album is outside every enabled audio library root.", cancellationToken);
            return;
        }

        var attemptId = Guid.NewGuid();
        var stageFolder = AlbumReplacementStagePath(albumFolder, attemptId);
        if (Directory.Exists(stageFolder)) {
            throw new IOException($"The album replacement staging folder already exists: '{stageFolder}'.");
        }

        var albumRelative = MusicImportPlanBuilder.AlbumFolderRelative(
            string.IsNullOrWhiteSpace(import.Author) ? "Unknown Artist" : import.Author,
            import.Title,
            profile?.PathTemplate,
            import.Year).Replace('\\', '/').Trim('/');
        var albumPrefix = albumRelative + "/";
        var resolved = rawPlan.Items.Select(item => {
            var target = item.TargetRelativePath.Replace('\\', '/');
            if (!target.StartsWith(albumPrefix, FileSystemPathComparison.Comparison)) {
                throw new InvalidDataException("The album replacement plan escaped its album folder.");
            }

            var relativeInsideAlbum = target[albumPrefix.Length..];
            return new ResolvedImportItem(
                Path.GetFullPath(Path.Combine(payload.ContentRoot, item.SourceRelativePath)),
                Path.GetFullPath(Path.Combine(stageFolder, relativeInsideAlbum)));
        }).ToArray();
        var importMode = profile?.ImportMode ?? ImportMode.Move;
        var units = ImportPlacementExecution.ReserveUnits(
            payload.ContentRoot,
            resolved.Select(item => (item, IsMedia: MusicImportPlanBuilder.IsAudioFile(item.SourceAbsolutePath))).ToArray(),
            mover);
        var checkpoint = new ImportPlacementCheckpoint(
            import.Kind,
            root.Id,
            Path.GetFullPath(root.Path),
            ImportPlacementExecution.PayloadRootPath(import.ContentPath
                ?? throw new InvalidOperationException("A fresh album replacement requires its payload path.")),
            importMode,
            Path.GetFullPath(albumFolder),
            Path.GetFullPath(albumFolder),
            "Replaced the existing album with the reviewed release.",
            units,
            string.IsNullOrWhiteSpace(import.ClientItemId) ? null : import.ClientItemId,
            attemptId,
            context.Job.Id);
        checkpoint = checkpoint with {
            ImportFileLedger = AcquisitionImportFileLedger.Create(checkpoint)
                .WithDecision(AcquisitionImportDecision.ReplaceUpgrade)
        };
        if (!await acquisitions.TryCreateImportPlacementCheckpointAsync(import.Id, checkpoint, cancellationToken)) {
            logger.LogInformation(
                "Album replacement checkpoint for {Id} was superseded before placement; skipping stale work.",
                import.Id);
            return;
        }

        await context.ReportProgressAsync(40, "Staging replacement album", cancellationToken);
        var completed = await ImportPlacementExecution.ExecuteAsync(
            acquisitions,
            mover,
            import.Id,
            checkpoint,
            cancellationToken);
        if (completed is null) {
            return;
        }

        await FinalizeAlbumReplacementAsync(context, import, root, completed, cancellationToken);
    }

    /// <summary>Atomically publishes a staged album, refreshes its catalog, and closes the upgrade child.</summary>
    private async Task FinalizeAlbumReplacementAsync(
        JobContext context,
        AcquisitionImportContext import,
        LibraryRootData root,
        ImportPlacementCheckpoint checkpoint,
        CancellationToken cancellationToken) {
        if (import.UpgradeOfAcquisitionId is not { } parentId) {
            throw new InvalidOperationException("Album replacement finalization requires an upgrade parent.");
        }
        var albumFolder = Path.GetFullPath(checkpoint.FinalSourcePath);
        var stageFolder = AlbumReplacementStagePath(albumFolder, checkpoint.AttemptId);
        var backupFolder = AlbumReplacementBackupPath(albumFolder, checkpoint.AttemptId);
        PublishAlbumReplacement(albumFolder, stageFolder, backupFolder, checkpoint);

        var mediaPaths = checkpoint.Units
            .Where(unit => unit.IsMedia)
            .Select(unit => Path.GetFullPath(Path.Combine(
                albumFolder,
                Path.GetRelativePath(stageFolder, unit.TargetAbsolutePath))))
            .ToArray();
        try {
            await context.ReportProgressAsync(80, "Cataloging replacement album", cancellationToken);
            await materializer.MaterializeAsync(
                import.Kind,
                context,
                new ImportedEntityMaterializationRequest(import.Id, import.EntityId, root, mediaPaths),
                cancellationToken);

            // Catalog materialization succeeded against the new folder. Retire the old folder before
            // publishing terminal DB state; if a later durable step retries, the checkpoint's completed
            // units plus the live album folder unambiguously mean the swap already finished.
            if (Directory.Exists(backupFolder)) {
                Directory.Delete(backupFolder, recursive: true);
            }

            var selected = await acquisitions.GetSelectedReleaseAsync(import.Id, cancellationToken);
            var qualityCode = selected is null
                ? AudioQuality.Unknown.ToCode()
                : MediaQualityLadder.Detect(EntityKind.AudioLibrary, selected.Title).Code;
            var revision = selected is null ? 1 : ReleaseRevisionDetection.Detect(selected.Title);
            var formatScore = await OwnedFormatScore.ComputeAsync(
                profiles,
                import.ProfileId,
                EntityKind.AudioLibrary,
                selected,
                cancellationToken);
            await acquisitions.UpdateOwnedMediaQualityAsync(parentId, qualityCode, revision, formatScore, cancellationToken);
            await history.SafeAddAsync(
                logger,
                new AcquisitionHistoryEntry(
                    parentId,
                    import.EntityId,
                    EntityKind.AudioLibrary,
                    AcquisitionHistoryEvent.Upgraded,
                    import.Title,
                    selected?.Title,
                    QualityCode: qualityCode,
                    FormatScore: formatScore,
                    Message: checkpoint.SuccessMessage),
                CancellationToken.None);
            if (monitors is not null) {
                await monitors.ResolveUpgradeChildAsync(import.Id, succeeded: true, cancellationToken);
            }
            await ImportRootResolution.EnqueueReconciliationAsync(
                context,
                JobType.ScanAudio,
                root,
                "Replaced album scan",
                logger,
                cancellationToken);
            await torrents.HandleImportedAsync(import, checkpoint.ImportMode, cancellationToken);
            await acquisitions.DeleteAsync(import.Id, cancellationToken);
        } catch {
            RestoreAlbumReplacement(albumFolder, backupFolder);
            throw;
        }
    }

    private static bool AlbumReplacementAlreadySwapped(ImportPlacementCheckpoint checkpoint) {
        var albumFolder = Path.GetFullPath(checkpoint.FinalSourcePath);
        var stageFolder = AlbumReplacementStagePath(albumFolder, checkpoint.AttemptId);
        var backupFolder = AlbumReplacementBackupPath(albumFolder, checkpoint.AttemptId);
        return Directory.Exists(albumFolder)
            && !Directory.Exists(stageFolder)
            && (Directory.Exists(backupFolder) || checkpoint.Units.All(unit => unit.FinalPath is not null));
    }

    private static void PublishAlbumReplacement(
        string albumFolder,
        string stageFolder,
        string backupFolder,
        ImportPlacementCheckpoint checkpoint) {
        if (AlbumReplacementAlreadySwapped(checkpoint)) {
            return;
        }
        if (Directory.Exists(backupFolder) && !Directory.Exists(albumFolder) && Directory.Exists(stageFolder)) {
            Directory.Move(stageFolder, albumFolder);
            return;
        }
        if (!Directory.Exists(albumFolder) || !Directory.Exists(stageFolder) || Directory.Exists(backupFolder)) {
            throw new IOException("The album replacement folders are not in a recoverable state.");
        }

        Directory.Move(albumFolder, backupFolder);
        try {
            Directory.Move(stageFolder, albumFolder);
        } catch {
            Directory.Move(backupFolder, albumFolder);
            throw;
        }
    }

    private static void RestoreAlbumReplacement(string albumFolder, string backupFolder) {
        if (!Directory.Exists(backupFolder)) {
            return;
        }
        if (Directory.Exists(albumFolder)) {
            Directory.Delete(albumFolder, recursive: true);
        }
        Directory.Move(backupFolder, albumFolder);
    }

    private static string AlbumReplacementStagePath(string albumFolder, Guid attemptId) =>
        Path.GetFullPath(albumFolder) + $".prismedia-new-{attemptId:N}";

    private static string AlbumReplacementBackupPath(string albumFolder, Guid attemptId) =>
        Path.GetFullPath(albumFolder) + $".prismedia-bak-{attemptId:N}";
}
