using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Handlers.Scan;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

public sealed partial class BookAcquisitionImportEngine {
    private static bool IsAudiobookReplacement(AcquisitionImportContext import) =>
        import.UpgradeOfAcquisitionId is not null
        && import.BookRendition == BookRendition.Audiobook;

    /// <summary>
    /// Stages every chapter in a reviewed audiobook beside the owned folder. Non-audio files already in
    /// that folder (for example an EPUB rendition or cover art) are retained in the staged copy. Publishing
    /// then exchanges the complete folder in one rename, so readers never observe a half-replaced audiobook.
    /// </summary>
    private async Task ReplaceExistingAudiobookAsync(
        JobContext context,
        AcquisitionImportContext import,
        BookImportProfile profile,
        LibraryRootData root,
        UpgradeReplaceTarget target,
        ResolvedImportPlan plan,
        CancellationToken cancellationToken) {
        if (target.ParentFinalSourcePath is not { } ownedFolder || !Directory.Exists(ownedFolder)) {
            await Fail(import.Id, "The owned audiobook folder could not be resolved for replacement.", cancellationToken);
            return;
        }

        var attemptId = Guid.NewGuid();
        var stageFolder = AudiobookReplacementStagePath(ownedFolder, attemptId);
        if (Directory.Exists(stageFolder)) {
            throw new IOException($"The audiobook replacement staging folder already exists: '{stageFolder}'.");
        }

        var stagedItems = plan.Items
            .Select(item => new ResolvedImportItem(
                item.SourceAbsolutePath,
                Path.Combine(stageFolder, Path.GetFileName(item.TargetAbsolutePath))))
            .ToArray();
        var units = ImportPlacementExecution.ReserveUnits(
            import.ContentPath
                ?? throw new InvalidOperationException("A fresh audiobook replacement requires its payload path."),
            stagedItems.Select(item => (item, IsMedia: true)).ToArray(),
            mover);
        var checkpoint = new ImportPlacementCheckpoint(
            import.Kind,
            root.Id,
            Path.GetFullPath(root.Path),
            ImportPlacementExecution.PayloadRootPath(import.ContentPath),
            profile.ImportMode,
            Path.GetFullPath(ownedFolder),
            Path.GetFullPath(ownedFolder),
            "Replaced the existing audiobook with the reviewed release.",
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
                "Audiobook replacement checkpoint for {Id} was superseded before placement; skipping stale work.",
                import.Id);
            return;
        }

        await context.ReportProgressAsync(40, "Staging replacement audiobook", cancellationToken);
        var completed = await ImportPlacementExecution.ExecuteAsync(
            acquisitions,
            mover,
            import.Id,
            checkpoint,
            cancellationToken);
        if (completed is null) {
            return;
        }

        await FinalizeAudiobookReplacementAsync(
            context,
            import,
            profile,
            root,
            completed,
            cancellationToken);
    }

    private async Task FinalizeAudiobookReplacementAsync(
        JobContext context,
        AcquisitionImportContext import,
        BookImportProfile profile,
        LibraryRootData root,
        ImportPlacementCheckpoint checkpoint,
        CancellationToken cancellationToken) {
        if (import.UpgradeOfAcquisitionId is not { } parentId) {
            throw new InvalidOperationException("Audiobook replacement finalization requires an upgrade parent.");
        }

        var ownedFolder = Path.GetFullPath(checkpoint.FinalSourcePath);
        var stageFolder = AudiobookReplacementStagePath(ownedFolder, checkpoint.AttemptId);
        var backupFolder = AudiobookReplacementBackupPath(ownedFolder, checkpoint.AttemptId);
        if (!AudiobookReplacementAlreadySwapped(checkpoint)) {
            var retainedSourceFolder = Directory.Exists(ownedFolder)
                ? ownedFolder
                : backupFolder;
            CopyRetainedBookFiles(retainedSourceFolder, stageFolder);
        }
        var previousAudioPaths = Directory.Exists(backupFolder)
            ? SupportedAudiobookPaths(backupFolder)
                .Select(path => Path.GetFullPath(Path.Combine(
                    ownedFolder,
                    Path.GetRelativePath(backupFolder, path))))
                .ToArray()
            : SupportedAudiobookPaths(ownedFolder);
        PublishAudiobookReplacement(ownedFolder, stageFolder, backupFolder, checkpoint);

        var finalPaths = checkpoint.Units
            .Where(unit => unit.IsMedia)
            .Select(unit => Path.GetFullPath(Path.Combine(
                ownedFolder,
                Path.GetRelativePath(stageFolder, unit.TargetAbsolutePath))))
            .ToArray();
        var finalPathSet = finalPaths.ToHashSet(FileSystemPathComparison.Comparer);
        var removedPaths = previousAudioPaths
            .Where(path => !finalPathSet.Contains(path))
            .ToArray();
        try {
            var selected = await acquisitions.GetSelectedReleaseAsync(import.Id, cancellationToken);
            var detectedSource = selected is null
                ? BookSourceTier.Unknown
                : BookFormatDetection.DetectSource(selected.Title);
            var target = await acquisitions.GetUpgradeReplaceTargetAsync(import.Id, cancellationToken);
            var preserveOwnedSource = selected?.ManualPick == true
                && detectedSource == BookSourceTier.Unknown
                && target is not null;
            var ownedQuality = new BookQualityRank(
                preserveOwnedSource ? target!.ParentOwnedQuality.Source : detectedSource,
                BookFormatTier.Unknown);
            var ownedFormatScore = await OwnedFormatScore.ComputeAsync(
                profiles,
                import.ProfileId,
                EntityKind.Book,
                selected,
                cancellationToken);

            await acquisitions.WriteImportHintAsync(
                import.Id,
                ownedFolder,
                import,
                ownedQuality,
                cancellationToken);
            await context.ReportProgressAsync(80, "Cataloging replacement audiobook", cancellationToken);
            var materialized = await materializer.MaterializeAsync(
                import.Kind,
                context,
                new ImportedEntityMaterializationRequest(
                    import.Id,
                    import.EntityId,
                    root,
                    finalPaths,
                    ReplacedSourcePaths: previousAudioPaths.Where(finalPathSet.Contains).ToArray(),
                    RemovedSourcePaths: removedPaths),
                cancellationToken);

            await acquisitions.UpdateOwnedQualityAsync(parentId, ownedQuality, cancellationToken);
            await history.SafeAddAsync(
                logger,
                new AcquisitionHistoryEntry(
                    parentId,
                    import.EntityId,
                    EntityKind.Book,
                    AcquisitionHistoryEvent.Upgraded,
                    import.Title,
                    selected?.Title,
                    selected?.IndexerName,
                    QualityCode: $"{ownedQuality.Source.ToCode()}/{ownedQuality.Format.ToCode()}",
                    FormatScore: ownedFormatScore,
                    Message: checkpoint.SuccessMessage),
                CancellationToken.None);
            await torrents.HandleImportedAsync(
                import,
                checkpoint.ImportMode,
                checkpoint.DiscardRemainingPayload,
                cancellationToken);
            await ImportedEntityReconciliation.EnqueueAsync(
                context,
                materialized,
                AcquisitionFinalizeJobPayload.CreateUpgrade(
                    import.Id,
                    parentId,
                    checkpoint.SuccessMessage,
                    backupFolder,
                    materialized.TouchedAncestorIds),
                cancellationToken);
        } catch {
            RestoreAudiobookReplacement(ownedFolder, stageFolder, backupFolder);
            throw;
        }
    }

    private static IReadOnlyList<string> SupportedAudiobookPaths(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(path => SupportedExtensions.Audiobook.Contains(Path.GetExtension(path)))
                .Select(Path.GetFullPath)
                .ToArray()
            : [];

    private static void CopyRetainedBookFiles(string ownedFolder, string stageFolder) {
        foreach (var source in Directory.EnumerateFiles(ownedFolder, "*", SearchOption.AllDirectories)) {
            if (SupportedExtensions.Audiobook.Contains(Path.GetExtension(source))) {
                continue;
            }

            var relative = Path.GetRelativePath(ownedFolder, source);
            var target = Path.GetFullPath(Path.Combine(stageFolder, relative));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }
    }

    private static bool AudiobookReplacementAlreadySwapped(ImportPlacementCheckpoint checkpoint) {
        var ownedFolder = Path.GetFullPath(checkpoint.FinalSourcePath);
        var stageFolder = AudiobookReplacementStagePath(ownedFolder, checkpoint.AttemptId);
        var backupFolder = AudiobookReplacementBackupPath(ownedFolder, checkpoint.AttemptId);
        return Directory.Exists(ownedFolder)
            && !Directory.Exists(stageFolder)
            && Directory.Exists(backupFolder);
    }

    private static void PublishAudiobookReplacement(
        string ownedFolder,
        string stageFolder,
        string backupFolder,
        ImportPlacementCheckpoint checkpoint) {
        if (AudiobookReplacementAlreadySwapped(checkpoint)) {
            return;
        }
        if (Directory.Exists(backupFolder) && !Directory.Exists(ownedFolder) && Directory.Exists(stageFolder)) {
            Directory.Move(stageFolder, ownedFolder);
            return;
        }
        if (!Directory.Exists(ownedFolder) || !Directory.Exists(stageFolder) || Directory.Exists(backupFolder)) {
            throw new IOException("The audiobook replacement folders are not in a recoverable state.");
        }

        Directory.Move(ownedFolder, backupFolder);
        try {
            Directory.Move(stageFolder, ownedFolder);
        } catch {
            Directory.Move(backupFolder, ownedFolder);
            throw;
        }
    }

    private static void RestoreAudiobookReplacement(
        string ownedFolder,
        string stageFolder,
        string backupFolder) {
        if (!Directory.Exists(backupFolder)) {
            return;
        }
        if (Directory.Exists(ownedFolder)) {
            if (Directory.Exists(stageFolder)) {
                Directory.Delete(stageFolder, recursive: true);
            }
            Directory.Move(ownedFolder, stageFolder);
        }
        Directory.Move(backupFolder, ownedFolder);
    }

    private static string AudiobookReplacementStagePath(string ownedFolder, Guid attemptId) =>
        Path.GetFullPath(ownedFolder) + $".prismedia-new-{attemptId:N}";

    private static string AudiobookReplacementBackupPath(string ownedFolder, Guid attemptId) =>
        Path.GetFullPath(ownedFolder) + $".prismedia-bak-{attemptId:N}";
}
