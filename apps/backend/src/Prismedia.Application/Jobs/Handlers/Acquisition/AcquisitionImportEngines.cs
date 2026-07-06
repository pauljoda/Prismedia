using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs.Ports;
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
    ImportedTorrentRemover torrents) : IAcquisitionImportEngine {
    public EntityKind Kind => EntityKind.Book;

    public async Task ImportAsync(JobContext context, AcquisitionImportContext import, CancellationToken cancellationToken) {
        var profile = await profiles.GetImportProfileAsync(import.ProfileId, EntityKind.Book, cancellationToken);
        if (profile is null) {
            await Fail(import.Id, "No book acquisition profile is configured for import.", cancellationToken);
            return;
        }

        // A request-time library choice overrides the profile's target; an unsuitable choice falls back.
        var root = await ImportRootResolution.ResolveAsync(
            roots, import.TargetLibraryRootId, profile.TargetLibraryRootId, static candidate => candidate.ScanBooks, cancellationToken);
        if (root is null) {
            await Fail(import.Id, "The target library root is missing or not book-enabled.", cancellationToken);
            return;
        }

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

        await context.ReportProgressAsync(40, "Moving files", cancellationToken);
        var finalPaths = new List<string>(plan.Items.Count);
        foreach (var item in plan.Items) {
            finalPaths.Add(await mover.PlaceAsync(item, profile.ImportMode, cancellationToken));
        }

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

        var hintFolder = Path.GetDirectoryName(finalPaths[0]) ?? root.Path;
        await acquisitions.WriteImportHintAsync(import.Id, hintFolder, import, ownedQuality, cancellationToken);
        await acquisitions.SetFinalSourcePathAsync(import.Id, hintFolder, cancellationToken);

        await context.ReportProgressAsync(80, "Scanning library", cancellationToken);
        await context.EnqueueIfNeededAsync(new EnqueueJobRequest(JobType.ScanBook, TargetLabel: "Imported book scan"), cancellationToken);

        await torrents.HandleImportedAsync(import, profile.ImportMode, cancellationToken);

        await acquisitions.MarkImportedWithQualityAsync(import.Id, ownedQuality, "Imported into the library.", cancellationToken, ownedFormatScore: ownedFormatScore);
        await context.ReportProgressAsync(100, "Imported", cancellationToken);
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
    ImportedTorrentRemover torrents) : IAcquisitionImportEngine {
    public EntityKind Kind => EntityKind.Movie;

    public async Task ImportAsync(JobContext context, AcquisitionImportContext import, CancellationToken cancellationToken) {
        var profile = await profiles.GetImportProfileAsync(import.ProfileId, EntityKind.Movie, cancellationToken);
        var root = await ImportRootResolution.ResolveAsync(
            roots, import.TargetLibraryRootId, profile?.TargetLibraryRootId, static candidate => candidate.ScanVideos, cancellationToken);
        if (root is null) {
            await Fail(import.Id, "No enabled video library root exists to import the movie into.", cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(import.ContentPath) || payloads.Read(import.ContentPath) is not { } payload) {
            await Fail(import.Id, "The completed download reported no content path.", cancellationToken);
            return;
        }

        // The owned quality (and the {Quality} naming token) is the video-ladder code detected from the
        // selected release title. Detected before planning so the template can render it into the path.
        var selected = await acquisitions.GetSelectedReleaseAsync(import.Id, cancellationToken);
        var ownedMediaQuality = selected is null ? null : MediaQualityLadder.Detect(EntityKind.Movie, selected.Title).Code;

        var templateContext = new ImportTemplateContext(import.Title, import.Author, import.Year);
        var plan = ImportTargetResolver.Resolve(
            payload.ContentRoot, root.Path,
            MovieImportPlanBuilder.Plan(payload.Files, templateContext, profile?.PathTemplate, ownedMediaQuality));
        if (plan.Blocked) {
            await acquisitions.SetStatusAsync(import.Id, AcquisitionStatus.ManualImportRequired, BlockMessage(plan.BlockReason), cancellationToken);
            return;
        }

        await context.ReportProgressAsync(40, "Moving files", cancellationToken);
        var importMode = profile?.ImportMode ?? ImportMode.Move;
        var finalPaths = new List<string>(plan.Items.Count);
        foreach (var item in plan.Items) {
            finalPaths.Add(await mover.PlaceAsync(item, importMode, cancellationToken));
        }

        // Book quality axes don't apply to movies (they record the book floor).
        var ownedMediaRevision = selected is null ? 1 : ReleaseRevisionDetection.Detect(selected.Title);
        var ownedFormatScore = await OwnedFormatScore.ComputeAsync(profiles, import.ProfileId, EntityKind.Movie, selected, cancellationToken);

        var hintFolder = Path.GetDirectoryName(finalPaths[0]) ?? root.Path;
        await acquisitions.WriteImportHintAsync(import.Id, hintFolder, import, BookQualityRank.Floor, cancellationToken);
        await acquisitions.SetFinalSourcePathAsync(import.Id, hintFolder, cancellationToken);

        await context.ReportProgressAsync(80, "Scanning library", cancellationToken);
        await context.EnqueueIfNeededAsync(new EnqueueJobRequest(JobType.ScanLibrary, TargetLabel: "Imported movie scan"), cancellationToken);

        await torrents.HandleImportedAsync(import, importMode, cancellationToken);

        await acquisitions.MarkImportedWithQualityAsync(import.Id, BookQualityRank.Floor, "Imported into the library.", cancellationToken, ownedMediaQuality, ownedMediaRevision, ownedFormatScore);
        await context.ReportProgressAsync(100, "Imported", cancellationToken);
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
/// exact layout the video scan materializes a series hierarchy from, so the scan that follows binds
/// the wanted series, season, and episodes by folder path and position. One engine class serves both
/// TV acquisition units — season packs (<see cref="EntityKind.VideoSeason"/>) and single episodes
/// (<see cref="EntityKind.Video"/>) — since placement rules are identical at either granularity.
/// </summary>
public sealed class TvAcquisitionImportEngine(
    EntityKind kind,
    IAcquisitionStore acquisitions,
    IBookAcquisitionProfileStore profiles,
    ILibraryScanRootPersistence roots,
    IDownloadPayloadReader payloads,
    IImportFileMover mover,
    ImportedTorrentRemover torrents) : IAcquisitionImportEngine {
    public EntityKind Kind => kind;

    public async Task ImportAsync(JobContext context, AcquisitionImportContext import, CancellationToken cancellationToken) {
        var profile = await profiles.GetImportProfileAsync(import.ProfileId, import.Kind, cancellationToken);
        var root = await ImportRootResolution.ResolveAsync(
            roots, import.TargetLibraryRootId, profile?.TargetLibraryRootId, static candidate => candidate.ScanVideos, cancellationToken);
        if (root is null) {
            await Fail(import.Id, "No enabled video library root exists to import the episodes into.", cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(import.ContentPath) || payloads.Read(import.ContentPath) is not { } payload) {
            await Fail(import.Id, "The completed download reported no content path.", cancellationToken);
            return;
        }

        // The owned quality (and the {Quality} naming token) is the video-ladder code from the selected
        // release; both TV units detect on the video ladder. Detected before planning so the template can
        // render it into the path. A season pack captures it too, though its monitor fulfills on import —
        // a multi-file pack can't be single-file swapped — so the code is recorded for display but never
        // drives an upgrade.
        var selected = await acquisitions.GetSelectedReleaseAsync(import.Id, cancellationToken);
        var ownedMediaQuality = selected is null ? null : MediaQualityLadder.Detect(import.Kind, selected.Title).Code;

        var series = string.IsNullOrWhiteSpace(import.Series) ? import.Title : import.Series;
        var plan = ImportTargetResolver.Resolve(
            payload.ContentRoot, root.Path,
            TvImportPlanBuilder.Plan(payload.Files, series, import.SeasonNumber, import.EpisodeNumber, profile?.PathTemplate, ownedMediaQuality));
        if (plan.Blocked) {
            await acquisitions.SetStatusAsync(
                import.Id, AcquisitionStatus.ManualImportRequired,
                plan.BlockReason == ImportBlockReason.NoSupportedPayload
                    ? "The download contains no supported video files."
                    : "The download's files carry no recognizable episode numbering; import manually.",
                cancellationToken);
            return;
        }

        await context.ReportProgressAsync(40, "Moving files", cancellationToken);
        var importMode = profile?.ImportMode ?? ImportMode.Move;
        var finalPaths = new List<string>(plan.Items.Count);
        foreach (var item in plan.Items) {
            finalPaths.Add(await mover.PlaceAsync(item, importMode, cancellationToken));
        }

        // The hint keys on the most specific folder that covers every placed file: the season folder
        // for a single-season payload, the series folder when a pack spans seasons — so the scan's
        // wanted binds (series by ancestor, season and episodes by position) all fall under it.
        var seasonFolders = finalPaths
            .Select(path => Path.GetDirectoryName(path) ?? root.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var hintFolder = seasonFolders.Length == 1
            ? seasonFolders[0]
            : Path.GetFullPath(Path.Combine(root.Path, TvImportPlanBuilder.SeriesFolderRelative(series, profile?.PathTemplate)));
        var ownedMediaRevision = selected is null ? 1 : ReleaseRevisionDetection.Detect(selected.Title);
        var ownedFormatScore = await OwnedFormatScore.ComputeAsync(profiles, import.ProfileId, import.Kind, selected, cancellationToken);

        await acquisitions.WriteImportHintAsync(import.Id, hintFolder, import, BookQualityRank.Floor, cancellationToken);
        await acquisitions.SetFinalSourcePathAsync(import.Id, hintFolder, cancellationToken);

        await context.ReportProgressAsync(80, "Scanning library", cancellationToken);
        await context.EnqueueIfNeededAsync(new EnqueueJobRequest(JobType.ScanLibrary, TargetLabel: "Imported episode scan"), cancellationToken);

        await torrents.HandleImportedAsync(import, importMode, cancellationToken);
        await acquisitions.MarkImportedWithQualityAsync(import.Id, BookQualityRank.Floor, "Imported into the library.", cancellationToken, ownedMediaQuality, ownedMediaRevision, ownedFormatScore);
        await context.ReportProgressAsync(100, "Imported", cancellationToken);
    }

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
    ImportedTorrentRemover torrents) : IAcquisitionImportEngine {
    public EntityKind Kind => EntityKind.AudioLibrary;

    public async Task ImportAsync(JobContext context, AcquisitionImportContext import, CancellationToken cancellationToken) {
        var profile = await profiles.GetImportProfileAsync(import.ProfileId, EntityKind.AudioLibrary, cancellationToken);
        var root = await ImportRootResolution.ResolveAsync(
            roots, import.TargetLibraryRootId, profile?.TargetLibraryRootId, static candidate => candidate.ScanAudio, cancellationToken);
        if (root is null) {
            await Fail(import.Id, "No enabled audio library root exists to import the album into.", cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(import.ContentPath) || payloads.Read(import.ContentPath) is not { } payload) {
            await Fail(import.Id, "The completed download reported no content path.", cancellationToken);
            return;
        }

        var artist = string.IsNullOrWhiteSpace(import.Author) ? "Unknown Artist" : import.Author;
        var plan = ImportTargetResolver.Resolve(
            payload.ContentRoot, root.Path, MusicImportPlanBuilder.Plan(payload.Files, artist, import.Title, profile?.PathTemplate, import.Year));
        if (plan.Blocked) {
            await acquisitions.SetStatusAsync(
                import.Id, AcquisitionStatus.ManualImportRequired,
                "The download contains no supported audio files.", cancellationToken);
            return;
        }

        await context.ReportProgressAsync(40, "Moving files", cancellationToken);
        var importMode = profile?.ImportMode ?? ImportMode.Move;
        foreach (var item in plan.Items) {
            await mover.PlaceAsync(item, importMode, cancellationToken);
        }

        // The hint and final path key on the ALBUM folder (not a disc subfolder a track landed in), so
        // the audio scan's album upsert path matches the bind exactly.
        var albumFolder = Path.GetFullPath(Path.Combine(root.Path, MusicImportPlanBuilder.AlbumFolderRelative(artist, import.Title, profile?.PathTemplate, import.Year)));
        // The owned quality is the audio-ladder code (and PROPER/REPACK revision) from the selected release.
        // An album is multi-file, so its monitor fulfills on import (no single-file swap); the code is captured
        // for display only.
        var selected = await acquisitions.GetSelectedReleaseAsync(import.Id, cancellationToken);
        var ownedMediaQuality = selected is null ? null : MediaQualityLadder.Detect(EntityKind.AudioLibrary, selected.Title).Code;
        var ownedMediaRevision = selected is null ? 1 : ReleaseRevisionDetection.Detect(selected.Title);
        var ownedFormatScore = await OwnedFormatScore.ComputeAsync(profiles, import.ProfileId, EntityKind.AudioLibrary, selected, cancellationToken);

        await acquisitions.WriteImportHintAsync(import.Id, albumFolder, import, BookQualityRank.Floor, cancellationToken);
        await acquisitions.SetFinalSourcePathAsync(import.Id, albumFolder, cancellationToken);

        await context.ReportProgressAsync(80, "Scanning library", cancellationToken);
        await context.EnqueueIfNeededAsync(new EnqueueJobRequest(JobType.ScanAudio, TargetLabel: "Imported album scan"), cancellationToken);

        await torrents.HandleImportedAsync(import, importMode, cancellationToken);
        await acquisitions.MarkImportedWithQualityAsync(import.Id, BookQualityRank.Floor, "Imported into the library.", cancellationToken, ownedMediaQuality, ownedMediaRevision, ownedFormatScore);
        await context.ReportProgressAsync(100, "Imported", cancellationToken);
    }

    private Task Fail(Guid acquisitionId, string message, CancellationToken cancellationToken) =>
        acquisitions.SetStatusAsync(acquisitionId, AcquisitionStatus.Failed, message, cancellationToken);
}
