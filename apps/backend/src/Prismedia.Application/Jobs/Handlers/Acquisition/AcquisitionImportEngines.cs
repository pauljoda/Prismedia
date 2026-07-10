using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Jobs.Scanning;
using Prismedia.Application.Jobs.Handlers.Scan;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Imports a completed download for one media kind: places the payload's files into the right library
/// root, writes the path-keyed identify hint, enqueues the kind's scan, and finalizes the acquisition.
/// Resolved per <see cref="Kind"/> through <see cref="IAcquisitionImportEngineFactory"/>, mirroring the
/// decision-engine factory, so adding a medium's import never touches the job handler.
/// </summary>
public interface IAcquisitionImportEngine {
    /// <summary>The media kind this engine imports.</summary>
    EntityKind Kind { get; }

    /// <summary>
    /// Runs the import for a completed download. The engine owns the terminal status transitions
    /// (Imported / ManualImportRequired / Failed-with-reason); the caller has already set Importing and
    /// handles unexpected exceptions.
    /// </summary>
    Task ImportAsync(JobContext context, AcquisitionImportContext import, CancellationToken cancellationToken);
}

/// <summary>Resolves the import engine for a media kind, or null when that kind has no import path yet.</summary>
public interface IAcquisitionImportEngineFactory {
    IAcquisitionImportEngine? Find(EntityKind kind);
}

/// <summary>Dispatches to the registered <see cref="IAcquisitionImportEngine"/> for a kind (one engine per kind).</summary>
public sealed class AcquisitionImportEngineFactory(IEnumerable<IAcquisitionImportEngine> engines) : IAcquisitionImportEngineFactory {
    private readonly IReadOnlyDictionary<EntityKind, IAcquisitionImportEngine> _byKind =
        engines.ToDictionary(engine => engine.Kind);

    public IAcquisitionImportEngine? Find(EntityKind kind) => _byKind.GetValueOrDefault(kind);
}

/// <summary>
/// Shared crash-safe placement protocol for non-TV imports. Every exact target is collision-resolved
/// and persisted before mutation. Each completed unit is then compare-and-swapped independently, so a
/// retry can adopt a target published just before a crash without replanning or suffixing it.
/// </summary>
internal static class ImportPlacementExecution {
    public static string PayloadRootPath(string contentPath) =>
        Directory.Exists(contentPath)
            ? Path.GetFullPath(contentPath)
            : Path.GetDirectoryName(Path.GetFullPath(contentPath))
                ?? throw new InvalidDataException("The download payload has no parent directory.");

    public static IReadOnlyList<ImportPlacementCheckpointUnit> ReserveUnits(
        string contentPath,
        IReadOnlyList<(ResolvedImportItem Item, bool IsMedia)> items,
        IImportFileMover mover) {
        var contentRoot = PayloadRootPath(contentPath);
        var reservedTargets = new List<string>(items.Count);
        var units = new List<ImportPlacementCheckpointUnit>(items.Count);
        foreach (var (item, isMedia) in items) {
            var source = Path.GetFullPath(item.SourceAbsolutePath);
            var relative = Path.GetRelativePath(contentRoot, source);
            if (Path.IsPathFullyQualified(relative)
                || relative.Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == "..")) {
                throw new InvalidDataException("An import source escaped the completed download boundary.");
            }

            var target = mover.ResolveExactTargetPath(
                Path.GetFullPath(item.TargetAbsolutePath),
                reservedTargets);
            reservedTargets.Add(target);
            units.Add(new ImportPlacementCheckpointUnit(
                relative,
                source,
                target,
                isMedia));
        }

        return units;
    }

    public static bool MatchesTransfer(
        ImportPlacementCheckpoint checkpoint,
        AcquisitionImportContext import) =>
        string.Equals(
            NormalizeClientItemId(checkpoint.TransferClientItemId),
            NormalizeClientItemId(import.ClientItemId),
            StringComparison.Ordinal);

    /// <summary>
    /// Completes pending units. Null means a concurrent lifecycle action superseded the attempt; callers
    /// must exit without writing any further acquisition state.
    /// </summary>
    public static async Task<ImportPlacementCheckpoint?> ExecuteAsync(
        IAcquisitionStore acquisitions,
        IImportFileMover mover,
        Guid acquisitionId,
        ImportPlacementCheckpoint checkpoint,
        CancellationToken cancellationToken) {
        if (!await acquisitions.IsCurrentImportPlacementCheckpointAsync(
                acquisitionId,
                checkpoint,
                cancellationToken)) {
            return null;
        }

        var current = checkpoint;
        for (var index = 0; index < current.Units.Count; index++) {
            var unit = current.Units[index];
            if (unit.FinalPath is not null) {
                if (!File.Exists(unit.TargetAbsolutePath)) {
                    throw new IOException(
                        $"The checkpointed import target '{unit.TargetAbsolutePath}' is missing; recovery stopped without replanning.");
                }
                continue;
            }

            var sourceExists = File.Exists(unit.SourceAbsolutePath);
            var targetExists = File.Exists(unit.TargetAbsolutePath);
            if (!sourceExists && !targetExists) {
                throw new FileNotFoundException(
                    "Neither the original payload file nor its reserved import target exists.",
                    unit.SourceAbsolutePath);
            }

            if (sourceExists && targetExists && !FilesHaveSameContent(unit.SourceAbsolutePath, unit.TargetAbsolutePath)) {
                throw new IOException(
                    $"The reserved import target '{unit.TargetAbsolutePath}' contains different bytes; recovery refused to overwrite or suffix it.");
            }

            if (sourceExists && !targetExists) {
                var placed = await mover.PlaceExactAsync(
                    new ResolvedImportItem(unit.SourceAbsolutePath, unit.TargetAbsolutePath),
                    current.ImportMode,
                    cancellationToken);
                if (!PathEquals(placed, unit.TargetAbsolutePath)) {
                    throw new IOException("The import mover did not publish the exact checkpointed target.");
                }
            }

            // target-only is the Move crash window; source+same-target is the Copy/Hardlink window.
            // Both represent the exact reserved bytes and are safe to adopt without a second placement.
            var advancedUnits = current.Units.ToArray();
            advancedUnits[index] = unit with { FinalPath = unit.TargetAbsolutePath };
            var advanced = current with { Units = advancedUnits };
            if (!await acquisitions.TryAdvanceImportPlacementCheckpointAsync(
                    acquisitionId,
                    current,
                    advanced,
                    cancellationToken)) {
                return null;
            }
            current = advanced;
        }

        return current;
    }

    public static IReadOnlyList<string> MediaPaths(ImportPlacementCheckpoint checkpoint) =>
        checkpoint.Units
            .Where(unit => unit.IsMedia)
            .Select(unit => unit.FinalPath
                ?? throw new InvalidOperationException("A pending placement cannot be materialized."))
            .ToArray();

    private static bool FilesHaveSameContent(string firstPath, string secondPath) {
        var first = new FileInfo(firstPath);
        var second = new FileInfo(secondPath);
        if (!first.Exists || !second.Exists || first.Length != second.Length) {
            return false;
        }

        using var firstStream = first.OpenRead();
        using var secondStream = second.OpenRead();
        return SHA256.HashData(firstStream).AsSpan().SequenceEqual(SHA256.HashData(secondStream));
    }

    private static string? NormalizeClientItemId(string? clientItemId) =>
        string.IsNullOrWhiteSpace(clientItemId) ? null : clientItemId;

    private static bool PathEquals(string first, string second) =>
        FileSystemPathComparison.Equals(Path.GetFullPath(first), Path.GetFullPath(second));
}

/// <summary>
/// Ends or hands off an imported acquisition's life in the download client. A move-mode import removes
/// the torrent (and its data) — the payload left the download dir, so it cannot seed. A hardlink/copy
/// import instead puts the transfer under seeding watch when a seed goal was captured at grab time; the
/// monitor removes it once the goal is met. Shared by every import engine; a cleanup failure never
/// fails the import — the media is already in the library.
/// </summary>
public sealed class ImportedTorrentRemover(
    IAcquisitionStore acquisitions,
    IDownloadClientConfigStore downloadClients,
    IDownloadClientFactory clients,
    ILogger<ImportedTorrentRemover> logger) {
    /// <summary>Move → remove now; hardlink/copy → seeding watch (or leave to the client's own rules when no goal is set).</summary>
    public async Task HandleImportedAsync(AcquisitionImportContext import, ImportMode mode, CancellationToken cancellationToken) {
        if (mode == ImportMode.Move) {
            await RemoveAsync(import, cancellationToken);
            return;
        }

        try {
            if (await acquisitions.MarkTransferSeedingAsync(import.Id, DateTimeOffset.UtcNow, cancellationToken)) {
                logger.LogDebug("AcquisitionImport: acquisition {Id} handed to seeding watch.", import.Id);
            }
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logger.LogWarning(ex, "AcquisitionImport: failed to start seeding watch for acquisition {Id}", import.Id);
        }
    }

    public async Task RemoveAsync(AcquisitionImportContext import, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(import.ClientItemId)) {
            return;
        }

        var client = import.DownloadClientConfigId is { } id
            ? await downloadClients.GetAsync(id, cancellationToken) ?? await downloadClients.GetDefaultAsync(cancellationToken)
            : await downloadClients.GetDefaultAsync(cancellationToken);
        if (client is null) {
            return;
        }

        try {
            var connection = new DownloadClientConnection(client.Id, client.Kind, client.BaseUrl, client.Username, client.Password, client.Category, client.ApiKey);
            await clients.Get(client.Kind).RemoveAsync(connection, import.ClientItemId, deleteData: true, cancellationToken);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            // The media is already imported; a failure cleaning up the torrent should not fail the import.
            logger.LogWarning(ex, "AcquisitionImport: failed to remove torrent for acquisition {Id}", import.Id);
        }
    }
}

/// <summary>
/// Book import engine: the profile-driven flow — plan supported book files against the profile's target
/// root and path template, move them, capture the owned quality for the upgrade loop, write the identify
/// hint, and chain a book scan. Ambiguous payloads stop at manual-import-required instead of guessing.
/// </summary>
public sealed class BookAcquisitionImportEngine(
    IAcquisitionStore acquisitions,
    IBookAcquisitionProfileStore profiles,
    ILibraryScanRootPersistence roots,
    IAcquisitionImportPlanner planner,
    IImportFileMover mover,
    IImportedEntityMaterializer materializer,
    ImportedTorrentRemover torrents,
    ILogger<BookAcquisitionImportEngine> logger) : IAcquisitionImportEngine {
    public EntityKind Kind => EntityKind.Book;

    public async Task ImportAsync(JobContext context, AcquisitionImportContext import, CancellationToken cancellationToken) {
        var profile = await profiles.GetImportProfileAsync(import.ProfileId, EntityKind.Book, cancellationToken);

        if (import.ImportPlacementCheckpoint is { } durableCheckpoint) {
            var checkpointRoot = await ResolveCheckpointRootAsync(durableCheckpoint, cancellationToken);
            if (checkpointRoot is null) {
                await acquisitions.SetStatusAsync(
                    import.Id,
                    AcquisitionStatus.ManualImportRequired,
                    "The saved book import targets a library root that moved, was disabled, or no longer accepts books. Review the partial import before retrying.",
                    cancellationToken);
                return;
            }
            if (!ImportPlacementExecution.MatchesTransfer(durableCheckpoint, import)) {
                await acquisitions.SetStatusAsync(
                    import.Id,
                    AcquisitionStatus.ManualImportRequired,
                    "This book import checkpoint belongs to a different download attempt and was not reused. Review the partial files before retrying.",
                    cancellationToken);
                return;
            }

            var resumed = await ImportPlacementExecution.ExecuteAsync(
                acquisitions,
                mover,
                import.Id,
                durableCheckpoint,
                cancellationToken);
            if (resumed is null) {
                return;
            }

            profile ??= new BookImportProfile(
                checkpointRoot.Id,
                MediaNamingTemplates.BookDefault,
                durableCheckpoint.ImportMode);
            await FinalizeAsync(context, import, profile, checkpointRoot, resumed, cancellationToken);
            return;
        }

        // A request-time library choice overrides the profile's target; an unsuitable choice falls back.
        var root = await ImportRootResolution.ResolveAsync(
            roots, import.TargetLibraryRootId, profile?.TargetLibraryRootId, static candidate => candidate.ScanBooks, cancellationToken);
        if (root is null) {
            await Fail(import.Id, "The target library root is missing or not book-enabled.", cancellationToken);
            return;
        }

        // No profile configured: degrade to the defaults the request UI promises ("permissive defaults
        // apply") — the resolved library, the default naming template, and a move import — matching how
        // the movie/TV/music engines already behave instead of failing the import.
        profile ??= new BookImportProfile(root.Id, MediaNamingTemplates.BookDefault, ImportMode.Move);

        if (string.IsNullOrWhiteSpace(import.ContentPath)) {
            await Fail(import.Id, "The completed download reported no content path.", cancellationToken);
            return;
        }

        var templateContext = new ImportTemplateContext(import.Title, import.Author, import.Year);
        var plan = await planner.PlanAsync(import.ContentPath, root.Path, profile, templateContext, cancellationToken);
        if (plan.Blocked) {
            await acquisitions.SetStatusAsync(import.Id, AcquisitionStatus.ManualImportRequired, BlockMessage(plan.BlockReason), cancellationToken);
            return;
        }

        var units = ImportPlacementExecution.ReserveUnits(
            import.ContentPath,
            plan.Items.Select(item => (item, IsMedia: true)).ToArray(),
            mover);
        var hintFolder = Path.GetDirectoryName(units[0].TargetAbsolutePath) ?? Path.GetFullPath(root.Path);
        var checkpoint = new ImportPlacementCheckpoint(
            import.Kind,
            root.Id,
            Path.GetFullPath(root.Path),
            ImportPlacementExecution.PayloadRootPath(import.ContentPath),
            profile.ImportMode,
            hintFolder,
            hintFolder,
            "Imported into the library.",
            units,
            string.IsNullOrWhiteSpace(import.ClientItemId) ? null : import.ClientItemId,
            Guid.NewGuid(),
            context.Job.Id);
        if (!await acquisitions.TryCreateImportPlacementCheckpointAsync(import.Id, checkpoint, cancellationToken)) {
            logger.LogInformation(
                "Book import checkpoint for {Id} was superseded before placement; skipping stale work.",
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

        await FinalizeAsync(context, import, profile, root, completed, cancellationToken);
    }

    private async Task FinalizeAsync(
        JobContext context,
        AcquisitionImportContext import,
        BookImportProfile profile,
        LibraryRootData root,
        ImportPlacementCheckpoint checkpoint,
        CancellationToken cancellationToken) {
        var finalPaths = ImportPlacementExecution.MediaPaths(checkpoint);

        // Capture the owned quality: format from the actual placed file (file truth, so a release that
        // claimed retail-EPUB but delivered a PDF is recorded honestly), source from the selected release
        // title (provenance is not in the bytes). Stamped onto the acquisition and carried on the hint so
        // the scan can record it on the book's detail row.
        var selected = await acquisitions.GetSelectedReleaseAsync(import.Id, cancellationToken);
        var ownedQuality = new BookQualityRank(
            selected is null ? BookSourceTier.Unknown : BookFormatDetection.DetectSource(selected.Title),
            BookFormatDetection.FormatTierFromExtension(finalPaths[0]));
        // The owned custom-format score is the selected release scored against this profile's formats, so the
        // upgrade loop's same-quality format-score cutoff has a baseline. Null-safe: no selected release → 0.
        var ownedFormatScore = await OwnedFormatScore.ComputeAsync(profiles, import.ProfileId, EntityKind.Book, selected, cancellationToken);

        await acquisitions.WriteImportHintAsync(import.Id, checkpoint.HintPath, import, ownedQuality, cancellationToken);
        await acquisitions.SetFinalSourcePathAsync(import.Id, checkpoint.FinalSourcePath, cancellationToken);

        await context.ReportProgressAsync(80, "Cataloging imported book", cancellationToken);
        await materializer.MaterializeAsync(
            import.Kind,
            context,
            new ImportedEntityMaterializationRequest(import.Id, import.EntityId, root, finalPaths),
            cancellationToken);
        await ImportRootResolution.EnqueueReconciliationAsync(
            context, JobType.ScanBook, root, "Imported book scan", logger, cancellationToken);

        await torrents.HandleImportedAsync(import, checkpoint.ImportMode, cancellationToken);

        await acquisitions.MarkImportedWithQualityAsync(import.Id, ownedQuality, checkpoint.SuccessMessage, cancellationToken, ownedFormatScore: ownedFormatScore);
    }

    private async Task<LibraryRootData?> ResolveCheckpointRootAsync(
        ImportPlacementCheckpoint checkpoint,
        CancellationToken cancellationToken) {
        var root = await roots.GetLibraryRootAsync(checkpoint.LibraryRootId, cancellationToken);
        return root is { Enabled: true, ScanBooks: true }
            && FileSystemPathComparison.Equals(
                Path.GetFullPath(root.Path),
                checkpoint.LibraryRootPath)
                ? root
                : null;
    }

    private Task Fail(Guid acquisitionId, string message, CancellationToken cancellationToken) =>
        acquisitions.SetStatusAsync(acquisitionId, AcquisitionStatus.Failed, message, cancellationToken);

    private static string BlockMessage(ImportBlockReason? reason) => reason switch {
        ImportBlockReason.NoSupportedPayload => "The download contains no supported book files (CBZ, ZIP, EPUB, PDF).",
        ImportBlockReason.AmbiguousMultiplePrimaries => "The download contains multiple books; import the right one manually.",
        ImportBlockReason.MixedPayload => "The download mixes a book file with comic archives; import manually.",
        _ => "The download could not be imported automatically."
    };
}

/// <summary>
/// Shared import-time custom-format scoring: scores the selected release against the acquisition's
/// profile so the owned format score can be captured for the upgrade loop's format-score cutoff. Null-safe
/// on every axis — no selected release, or a profile whose rules carry no formats, scores 0.
/// </summary>
internal static class OwnedFormatScore {
    public static async Task<int> ComputeAsync(
        IBookAcquisitionProfileStore profiles,
        Guid? profileId,
        EntityKind kind,
        SelectedRelease? selected,
        CancellationToken cancellationToken) {
        if (selected is null) {
            return 0;
        }

        var rules = await profiles.GetRulesAsync(profileId, kind, cancellationToken);
        return CustomFormatEvaluation.Score(selected.Title, rules);
    }
}

/// <summary>
/// Shared import-target resolution: the request-time library choice wins when it exists and supports
/// the kind, then the profile's target, then the first suitable enabled root (never an NSFW one ahead
/// of a regular one). An unsuitable explicit choice degrades to the next tier instead of failing.
/// </summary>
/// <summary>
/// Shared execution pieces for imports merging into an existing on-disk target (TV, movies, music):
/// the stage-and-swap around the owned-file replacer, and the all-dropped terminal outcome
/// (manual-import hold for format-change upgrades; blocklist + fail when nothing was better).
/// </summary>
internal static class MergedImportExecution {
    /// <summary>
    /// Swaps one owned file for its strictly-better incoming counterpart. Move imports hand the payload
    /// file straight to the replacer; Copy/Hardlink imports stage a copy in a dot-folder scratch beside
    /// the owned file first, so the seeding payload is never consumed. Returns the swapped path, or null
    /// when the replace failed (the owned file is intact; the incoming file is dropped).
    /// </summary>
    public static async Task<string?> ReplaceOwnedAsync(
        IOwnedFileReplacer replacer,
        ILogger logger,
        string ownedFilePath,
        string sourceAbsolute,
        ImportMode importMode,
        CancellationToken cancellationToken,
        bool allowFormatChange = false,
        bool retainBackup = false,
        string? retainedBackupPath = null,
        string? incomingEvidencePath = null) {
        var incoming = sourceAbsolute;
        string? scratchDir = null;
        if (importMode != ImportMode.Move) {
            scratchDir = Path.Combine(Path.GetDirectoryName(ownedFilePath)!, ".prismedia-incoming");
            Directory.CreateDirectory(scratchDir);
            incoming = Path.Combine(scratchDir, Path.GetFileName(sourceAbsolute));
            File.Copy(sourceAbsolute, incoming, overwrite: true);
        }

        try {
            var result = retainBackup
                ? await replacer.ReplaceRetainingBackupAsync(
                    ownedFilePath,
                    incoming,
                    BookFormatTier.Unknown,
                    cancellationToken,
                    EntityKind.Video,
                    allowFormatChange,
                    retainedBackupPath,
                    incomingEvidencePath)
                : await replacer.ReplaceAsync(
                    ownedFilePath, incoming, BookFormatTier.Unknown, cancellationToken, EntityKind.Video, allowFormatChange);
            if (!result.Succeeded) {
                logger.LogWarning("MergedImport: in-place replace of {Owned} failed: {Reason}", ownedFilePath, result.FailureReason);
            }

            return result.Succeeded ? result.SwappedPath : null;
        } finally {
            if (scratchDir is not null) {
                try {
                    Directory.Delete(scratchDir, recursive: true);
                } catch (Exception ex) when (ex is not OperationCanceledException) {
                    logger.LogDebug(ex, "MergedImport: could not clean the replace scratch folder {Scratch}.", scratchDir);
                }
            }
        }
    }

    /// <summary>
    /// The all-dropped outcome: an upgrade that only changes file formats is held for manual import (the
    /// release has real value); a payload with nothing better than the owned files is blocklisted so it
    /// is never grabbed again, its torrent discarded, and the acquisition failed with an honest message.
    /// </summary>
    public static async Task FailNothingUsableAsync(
        IAcquisitionStore acquisitions,
        IAcquisitionBlocklistStore blocklist,
        IAcquisitionHistoryStore history,
        ImportedTorrentRemover torrents,
        ILogger logger,
        AcquisitionImportContext import,
        SelectedRelease? selected,
        bool hasFormatChange,
        string formatChangeMessage,
        CancellationToken cancellationToken) {
        if (hasFormatChange) {
            await acquisitions.SetStatusAsync(import.Id, AcquisitionStatus.ManualImportRequired, formatChangeMessage, cancellationToken);
            return;
        }

        const string message = "Nothing in the release was better than the files you already have; the release was blocklisted.";
        if (selected is not null) {
            await blocklist.AddAsync(
                new BlocklistAddRequest(
                    selected.Identity, BlocklistReason.NoImportableFiles, selected.Title, selected.IndexerName,
                    selected.InfoHash, import.Id, message),
                cancellationToken);
            await history.SafeAddAsync(logger, new AcquisitionHistoryEntry(
                import.Id, import.EntityId, import.Kind, AcquisitionHistoryEvent.Blocklisted, import.Title,
                selected.Title, selected.IndexerName,
                Message: $"Blocklisted ({BlocklistReason.NoImportableFiles.ToCode()})."), cancellationToken);
        }

        await torrents.RemoveAsync(import, cancellationToken);
        await history.SafeAddAsync(logger, new AcquisitionHistoryEntry(
            import.Id, import.EntityId, import.Kind, AcquisitionHistoryEvent.ImportFailed, import.Title,
            selected?.Title, selected?.IndexerName, Message: message), cancellationToken);
        await acquisitions.SetStatusAsync(
            import.Id, AcquisitionStatus.Failed,
            selected is null ? "Nothing in the release was better than the files you already have." : message,
            cancellationToken);
    }
}

internal static class ImportRootResolution {
    public static async Task<LibraryRootData?> ResolveAsync(
        ILibraryScanRootPersistence roots,
        Guid? requestedRootId,
        Guid? profileRootId,
        Func<LibraryRootData, bool> supportsKind,
        CancellationToken cancellationToken) {
        foreach (var rootId in new[] { requestedRootId, profileRootId }) {
            if (rootId is not { } id) {
                continue;
            }

            var chosen = await roots.GetLibraryRootAsync(id, cancellationToken);
            if (chosen is { Enabled: true } && supportsKind(chosen)) {
                return chosen;
            }
        }

        return (await roots.GetEnabledRootsAsync(cancellationToken))
            .Where(supportsKind)
            .OrderBy(candidate => candidate.IsNsfw)
            .ThenBy(candidate => candidate.Label, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public static async Task<LibraryRootData?> ResolveOwningAsync(
        ILibraryScanRootPersistence roots,
        string path,
        Func<LibraryRootData, bool> supportsKind,
        CancellationToken cancellationToken) {
        var candidate = Path.GetFullPath(path);
        return (await roots.GetEnabledRootsAsync(cancellationToken))
            .Where(supportsKind)
            .Where(root => IsAtOrUnder(candidate, Path.GetFullPath(root.Path)))
            .OrderByDescending(root => Path.GetFullPath(root.Path).Length)
            .FirstOrDefault();
    }

    public static Task EnqueueReconciliationAsync(
        JobContext context,
        JobType type,
        LibraryRootData root,
        string label,
        ILogger logger,
        CancellationToken cancellationToken) =>
        ImportedMaterializationHousekeeping.TryAsync(
            logger,
            "Imported Entity is ready but optional library reconciliation could not be queued.",
            () => context.EnqueueIfNeededAsync(new EnqueueJobRequest(
                type,
                PayloadJson: new ScanRootPayload(root.Id).ToJson(),
                TargetEntityKind: JobTargetKinds.LibraryRoot,
                TargetEntityId: root.Id.ToString(),
                TargetLabel: label), cancellationToken));

    private static bool IsAtOrUnder(string candidate, string root) {
        if (FileSystemPathComparison.Equals(candidate, root)) {
            return true;
        }

        var normalizedRoot = Path.EndsInDirectorySeparator(root)
            ? root
            : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(normalizedRoot, FileSystemPathComparison.Comparison);
    }
}

/// <summary>
/// Movie import engine: places the release's primary video file at
/// <c>{Title (Year)}/{Title (Year)}.{ext}</c> under the first video-enabled library root, writes the
/// identify hint keyed on the movie folder, and chains a video scan — which binds the folder to the
/// wanted Movie entity via the acquisition hint. The profile's import mode controls whether the payload is
/// moved, copied, or hardlinked before the completed download is cleaned up or left to seed.
/// </summary>
public sealed class MovieAcquisitionImportEngine(
    IAcquisitionStore acquisitions,
    IBookAcquisitionProfileStore profiles,
    ILibraryScanRootPersistence roots,
    IDownloadPayloadReader payloads,
    IImportFileMover mover,
    ImportedTorrentRemover torrents,
    IImportTargetIndex targets,
    IAcquisitionBlocklistStore blocklist,
    IAcquisitionHistoryStore history,
    IImportedEntityMaterializer materializer,
    ILogger<MovieAcquisitionImportEngine> logger) : IAcquisitionImportEngine {
    public EntityKind Kind => EntityKind.Movie;

    public async Task ImportAsync(JobContext context, AcquisitionImportContext import, CancellationToken cancellationToken) {
        var profile = await profiles.GetImportProfileAsync(import.ProfileId, EntityKind.Movie, cancellationToken);

        // Quality is a release fact and remains available even when Move already consumed the payload.
        var selected = await acquisitions.GetSelectedReleaseAsync(import.Id, cancellationToken);
        var ownedMediaQuality = selected is null ? null : MediaQualityLadder.Detect(EntityKind.Movie, selected.Title).Code;

        if (import.ImportPlacementCheckpoint is { } durableCheckpoint) {
            var checkpointRoot = await ResolveCheckpointRootAsync(durableCheckpoint, cancellationToken);
            if (checkpointRoot is null) {
                await acquisitions.SetStatusAsync(
                    import.Id,
                    AcquisitionStatus.ManualImportRequired,
                    "The saved movie import targets a library root that moved, was disabled, or no longer accepts video. Review the partial import before retrying.",
                    cancellationToken);
                return;
            }
            if (!ImportPlacementExecution.MatchesTransfer(durableCheckpoint, import)) {
                await acquisitions.SetStatusAsync(
                    import.Id,
                    AcquisitionStatus.ManualImportRequired,
                    "This movie import checkpoint belongs to a different download attempt and was not reused. Review the partial files before retrying.",
                    cancellationToken);
                return;
            }

            var resumed = await ImportPlacementExecution.ExecuteAsync(
                acquisitions,
                mover,
                import.Id,
                durableCheckpoint,
                cancellationToken);
            if (resumed is null) {
                return;
            }

            await FinalizeImportAsync(
                context,
                import,
                selected,
                ownedMediaQuality,
                checkpointRoot,
                resumed.HintPath,
                resumed.FinalSourcePath,
                ImportPlacementExecution.MediaPaths(resumed),
                resumed.ImportMode,
                resumed.SuccessMessage,
                cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(import.ContentPath) || payloads.Read(import.ContentPath) is not { } payload) {
            await Fail(import.Id, "The completed download reported no content path.", cancellationToken);
            return;
        }

        var templateContext = new ImportTemplateContext(import.Title, import.Author, import.Year);

        // A movie that already lives on disk merges into its existing folder (or safely holds an owned-file
        // upgrade for review), never a template-derived parallel folder — that would mint a duplicate movie.
        if (import.EntityId is { } linkedEntityId
            && await targets.GetMovieTargetAsync(linkedEntityId, cancellationToken) is { } target
            && Directory.Exists(target.FolderPath)) {
            var existingRoot = await ImportRootResolution.ResolveOwningAsync(
                roots,
                target.FolderPath,
                static candidate => candidate.ScanVideos,
                cancellationToken);
            if (existingRoot is null) {
                await Fail(import.Id, "The existing movie is outside every enabled video library root.", cancellationToken);
                return;
            }

            await ImportIntoExistingMovieAsync(
                context,
                import,
                payload,
                target,
                existingRoot,
                profile,
                templateContext,
                selected,
                ownedMediaQuality,
                cancellationToken);
            return;
        }

        var root = await ImportRootResolution.ResolveAsync(
            roots, import.TargetLibraryRootId, profile?.TargetLibraryRootId, static candidate => candidate.ScanVideos, cancellationToken);
        if (root is null) {
            await Fail(import.Id, "No enabled video library root exists to import the movie into.", cancellationToken);
            return;
        }

        var plan = ImportTargetResolver.Resolve(
            payload.ContentRoot, root.Path,
            MovieImportPlanBuilder.Plan(payload.Files, templateContext, profile?.PathTemplate, ownedMediaQuality));
        if (plan.Blocked) {
            await acquisitions.SetStatusAsync(import.Id, AcquisitionStatus.ManualImportRequired, BlockMessage(plan.BlockReason), cancellationToken);
            return;
        }

        var importMode = profile?.ImportMode ?? ImportMode.Move;
        var units = ImportPlacementExecution.ReserveUnits(
            payload.ContentRoot,
            plan.Items.Select(item => (item, IsMedia: true)).ToArray(),
            mover);
        var hintFolder = Path.GetDirectoryName(units[0].TargetAbsolutePath) ?? Path.GetFullPath(root.Path);
        var checkpoint = CreateCheckpoint(
            import,
            context,
            root,
            importMode,
            hintFolder,
            hintFolder,
            "Imported into the library.",
            units);
        if (!await acquisitions.TryCreateImportPlacementCheckpointAsync(import.Id, checkpoint, cancellationToken)) {
            logger.LogInformation(
                "Movie import checkpoint for {Id} was superseded before placement; skipping stale work.",
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
            selected,
            ownedMediaQuality,
            root,
            completed.HintPath,
            completed.FinalSourcePath,
            ImportPlacementExecution.MediaPaths(completed),
            completed.ImportMode,
            completed.SuccessMessage,
            cancellationToken);
    }

    /// <summary>
    /// The merged path for an existing movie: the release's primary video either fills a folder that has
    /// no video yet, or gates against the owned file. Non-upgrades are blocklisted; valid upgrades are
    /// held before mutation until the replacement protocol has the same crash-safe checkpoint semantics.
    /// </summary>
    private async Task ImportIntoExistingMovieAsync(
        JobContext context,
        AcquisitionImportContext import,
        DownloadPayload payload,
        MovieDiskTarget target,
        LibraryRootData root,
        BookImportProfile? profile,
        ImportTemplateContext templateContext,
        SelectedRelease? selected,
        string? qualityCode,
        CancellationToken cancellationToken) {
        var plan = MovieImportPlanBuilder.Plan(payload.Files, templateContext, profile?.PathTemplate, qualityCode);
        if (plan.Blocked) {
            await acquisitions.SetStatusAsync(import.Id, AcquisitionStatus.ManualImportRequired, BlockMessage(plan.BlockReason), cancellationToken);
            return;
        }

        var item = plan.Items[0];
        var sourceAbsolute = Path.GetFullPath(Path.Combine(payload.ContentRoot, item.SourceRelativePath));
        var fileName = item.TargetRelativePath.Split('/')[^1];
        var importMode = profile?.ImportMode ?? ImportMode.Move;

        if (target.OwnedVideoFilePath is not { } owned || !File.Exists(owned)) {
            // The folder exists (covers, sidecars) but owns no video yet — fill it with the template-named file.
            var units = ImportPlacementExecution.ReserveUnits(
                payload.ContentRoot,
                [(new ResolvedImportItem(
                    sourceAbsolute,
                    Path.GetFullPath(Path.Combine(target.FolderPath, fileName))), IsMedia: true)],
                mover);
            var checkpoint = CreateCheckpoint(
                import,
                context,
                root,
                importMode,
                Path.GetFullPath(target.FolderPath),
                units[0].TargetAbsolutePath,
                "Imported into the existing movie.",
                units);
            if (!await acquisitions.TryCreateImportPlacementCheckpointAsync(import.Id, checkpoint, cancellationToken)) {
                logger.LogInformation(
                    "Existing-movie import checkpoint for {Id} was superseded before placement; skipping stale work.",
                    import.Id);
                return;
            }

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
                selected,
                qualityCode,
                root,
                completed.HintPath,
                completed.FinalSourcePath,
                ImportPlacementExecution.MediaPaths(completed),
                completed.ImportMode,
                completed.SuccessMessage,
                cancellationToken);
            return;
        }

        var incomingPosition = selected is null ? 0 : MediaQualityLadder.Detect(EntityKind.Movie, selected.Title).Position;
        if (incomingPosition <= 0) {
            incomingPosition = (int)VideoQualityDetection.Detect(Path.GetFileNameWithoutExtension(item.SourceRelativePath));
        }

        var incomingRevision = selected is null ? 1 : ReleaseRevisionDetection.Detect(selected.Title);
        var rules = await profiles.GetRulesAsync(import.ProfileId, EntityKind.Movie, cancellationToken);
        var action = TvExistingTargetMerge.DecideAgainstOwned(
            fileName, owned, incomingPosition, incomingRevision, rules.ProperPolicy, import.AllowFormatChange);
        if (action != MergeFileAction.ReplaceUpgrade) {
            await MergedImportExecution.FailNothingUsableAsync(
                acquisitions, blocklist, history, torrents, logger, import, selected,
                hasFormatChange: action == MergeFileAction.DropFormatChange,
                "The release upgrades the movie but changes the file format; import it manually.",
                cancellationToken);
            return;
        }

        await acquisitions.SetStatusAsync(
            import.Id,
            AcquisitionStatus.ManualImportRequired,
            "This release upgrades the existing movie, but automatic in-place replacement is paused because it cannot yet guarantee crash-safe recovery. The owned file was left untouched; import the upgrade manually.",
            cancellationToken);
    }

    private static ImportPlacementCheckpoint CreateCheckpoint(
        AcquisitionImportContext import,
        JobContext context,
        LibraryRootData root,
        ImportMode importMode,
        string hintPath,
        string finalSourcePath,
        string successMessage,
        IReadOnlyList<ImportPlacementCheckpointUnit> units) =>
        new(
            import.Kind,
            root.Id,
            Path.GetFullPath(root.Path),
            ImportPlacementExecution.PayloadRootPath(import.ContentPath
                ?? throw new InvalidOperationException("A fresh movie import requires its payload path.")),
            importMode,
            Path.GetFullPath(hintPath),
            Path.GetFullPath(finalSourcePath),
            successMessage,
            units,
            string.IsNullOrWhiteSpace(import.ClientItemId) ? null : import.ClientItemId,
            Guid.NewGuid(),
            context.Job.Id);

    private async Task<LibraryRootData?> ResolveCheckpointRootAsync(
        ImportPlacementCheckpoint checkpoint,
        CancellationToken cancellationToken) {
        var root = await roots.GetLibraryRootAsync(checkpoint.LibraryRootId, cancellationToken);
        return root is { Enabled: true, ScanVideos: true }
            && FileSystemPathComparison.Equals(
                Path.GetFullPath(root.Path),
                checkpoint.LibraryRootPath)
                ? root
                : null;
    }

    /// <summary>The shared success tail: hint, final source path, scan chain, torrent handling, and the imported mark.</summary>
    private async Task FinalizeImportAsync(
        JobContext context,
        AcquisitionImportContext import,
        SelectedRelease? selected,
        string? qualityCode,
        LibraryRootData root,
        string hintFolder,
        string finalSourcePath,
        IReadOnlyList<string> placedMediaPaths,
        ImportMode importMode,
        string message,
        CancellationToken cancellationToken) {
        // Book quality axes don't apply to movies (they record the book floor).
        var ownedMediaRevision = selected is null ? 1 : ReleaseRevisionDetection.Detect(selected.Title);
        var ownedFormatScore = await OwnedFormatScore.ComputeAsync(profiles, import.ProfileId, EntityKind.Movie, selected, cancellationToken);

        await acquisitions.WriteImportHintAsync(import.Id, hintFolder, import, BookQualityRank.Floor, cancellationToken);
        await acquisitions.SetFinalSourcePathAsync(import.Id, finalSourcePath, cancellationToken);

        await context.ReportProgressAsync(80, "Cataloging imported movie", cancellationToken);
        await materializer.MaterializeAsync(
            import.Kind,
            context,
            new ImportedEntityMaterializationRequest(import.Id, import.EntityId, root, placedMediaPaths),
            cancellationToken);
        await ImportRootResolution.EnqueueReconciliationAsync(
            context, JobType.ScanLibrary, root, "Imported movie scan", logger, cancellationToken);

        await torrents.HandleImportedAsync(import, importMode, cancellationToken);

        await acquisitions.MarkImportedWithQualityAsync(import.Id, BookQualityRank.Floor, message, cancellationToken, qualityCode, ownedMediaRevision, ownedFormatScore);
    }

    private Task Fail(Guid acquisitionId, string message, CancellationToken cancellationToken) =>
        acquisitions.SetStatusAsync(acquisitionId, AcquisitionStatus.Failed, message, cancellationToken);

    private static string BlockMessage(ImportBlockReason? reason) => reason switch {
        ImportBlockReason.NoSupportedPayload => "The download contains no supported video files.",
        ImportBlockReason.AmbiguousMultiplePrimaries => "The download contains multiple full-size videos; import the right one manually.",
        _ => "The download could not be imported automatically."
    };
}

/// <summary>
/// TV import engine: places a release's episode files as
/// <c>{Series}/Season NN/{Series} - SxxEyy.{ext}</c> in the first video-enabled library root — the
/// exact layout the video catalog materializes a series hierarchy from. The engine synchronously binds
/// the wanted series, season, and episodes by folder path and position before it reports Imported, so
/// playback never waits for a later aggregate scan. One engine class serves both
/// TV acquisition units — season packs (<see cref="EntityKind.VideoSeason"/>) and single episodes
/// (<see cref="EntityKind.Video"/>) — since placement rules are identical at either granularity.
/// An acquisition linked to a series that already lives on disk MERGES into the existing folder tree
/// instead: new episodes land in the real season folders, already-owned episodes follow the upgrade
/// rules (replace strictly-better in place, drop the rest), and a payload with nothing better than the
/// owned files fails with the release blocklisted.
/// </summary>
public sealed class TvAcquisitionImportEngine(
    EntityKind kind,
    IAcquisitionStore acquisitions,
    IBookAcquisitionProfileStore profiles,
    ILibraryScanRootPersistence roots,
    IDownloadPayloadReader payloads,
    IImportFileMover mover,
    ImportedTorrentRemover torrents,
    IImportTargetIndex targets,
    IOwnedFileReplacer replacer,
    IAcquisitionBlocklistStore blocklist,
    IAcquisitionHistoryStore history,
    IImportedVideoMaterializer materializer,
    VideoScanConcurrencyGate scanGate,
    ILogger<TvAcquisitionImportEngine> logger) : IAcquisitionImportEngine {
    public EntityKind Kind => kind;

    public async Task ImportAsync(JobContext context, AcquisitionImportContext import, CancellationToken cancellationToken) {
        var profile = await profiles.GetImportProfileAsync(import.ProfileId, import.Kind, cancellationToken);
        var payload = string.IsNullOrWhiteSpace(import.ContentPath) ? null : payloads.Read(import.ContentPath);

        // The owned quality (and the {Quality} naming token) is the video-ladder code from the selected
        // release; both TV units detect on the video ladder. Detected before planning so the template can
        // render it into the path. A season pack captures it too, though its monitor fulfills on import —
        // a multi-file pack can't be single-file swapped — so the code is recorded for display but never
        // drives an upgrade.
        var selected = await acquisitions.GetSelectedReleaseAsync(import.Id, cancellationToken);
        var ownedMediaQuality = selected is null ? null : MediaQualityLadder.Detect(import.Kind, selected.Title).Code;

        // Keep a full video scan from observing moved files before the acquisition hint and wanted
        // bindings exist. The same singleton gate wraps scan snapshots and reconciliation.
        await using var scanLease = await scanGate.EnterAsync(cancellationToken);

        if (import.TvImportCheckpoint is { } durableCheckpoint) {
            if (!await acquisitions.IsCurrentTvImportCheckpointAsync(
                    import.Id,
                    durableCheckpoint,
                    cancellationToken)) {
                logger.LogInformation(
                    "TV import checkpoint for {Id} was superseded while waiting for the video scan gate; skipping stale work.",
                    import.Id);
                return;
            }

            if (!string.Equals(
                    NormalizeClientItemId(durableCheckpoint.TransferClientItemId),
                    NormalizeClientItemId(import.ClientItemId),
                    StringComparison.Ordinal)) {
                await acquisitions.SetStatusAsync(
                    import.Id,
                    AcquisitionStatus.ManualImportRequired,
                    "This TV import checkpoint belongs to a different download attempt and was not reused. Review the partial files before retrying.",
                    cancellationToken);
                return;
            }

            await ExecuteTvCheckpointAsync(
                context,
                import,
                payload,
                durableCheckpoint,
                selected,
                ownedMediaQuality,
                cancellationToken);
            return;
        }

        // File placement is intentionally checkpointed before catalog persistence. If a transient
        // database failure interrupted that second step (especially after a Move consumed the download
        // payload), retry the exact final path instead of trying to move the release a second time.
        if (await TryResumePlacedImportAsync(
                context, import, profile, selected, ownedMediaQuality, cancellationToken)) {
            return;
        }

        if (payload is null) {
            await Fail(import.Id, "The completed download reported no content path.", cancellationToken);
            return;
        }

        // An acquisition linked to a series already on disk merges into its existing tree — placing a
        // template-derived parallel folder would mint a duplicate series (the scan's binds refuse
        // entities that already own files). Everything else keeps the template placement.
        if (import.EntityId is { } linkedEntityId
            && await targets.GetTvLayoutAsync(linkedEntityId, cancellationToken) is { } layout
            && Directory.Exists(layout.SeriesFolderPath)) {
            await ImportIntoExistingSeriesAsync(context, import, payload, layout, profile, selected, ownedMediaQuality, cancellationToken);
            return;
        }

        var root = await ImportRootResolution.ResolveAsync(
            roots, import.TargetLibraryRootId, profile?.TargetLibraryRootId, static candidate => candidate.ScanVideos, cancellationToken);
        if (root is null) {
            await Fail(import.Id, "No enabled video library root exists to import the episodes into.", cancellationToken);
            return;
        }

        var series = SeriesOf(import);
        var unitsPlan = TvImportPlanBuilder.PlanUnits(
            payload.Files,
            series,
            import.SeasonNumber,
            import.EpisodeNumber,
            profile?.PathTemplate,
            ownedMediaQuality,
            await EpisodeTitlesForAsync(import, cancellationToken));
        var plan = unitsPlan.Blocked
            ? ResolvedImportPlan.Block(unitsPlan.BlockReason!.Value)
            : ImportTargetResolver.Resolve(
                payload.ContentRoot,
                root.Path,
                ImportPlan.For(unitsPlan.Units
                    .Select(unit => new ImportPlanItem(unit.SourceRelativePath, unit.TargetRelativePath))
                    .ToArray()));
        if (plan.Blocked) {
            await acquisitions.SetStatusAsync(import.Id, AcquisitionStatus.ManualImportRequired, BlockMessage(plan.BlockReason), cancellationToken);
            return;
        }

        var importMode = profile?.ImportMode ?? ImportMode.Move;
        var seriesFolder = Path.GetFullPath(Path.Combine(
            root.Path,
            TvImportPlanBuilder.SeriesFolderRelative(series, profile?.PathTemplate)));
        var checkpoint = new TvImportCheckpoint(
            root.Id,
            seriesFolder,
            importMode,
            import.AllowFormatChange,
            "Imported into the library.",
            PreferSingleFileFinalSource: import.Kind == EntityKind.Video,
            unitsPlan.Units
                .Zip(plan.Items, static (unit, item) => new TvImportCheckpointUnit(
                    unit.SourceRelativePath,
                    item.TargetAbsolutePath,
                    unit.Season,
                    unit.Episode,
                    unit.ExtraEpisodes))
                .ToArray(),
            TransferClientItemId: NormalizeClientItemId(import.ClientItemId),
            AttemptId: Guid.NewGuid(),
            ClaimJobId: context.Job.Id);
        if (await PrepareTvCheckpointAsync(import, payload, checkpoint, cancellationToken) is not { } preparedCheckpoint) {
            return;
        }
        checkpoint = preparedCheckpoint;
        await ExecuteTvCheckpointAsync(
            context,
            import,
            payload,
            checkpoint,
            selected,
            ownedMediaQuality,
            cancellationToken);
    }

    /// <summary>
    /// The merged path for an existing series: plan units against the real on-disk layout, place new
    /// episodes into the existing folders, replace strictly-better collisions in place, and drop the
    /// rest. A payload that produced nothing is failed (blocklisting the release) or held for manual
    /// import when the only upgrades change the file format.
    /// </summary>
    private async Task ImportIntoExistingSeriesAsync(
        JobContext context,
        AcquisitionImportContext import,
        DownloadPayload payload,
        TvSeriesDiskLayout layout,
        BookImportProfile? profile,
        SelectedRelease? selected,
        string? qualityCode,
        CancellationToken cancellationToken) {
        var series = SeriesOf(import);
        var unitsPlan = TvImportPlanBuilder.PlanUnits(
            payload.Files, series, import.SeasonNumber, import.EpisodeNumber, profile?.PathTemplate, qualityCode,
            await EpisodeTitlesForAsync(import, cancellationToken));
        if (unitsPlan.Blocked) {
            await acquisitions.SetStatusAsync(import.Id, AcquisitionStatus.ManualImportRequired, BlockMessage(unitsPlan.BlockReason), cancellationToken);
            return;
        }

        var owningRoot = await ResolveOwningVideoRootAsync(layout.SeriesFolderPath, cancellationToken);
        if (owningRoot is null) {
            await Fail(import.Id, "The existing series is not inside an enabled video library root.", cancellationToken);
            return;
        }

        // Incoming quality from the release title; a title that ranks Unknown falls back to the
        // payload's own file tokens so a well-named pack under a bare title still gates honestly.
        var incomingPosition = selected is null ? 0 : MediaQualityLadder.Detect(import.Kind, selected.Title).Position;
        if (incomingPosition <= 0) {
            incomingPosition = unitsPlan.Units
                .Select(unit => (int)VideoQualityDetection.Detect(Path.GetFileNameWithoutExtension(unit.SourceRelativePath)))
                .DefaultIfEmpty(0)
                .Max();
        }

        var incomingRevision = selected is null ? 1 : ReleaseRevisionDetection.Detect(selected.Title);
        var rules = await profiles.GetRulesAsync(import.ProfileId, import.Kind, cancellationToken);
        var merged = TvExistingTargetMerge.Plan(
            unitsPlan.Units, layout,
            season => TvImportPlanBuilder.SeasonFolderSegment(series, season, profile?.PathTemplate),
            incomingPosition, incomingRevision, rules.ProperPolicy, import.AllowFormatChange);

        if (merged.Any(item => item.Action == MergeFileAction.HoldStructuralConflict)) {
            await acquisitions.SetStatusAsync(
                import.Id,
                AcquisitionStatus.ManualImportRequired,
                "A multi-episode file overlaps episode slots that already belong to different files. Review the files manually; Prismedia did not merge conflicting episode owners.",
                cancellationToken);
            return;
        }

        // Safety net: a merged import never writes outside the existing series folder.
        var seriesRoot = Path.GetFullPath(layout.SeriesFolderPath);
        if (merged.Any(item => !IsUnderFolder(Path.GetFullPath(item.TargetAbsolutePath), seriesRoot))) {
            await Fail(import.Id, "The merged import computed a target outside the series folder.", cancellationToken);
            return;
        }

        var importMode = profile?.ImportMode ?? ImportMode.Move;
        var executable = merged
            .Where(item => item.Action is MergeFileAction.PlaceNew or MergeFileAction.ReplaceUpgrade)
            .ToArray();
        var formatChanges = merged.Count(item => item.Action == MergeFileAction.DropFormatChange);
        if (executable.Length == 0) {
            await HandleNothingUsableAsync(import, selected, formatChanges > 0, cancellationToken);
            return;
        }

        var skipped = merged.Count - executable.Length;
        var message = skipped == 0
            ? "Imported into the existing series."
            : $"Imported {executable.Length} of {merged.Count} file(s) into the existing series; {skipped} were not upgrades over the files you already have.";
        var unitsBySource = unitsPlan.Units.ToDictionary(
            unit => unit.SourceRelativePath,
            FileSystemPathComparison.Comparer);
        var checkpoint = new TvImportCheckpoint(
            owningRoot.Id,
            layout.SeriesFolderPath,
            importMode,
            import.AllowFormatChange,
            message,
            PreferSingleFileFinalSource: true,
            executable.Select(item => {
                var unit = unitsBySource[item.SourceRelativePath];
                return new TvImportCheckpointUnit(
                    item.SourceRelativePath,
                    Path.GetFullPath(item.TargetAbsolutePath),
                    unit.Season,
                    unit.Episode,
                    unit.ExtraEpisodes,
                    item.Action == MergeFileAction.ReplaceUpgrade ? item.OwnedFilePath : null);
            }).ToArray(),
            TransferClientItemId: NormalizeClientItemId(import.ClientItemId),
            AttemptId: Guid.NewGuid(),
            ClaimJobId: context.Job.Id);
        if (await PrepareTvCheckpointAsync(import, payload, checkpoint, cancellationToken) is not { } preparedCheckpoint) {
            return;
        }
        checkpoint = preparedCheckpoint;
        await ExecuteTvCheckpointAsync(
            context,
            import,
            payload,
            checkpoint,
            selected,
            qualityCode,
            cancellationToken);
    }

    private async Task<TvImportCheckpoint?> PrepareTvCheckpointAsync(
        AcquisitionImportContext import,
        DownloadPayload payload,
        TvImportCheckpoint checkpoint,
        CancellationToken cancellationToken) {
        // Resolve every new-file collision before the plan is persisted. Execute then uses the exact
        // checkpoint path without choosing another suffix after a crash. Upgrade targets are already
        // exact by definition; reserve them so another unit cannot silently claim the same destination.
        var reservedTargets = new List<string>(checkpoint.Units.Count);
        var units = new TvImportCheckpointUnit[checkpoint.Units.Count];
        for (var index = 0; index < checkpoint.Units.Count; index++) {
            var unit = checkpoint.Units[index];
            string exactTarget;
            if (unit.PreviousFilePath is { } previousFilePath) {
                exactTarget = Path.ChangeExtension(
                    Path.GetFullPath(previousFilePath),
                    Path.GetExtension(unit.SourceRelativePath));
                if (!FileSystemPathComparison.Equals(exactTarget, Path.GetFullPath(previousFilePath))
                    && (File.Exists(exactTarget) || Directory.Exists(exactTarget))) {
                    await acquisitions.SetStatusAsync(
                        import.Id,
                        AcquisitionStatus.ManualImportRequired,
                        CrossFormatTargetConflictMessage(exactTarget),
                        cancellationToken);
                    return null;
                }
                if (reservedTargets.Contains(exactTarget, FileSystemPathComparison.Comparer)) {
                    throw new InvalidOperationException("The TV import plan assigns multiple files to one upgrade target.");
                }
            } else {
                var desiredTarget = Path.GetFullPath(unit.TargetAbsolutePath);
                exactTarget = mover.ResolveExactTargetPath(desiredTarget, reservedTargets);
                if (!FileSystemPathComparison.Equals(exactTarget, desiredTarget)) {
                    await acquisitions.SetStatusAsync(
                        import.Id,
                        AcquisitionStatus.ManualImportRequired,
                        $"The episode slot target already exists in the library ({Path.GetFileName(desiredTarget)}). " +
                        "Review the conflicting file before retrying; Prismedia will not create a duplicate episode variant implicitly.",
                        cancellationToken);
                    return null;
                }
            }

            reservedTargets.Add(exactTarget);
            units[index] = unit with {
                TargetAbsolutePath = exactTarget,
                SourceAbsolutePath = Path.GetFullPath(Path.Combine(payload.ContentRoot, unit.SourceRelativePath)),
                ReplacementBackupPath = unit.PreviousFilePath is null
                    ? null
                    : OwnedFileReplacementArtifacts.CheckpointBackupPath(unit.PreviousFilePath, checkpoint.AttemptId),
                ReplacementEvidencePath = unit.PreviousFilePath is null
                    ? null
                    : OwnedFileReplacementArtifacts.CheckpointEvidencePath(unit.PreviousFilePath, checkpoint.AttemptId),
            };

            if ((!string.IsNullOrWhiteSpace(units[index].ReplacementBackupPath)
                    && File.Exists(units[index].ReplacementBackupPath))
                || (!string.IsNullOrWhiteSpace(units[index].ReplacementEvidencePath)
                    && File.Exists(units[index].ReplacementEvidencePath))) {
                await acquisitions.SetStatusAsync(
                    import.Id,
                    AcquisitionStatus.ManualImportRequired,
                    "Recovery files already exist for this exact TV import attempt. Review the files before retrying.",
                    cancellationToken);
                return null;
            }
        }

        checkpoint = checkpoint with { Units = units };
        // Persist the complete plan before ExecuteTvCheckpointAsync performs its first filesystem mutation.
        // The executor refreshes the binding hint on every run, including durable resumes.
        if (await acquisitions.TryCreateTvImportCheckpointAsync(import.Id, checkpoint, cancellationToken)) {
            return checkpoint;
        }

        logger.LogInformation(
            "TV import {Id} lost its initial lifecycle claim before checkpoint creation; no files were touched.",
            import.Id);
        return null;
    }

    /// <summary>
    /// Executes or resumes the exact durable TV plan. Each successful file mutation is persisted before the
    /// next begins, then the complete batch is materialized together from the checkpoint. A retry therefore
    /// never guesses from a partially consumed download payload, consumes the binding hint only once, and
    /// never reports Imported for a partial pack.
    /// </summary>
    private async Task ExecuteTvCheckpointAsync(
        JobContext context,
        AcquisitionImportContext import,
        DownloadPayload? payload,
        TvImportCheckpoint checkpoint,
        SelectedRelease? selected,
        string? qualityCode,
        CancellationToken cancellationToken) {
        var root = await roots.GetLibraryRootAsync(checkpoint.LibraryRootId, cancellationToken);
        if (root is not { Enabled: true, ScanVideos: true }) {
            throw new InvalidOperationException("The TV import checkpoint's video library root is no longer enabled.");
        }

        var seriesFolder = Path.GetFullPath(checkpoint.SeriesFolderPath);
        if (!IsAtOrUnderFolder(seriesFolder, Path.GetFullPath(root.Path)) || checkpoint.Units.Count == 0) {
            throw new InvalidOperationException("The TV import checkpoint is invalid for its library root.");
        }

        // Recreate the broad, unconsumed identity/binding hint before EVERY run. A prior process may have
        // died after placing only part of the pack, allowing a waiting scan to consume the old hint. Keeping
        // this refresh ahead of the next mutation makes a second interruption safe as well.
        await acquisitions.WriteImportHintAsync(
            import.Id,
            seriesFolder,
            import,
            BookQualityRank.Floor,
            cancellationToken);

        var units = checkpoint.Units.ToArray();
        await context.ReportProgressAsync(40, "Placing episodes", cancellationToken);
        for (var index = 0; index < units.Length; index++) {
            var unit = units[index];
            var checkpointTarget = Path.GetFullPath(unit.TargetAbsolutePath);
            if (!IsAtOrUnderFolder(checkpointTarget, Path.GetFullPath(root.Path))
                || (unit.PreviousFilePath is { } previousFilePath
                    && !IsAtOrUnderFolder(Path.GetFullPath(previousFilePath), Path.GetFullPath(root.Path)))) {
                throw new InvalidOperationException("A TV import checkpoint file is outside its library root.");
            }

            var finalPath = !string.IsNullOrWhiteSpace(unit.FinalPath) && File.Exists(unit.FinalPath)
                ? Path.GetFullPath(unit.FinalPath)
                : null;

            if (finalPath is null) {
                var sourceAbsolute = !string.IsNullOrWhiteSpace(unit.SourceAbsolutePath)
                    ? Path.GetFullPath(unit.SourceAbsolutePath)
                    : payload is null
                        ? null
                        : Path.GetFullPath(Path.Combine(payload.ContentRoot, unit.SourceRelativePath));

                var expectedPath = checkpointTarget;
                var previousPath = unit.PreviousFilePath is null
                    ? null
                    : Path.GetFullPath(unit.PreviousFilePath);
                var evidencePath = string.IsNullOrWhiteSpace(unit.ReplacementEvidencePath)
                    ? null
                    : Path.GetFullPath(unit.ReplacementEvidencePath);
                var evidenceMatchesTarget = evidencePath is not null
                    && File.Exists(evidencePath)
                    && File.Exists(expectedPath)
                    && FilesHaveSameContent(evidencePath, expectedPath);
                if (previousPath is not null
                    && (sourceAbsolute is null || !File.Exists(sourceAbsolute))
                    && File.Exists(OwnedFileReplacementArtifacts.StagedPath(previousPath))) {
                    sourceAbsolute = RestoreReplacementArtifactSource(
                        OwnedFileReplacementArtifacts.StagedPath(previousPath),
                        sourceAbsolute,
                        unit.SourceRelativePath);
                } else if (previousPath is not null
                    && (sourceAbsolute is null || !File.Exists(sourceAbsolute))
                    && evidencePath is not null
                    && File.Exists(evidencePath)
                    && !evidenceMatchesTarget) {
                    sourceAbsolute = RestoreReplacementArtifactSource(
                        evidencePath,
                        sourceAbsolute,
                        unit.SourceRelativePath);
                }

                var sourceExists = sourceAbsolute is not null && File.Exists(sourceAbsolute);
                var targetExists = File.Exists(expectedPath);
                var isReplacement = previousPath is not null;
                var targetMatchesAvailableSource = targetExists
                    && sourceExists
                    && FilesHaveSameContent(expectedPath, sourceAbsolute!);

                // New exact-path placements publish atomically into a target that was absent when the
                // checkpoint was written. Replacements need stronger evidence because their same-path
                // target existed before the mutation: the retained backup proves the atomic install ran.
                var replacementInstallCompleted = isReplacement
                    && !sourceExists
                    && targetExists
                    && evidenceMatchesTarget;
                if ((!isReplacement && targetExists && !sourceExists)
                    || targetMatchesAvailableSource
                    || replacementInstallCompleted) {
                    finalPath = expectedPath;
                } else {
                    if (!sourceExists) {
                        throw new FileNotFoundException(
                            "A pending TV import file is missing from the download, exact target, and replacement staging checkpoint.",
                            sourceAbsolute ?? unit.SourceRelativePath);
                    }

                    if (previousPath is not null
                        && !FileSystemPathComparison.Equals(expectedPath, previousPath)
                        && (File.Exists(expectedPath) || Directory.Exists(expectedPath))) {
                        await acquisitions.SetStatusAsync(
                            import.Id,
                            AcquisitionStatus.ManualImportRequired,
                            CrossFormatTargetConflictMessage(expectedPath),
                            cancellationToken);
                        return;
                    }

                    finalPath = previousPath is not null
                        ? await ReplaceOwnedAsync(
                            previousPath,
                            sourceAbsolute!,
                            checkpoint.ImportMode,
                            checkpoint.AllowFormatChange,
                            unit.ReplacementBackupPath,
                            unit.ReplacementEvidencePath,
                            cancellationToken)
                        : await mover.PlaceExactAsync(
                            new ResolvedImportItem(sourceAbsolute!, checkpointTarget),
                            checkpoint.ImportMode,
                            cancellationToken);
                    if (finalPath is null) {
                        throw new InvalidOperationException("A planned TV upgrade could not replace its existing episode file.");
                    }
                }

                RetirePreviousFormatPath(unit.PreviousFilePath, finalPath);

                units[index] = unit = unit with { FinalPath = finalPath };
                checkpoint = checkpoint with { Units = units };
                await acquisitions.SetTvImportCheckpointAsync(import.Id, checkpoint, cancellationToken);
                TryDeleteFile(unit.ReplacementEvidencePath);
            }

            await context.ReportProgressAsync(
                40 + ((index + 1) * 30 / units.Length),
                $"Placed {index + 1}/{units.Length} episode files",
                cancellationToken);
        }

        var importedEpisodes = units.Select(unit => new ImportedTvEpisode(
            unit.FinalPath!,
            unit.SeasonNumber,
            unit.EpisodeNumber,
            unit.CoveredEpisodeNumbers,
            unit.PreviousFilePath)).ToArray();
        var seasonFolders = importedEpisodes
            .Select(episode => Path.GetDirectoryName(episode.FilePath) ?? seriesFolder)
            .Distinct(FileSystemPathComparison.Comparer)
            .ToArray();
        var hintFolder = seasonFolders.Length == 1 ? seasonFolders[0] : seriesFolder;
        var finalSourcePath = checkpoint.PreferSingleFileFinalSource && importedEpisodes.Length == 1
            ? importedEpisodes[0].FilePath
            : hintFolder;
        await FinalizeImportAsync(
            context,
            import,
            selected,
            qualityCode,
            hintFolder,
            finalSourcePath,
            checkpoint.ImportMode,
            new ImportedTvMaterializationRequest(import.Id, root, seriesFolder, importedEpisodes),
            checkpoint.SuccessMessage,
            cancellationToken);
    }

    /// <summary>
    /// Resumes the catalog half of an import whose files were already placed. The checkpoint can name
    /// one file, one season folder, or the series folder for a multi-season pack; cataloging the other
    /// already-owned files beneath that boundary is harmless because video upserts are path-idempotent.
    /// </summary>
    private async Task<bool> TryResumePlacedImportAsync(
        JobContext context,
        AcquisitionImportContext import,
        BookImportProfile? profile,
        SelectedRelease? selected,
        string? qualityCode,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(import.FinalSourcePath)) {
            return false;
        }

        var checkpoint = Path.GetFullPath(import.FinalSourcePath);
        var files = EnumerateCheckpointVideos(checkpoint);
        if (files.Count == 0) {
            return false;
        }

        TvSeriesDiskLayout? layout = null;
        if (import.EntityId is { } linkedEntityId) {
            layout = await targets.GetTvLayoutAsync(linkedEntityId, cancellationToken);
        }

        var seriesFolders = files
            .Select(path => Path.GetDirectoryName(Path.GetDirectoryName(path) ?? string.Empty))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(FileSystemPathComparison.Comparer)
            .ToArray();
        var seriesFolder = layout?.SeriesFolderPath
            ?? (seriesFolders.Length == 1 ? seriesFolders[0] : null);
        if (seriesFolder is null) {
            throw new InvalidOperationException("The placed TV files do not resolve to one series folder.");
        }

        var root = await ResolveOwningVideoRootAsync(seriesFolder, cancellationToken)
            ?? throw new InvalidOperationException("The placed TV files are no longer inside an enabled video library root.");
        var importedEpisodes = new List<ImportedTvEpisode>(files.Count);
        foreach (var file in files) {
            var parsed = TvReleaseTokens.ParseEpisode(Path.GetFileNameWithoutExtension(file));
            if (parsed is null && files.Count == 1 && import.SeasonNumber is { } season && import.EpisodeNumber is { } episode) {
                parsed = (season, episode);
            }

            if (parsed is { } unit) {
                importedEpisodes.Add(new ImportedTvEpisode(file, unit.Season, unit.Episode, []));
            }
        }

        if (importedEpisodes.Count == 0) {
            throw new InvalidOperationException("The placed TV files no longer carry recognizable episode numbers.");
        }

        var seasonFolders = importedEpisodes
            .Select(episode => Path.GetDirectoryName(episode.FilePath)!)
            .Distinct(FileSystemPathComparison.Comparer)
            .ToArray();
        var hintFolder = seasonFolders.Length == 1 ? seasonFolders[0] : seriesFolder;
        await FinalizeImportAsync(
            context,
            import,
            selected,
            qualityCode,
            hintFolder,
            checkpoint,
            profile?.ImportMode ?? ImportMode.Move,
            new ImportedTvMaterializationRequest(import.Id, root, seriesFolder, importedEpisodes),
            "Finished cataloging files already placed in the library.",
            cancellationToken);
        return true;
    }

    private static IReadOnlyList<string> EnumerateCheckpointVideos(string checkpoint) {
        if (File.Exists(checkpoint)) {
            return MovieImportPlanBuilder.VideoExtensions.Contains(Path.GetExtension(checkpoint))
                ? [checkpoint]
                : [];
        }

        if (!Directory.Exists(checkpoint)) {
            return [];
        }

        return Directory
            .EnumerateFiles(checkpoint, "*", SearchOption.AllDirectories)
            .Where(path => MovieImportPlanBuilder.VideoExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool FilesHaveSameContent(string firstPath, string secondPath) {
        try {
            var first = new FileInfo(firstPath);
            var second = new FileInfo(secondPath);
            if (!first.Exists || !second.Exists || first.Length != second.Length) {
                return false;
            }

            using var firstStream = first.OpenRead();
            using var secondStream = second.OpenRead();
            return SHA256.HashData(firstStream).AsSpan().SequenceEqual(SHA256.HashData(secondStream));
        } catch (IOException) {
            return false;
        } catch (UnauthorizedAccessException) {
            return false;
        }
    }

    private static string RestoreReplacementArtifactSource(
        string artifactPath,
        string? originalSourcePath,
        string sourceRelativePath) {
        var source = !string.IsNullOrWhiteSpace(originalSourcePath)
            ? Path.GetFullPath(originalSourcePath)
            : Path.Combine(
                Path.GetTempPath(),
                "prismedia-import-recovery",
                $"{Guid.NewGuid():N}{Path.GetExtension(sourceRelativePath)}");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        if (File.Exists(source)) {
            throw new IOException($"The replacement recovery source already exists: {source}");
        }

        File.Move(artifactPath, source);
        return source;
    }

    private static void TryDeleteFile(string? path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return;
        }

        try {
            File.Delete(path);
        } catch {
            // Non-media crash evidence is safe to leave behind; a later maintenance pass can remove it.
        }
    }

    /// <summary>
    /// The provider episode list of the season being imported (numbers + titles from the linked
    /// entity's graph), for title-based file alignment. Empty for unlinked acquisitions or when the
    /// season isn't materialized yet — the plan then keeps its numeric placement.
    /// </summary>
    private async Task<IReadOnlyList<TvEpisodeTitle>> EpisodeTitlesForAsync(
        AcquisitionImportContext import, CancellationToken cancellationToken) =>
        import.EntityId is { } linkedEntityId && import.SeasonNumber is { } season
            ? await targets.GetSeasonEpisodeTitlesAsync(linkedEntityId, season, cancellationToken)
            : [];

    /// <summary>
    /// The shared success tail: persist the hint/path checkpoint, synchronously materialize the exact
    /// files, queue aggregate scan housekeeping, handle the torrent, and only then record Imported.
    /// </summary>
    private async Task FinalizeImportAsync(
        JobContext context,
        AcquisitionImportContext import,
        SelectedRelease? selected,
        string? qualityCode,
        string hintFolder,
        string finalSourcePath,
        ImportMode importMode,
        ImportedTvMaterializationRequest materialization,
        string message,
        CancellationToken cancellationToken) {
        var ownedMediaRevision = selected is null ? 1 : ReleaseRevisionDetection.Detect(selected.Title);
        var ownedFormatScore = await OwnedFormatScore.ComputeAsync(profiles, import.ProfileId, import.Kind, selected, cancellationToken);

        await acquisitions.WriteImportHintAsync(import.Id, hintFolder, import, BookQualityRank.Floor, cancellationToken);
        await acquisitions.SetFinalSourcePathAsync(import.Id, finalSourcePath, cancellationToken);

        await context.ReportProgressAsync(75, "Cataloging imported episodes", cancellationToken);
        await materializer.MaterializeAsync(context, materialization, cancellationToken);

        // Keep the aggregate scan for snapshot/stale cleanup and sidecars. Readiness no longer depends
        // on this singleton job being accepted or finishing before the acquisition status changes.
        await context.ReportProgressAsync(90, "Scheduling library housekeeping", cancellationToken);
        try {
            await context.EnqueueIfNeededAsync(
                new EnqueueJobRequest(JobType.ScanLibrary, TargetLabel: "Imported episode scan"),
                cancellationToken);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning(ex, "TV import completed readiness but could not queue optional library housekeeping.");
        }

        await torrents.HandleImportedAsync(import, importMode, cancellationToken);
        // No fallible work follows the terminal commit. Otherwise a progress-store outage could make the
        // job handler downgrade an already-ready, checkpoint-cleared acquisition from Imported to Failed.
        await context.ReportProgressAsync(100, "Finalizing import", cancellationToken);
        await acquisitions.MarkImportedWithQualityAsync(import.Id, BookQualityRank.Floor, message, cancellationToken, qualityCode, ownedMediaRevision, ownedFormatScore);
    }

    private Task<string?> ReplaceOwnedAsync(
        string ownedFilePath,
        string sourceAbsolute,
        ImportMode importMode,
        bool allowFormatChange,
        string? retainedBackupPath,
        string? incomingEvidencePath,
        CancellationToken cancellationToken) =>
        MergedImportExecution.ReplaceOwnedAsync(
            replacer,
            logger,
            ownedFilePath,
            sourceAbsolute,
            importMode,
            cancellationToken,
            allowFormatChange,
            retainBackup: true,
            retainedBackupPath,
            incomingEvidencePath);

    private static void RetirePreviousFormatPath(string? previousFilePath, string replacementPath) {
        if (string.IsNullOrWhiteSpace(previousFilePath)) {
            return;
        }

        var previous = Path.GetFullPath(previousFilePath);
        var replacement = Path.GetFullPath(replacementPath);
        if (FileSystemPathComparison.Equals(previous, replacement) || !File.Exists(previous)) {
            return;
        }

        File.Delete(previous);
        if (File.Exists(previous)) {
            throw new IOException($"The previous video format could not be retired after installing {replacement}.");
        }
    }

    private static string CrossFormatTargetConflictMessage(string targetPath) =>
        $"The format-change target already exists in the library ({Path.GetFileName(targetPath)}). " +
        "Review the conflicting file before retrying this import; Prismedia did not overwrite it.";

    private Task HandleNothingUsableAsync(
        AcquisitionImportContext import, SelectedRelease? selected, bool hasFormatChange, CancellationToken cancellationToken) =>
        MergedImportExecution.FailNothingUsableAsync(
            acquisitions, blocklist, history, torrents, logger, import, selected, hasFormatChange,
            "The release upgrades existing episodes but changes the file format; import it manually.",
            cancellationToken);

    private static string SeriesOf(AcquisitionImportContext import) =>
        string.IsNullOrWhiteSpace(import.Series) ? import.Title : import.Series;

    private static string? NormalizeClientItemId(string? clientItemId) =>
        string.IsNullOrWhiteSpace(clientItemId) ? null : clientItemId;

    private async Task<LibraryRootData?> ResolveOwningVideoRootAsync(
        string seriesFolderPath,
        CancellationToken cancellationToken) {
        var seriesFolder = Path.GetFullPath(seriesFolderPath);
        return (await roots.GetEnabledRootsAsync(cancellationToken))
            .Where(root => root.ScanVideos && IsAtOrUnderFolder(seriesFolder, Path.GetFullPath(root.Path)))
            .OrderByDescending(root => Path.GetFullPath(root.Path).Length)
            .FirstOrDefault();
    }

    private static bool IsAtOrUnderFolder(string candidate, string folder) =>
        FileSystemPathComparison.Equals(candidate, folder) || IsUnderFolder(candidate, folder);

    private static bool IsUnderFolder(string candidate, string folder) {
        var normalized = folder.EndsWith(Path.DirectorySeparatorChar) ? folder : folder + Path.DirectorySeparatorChar;
        return candidate.StartsWith(normalized, FileSystemPathComparison.Comparison);
    }

    private static string BlockMessage(ImportBlockReason? reason) => reason == ImportBlockReason.NoSupportedPayload
        ? "The download contains no supported video files."
        : "The download's files carry no recognizable episode numbering; import manually.";

    private Task Fail(Guid acquisitionId, string message, CancellationToken cancellationToken) =>
        acquisitions.SetStatusAsync(acquisitionId, AcquisitionStatus.Failed, message, cancellationToken);
}

/// <summary>
/// Music import engine: places the album release's audio files (and cover art) under
/// <c>{Artist}/{Album}/</c> in the first audio-enabled library root, preserving disc-folder structure,
/// writes the identify hint keyed on the album folder, and chains an audio scan — which binds the album
/// and artist folders to their wanted entities via the acquisition hint. The profile's import mode controls
/// whether the payload is moved, copied, or hardlinked before cleanup or seeding watch.
/// </summary>
public sealed class MusicAcquisitionImportEngine(
    IAcquisitionStore acquisitions,
    IBookAcquisitionProfileStore profiles,
    ILibraryScanRootPersistence roots,
    IDownloadPayloadReader payloads,
    IImportFileMover mover,
    ImportedTorrentRemover torrents,
    IImportTargetIndex targets,
    IAcquisitionBlocklistStore blocklist,
    IAcquisitionHistoryStore history,
    IImportedEntityMaterializer materializer,
    ILogger<MusicAcquisitionImportEngine> logger) : IAcquisitionImportEngine {
    public EntityKind Kind => EntityKind.AudioLibrary;

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

            var resumed = await ImportPlacementExecution.ExecuteAsync(
                acquisitions,
                mover,
                import.Id,
                durableCheckpoint,
                cancellationToken);
            if (resumed is null) {
                return;
            }

            await FinalizeImportAsync(
                context,
                import,
                checkpointRoot,
                resumed.HintPath,
                ImportPlacementExecution.MediaPaths(resumed),
                resumed.ImportMode,
                resumed.SuccessMessage,
                cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(import.ContentPath) || payloads.Read(import.ContentPath) is not { } payload) {
            await Fail(import.Id, "The completed download reported no content path.", cancellationToken);
            return;
        }

        var artist = string.IsNullOrWhiteSpace(import.Author) ? "Unknown Artist" : import.Author;
        var rawPlan = MusicImportPlanBuilder.Plan(payload.Files, artist, import.Title, profile?.PathTemplate, import.Year);
        if (rawPlan.Blocked) {
            await acquisitions.SetStatusAsync(
                import.Id, AcquisitionStatus.ManualImportRequired,
                "The download contains no supported audio files.", cancellationToken);
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
        var albumFolder = Path.GetFullPath(Path.Combine(root.Path, MusicImportPlanBuilder.AlbumFolderRelative(artist, import.Title, profile?.PathTemplate, import.Year)));
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
            completed.ImportMode,
            completed.SuccessMessage,
            cancellationToken);
    }

    /// <summary>
    /// The existing album folder to merge into: the album's own on-disk folder when it has one, else a
    /// template-named album folder INSIDE the existing artist folder (never a second artist folder).
    /// Null when neither container exists on disk — the caller keeps the template placement.
    /// </summary>
    private static string? ExistingAlbumFolderOf(AlbumDiskTarget target, string artist, AcquisitionImportContext import, BookImportProfile? profile) {
        if (target.AlbumFolderPath is { } albumFolder && Directory.Exists(albumFolder)) {
            return albumFolder;
        }

        if (target.ArtistFolderPath is { } artistFolder && Directory.Exists(artistFolder)) {
            var albumSegment = MusicImportPlanBuilder
                .AlbumFolderRelative(artist, import.Title, profile?.PathTemplate, import.Year)
                .Split('/')[^1];
            return Path.Combine(artistFolder, albumSegment);
        }

        return null;
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
                    new ResolvedImportItem(sourceAbsolute, Path.GetFullPath(item.TargetAbsolutePath)),
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
            completed.ImportMode,
            completed.SuccessMessage,
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

    /// <summary>The shared success tail: hint, final source path, scan chain, torrent handling, and the imported mark.</summary>
    private async Task FinalizeImportAsync(
        JobContext context,
        AcquisitionImportContext import,
        LibraryRootData root,
        string albumFolder,
        IReadOnlyList<string> placedMediaPaths,
        ImportMode importMode,
        string message,
        CancellationToken cancellationToken) {
        // The owned quality is the audio-ladder code (and PROPER/REPACK revision) from the selected release.
        // An album is multi-file, so its monitor fulfills on import (no single-file swap); the code is captured
        // for display only.
        var selected = await acquisitions.GetSelectedReleaseAsync(import.Id, cancellationToken);
        var ownedMediaQuality = selected is null ? null : MediaQualityLadder.Detect(EntityKind.AudioLibrary, selected.Title).Code;
        var ownedMediaRevision = selected is null ? 1 : ReleaseRevisionDetection.Detect(selected.Title);
        var ownedFormatScore = await OwnedFormatScore.ComputeAsync(profiles, import.ProfileId, EntityKind.AudioLibrary, selected, cancellationToken);

        await acquisitions.WriteImportHintAsync(import.Id, albumFolder, import, BookQualityRank.Floor, cancellationToken);
        await acquisitions.SetFinalSourcePathAsync(import.Id, albumFolder, cancellationToken);

        await context.ReportProgressAsync(80, "Cataloging imported album", cancellationToken);
        await materializer.MaterializeAsync(
            import.Kind,
            context,
            new ImportedEntityMaterializationRequest(import.Id, import.EntityId, root, placedMediaPaths),
            cancellationToken);
        await ImportRootResolution.EnqueueReconciliationAsync(
            context, JobType.ScanAudio, root, "Imported album scan", logger, cancellationToken);

        await torrents.HandleImportedAsync(import, importMode, cancellationToken);
        await acquisitions.MarkImportedWithQualityAsync(import.Id, BookQualityRank.Floor, message, cancellationToken, ownedMediaQuality, ownedMediaRevision, ownedFormatScore);
    }

    private Task Fail(Guid acquisitionId, string message, CancellationToken cancellationToken) =>
        acquisitions.SetStatusAsync(acquisitionId, AcquisitionStatus.Failed, message, cancellationToken);
}
