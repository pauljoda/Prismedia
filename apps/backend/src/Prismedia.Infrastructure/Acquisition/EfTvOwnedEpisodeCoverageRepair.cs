using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Entities;
using Prismedia.Application.Files;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Validates import receipts and current ownership before restoring confidently proven shared-file coverage.</summary>
public sealed partial class EfTvOwnedEpisodeCoverageRepair(PrismediaDbContext db, IImportTargetIndex targets,
    IEntityLifecycleMutationLease lifecycle) : ITvOwnedEpisodeCoverageRepair {
    /// <inheritdoc />
    public async Task<int> RepairAsync(Guid monitorId, Guid seasonId,
        Func<Guid, CancellationToken, Task> enqueueReconciliation, CancellationToken cancellationToken) {
        var remapped = await RepairIncorrectOwnersAsync(monitorId, seasonId, enqueueReconciliation, cancellationToken);
        if (remapped > 0) return remapped;
        var proposed = await ReadCandidatesAsync(monitorId, seasonId, cancellationToken);
        if (proposed.Count == 0) return 0;
        var ids = proposed.SelectMany(candidate => candidate.Sources.Select(source => source.EntityId)
            .Concat(candidate.Plan.MissingEpisodes.Select(episode => episode.EntityId!.Value)))
            .Append(seasonId).Distinct().ToArray();
        var restored = 0;
        await lifecycle.ExecuteManyAsync(ids, async token => {
            // Re-read both the catalog and receipt inside the same lifecycle boundary as import and
            // monitor changes. A delayed plan must never bind a different positional episode.
            var current = await ReadCandidatesAsync(monitorId, seasonId, token);
            var accepted = current.Where(candidate => proposed.Any(previous => SameEvidence(previous, candidate))).ToArray();
            var missingIds = accepted.SelectMany(candidate => candidate.Plan.MissingEpisodes)
                .Select(episode => episode.EntityId!.Value).ToArray();
            // Competing files may each claim the same missing half. Preserve both for review.
            var contested = missingIds.GroupBy(id => id).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
            var queued = new List<Guid>();
            foreach (var candidate in accepted.Where(candidate => !candidate.Plan.MissingEpisodes.Any(episode => contested.Contains(episode.EntityId!.Value)))) {
                if (Observe(candidate.Path) != candidate.Observation) continue;
                foreach (var episode in candidate.Plan.MissingEpisodes) {
                    var entity = await db.Entities.SingleAsync(row => row.Id == episode.EntityId, token);
                    // Other work in this scoped context may have tracked the pre-lease entity.
                    await db.Entry(entity).ReloadAsync(token);
                    if (!entity.IsWanted || entity.ParentEntityId != seasonId) continue;
                    var source = candidate.Sources[0];
                    var now = DateTimeOffset.UtcNow;
                    db.EntityFiles.Add(new EntityFileRow {
                        Id = Guid.NewGuid(), EntityId = entity.Id, Role = EntityFileRole.Source,
                        Path = candidate.Path, SizeBytes = candidate.Observation.Length, MimeType = source.MimeType,
                        Source = FileSourceKind.Scan.ToCode(), CreatedAt = now, UpdatedAt = now
                    });
                    entity.IsWanted = false;
                    entity.UpdatedAt = now;
                    queued.Add(entity.Id);
                }
            }
            await db.SaveChangesAsync(token);
            foreach (var id in queued) await enqueueReconciliation(id, token);
            restored = queued.Count;
        }, cancellationToken);
        return restored;
    }

    private async Task<IReadOnlyList<Candidate>> ReadCandidatesAsync(Guid monitorId, Guid seasonId, CancellationToken token) {
        if (!await db.Monitors.AsNoTracking().AnyAsync(row => row.Id == monitorId && row.EntityId == seasonId
                && row.Kind == EntityKind.VideoSeason && row.Status == MonitorStatus.Active, token)) return [];
        var seasonCode = EntityKind.VideoSeason.ToCode();
        var season = await db.Entities.AsNoTracking().SingleOrDefaultAsync(row => row.Id == seasonId && row.KindCode == seasonCode, token);
        if (season is not { ParentEntityId: { } seriesId }) return [];
        var catalog = await targets.GetSeriesEpisodeCatalogAsync(seasonId, token);
        var requested = catalog.Where(item => item.SeasonEntityId == seasonId).ToArray();
        if (requested.Length != 1 || !requested[0].Episodes.Any(episode => episode.IsWanted)) return [];
        var layout = await targets.GetTvLayoutAsync(seasonId, token);
        var seasonNumber = requested[0].SeasonNumber;
        if (layout is null || !layout.Seasons.TryGetValue(seasonNumber, out var diskSeason)
            || diskSeason.SeasonEntityId != seasonId || diskSeason.HasUnresolvedOwnership
            || diskSeason.FolderPath is null) return [];
        var series = await db.Entities.AsNoTracking().SingleOrDefaultAsync(row => row.Id == seriesId, token);
        if (series?.KindCode != EntityKind.VideoSeries.ToCode()) return [];
        var episodeIds = requested[0].Episodes.Select(episode => episode.EntityId).OfType<Guid>().ToArray();
        var scopeIds = episodeIds.Append(seasonId).Append(seriesId).ToArray();
        // The newest receipt is selected BEFORE checking manual intent. A later manual replacement
        // must veto historical automatic evidence, never expose an older convenient receipt.
        var receipt = await db.Acquisitions.AsNoTracking()
            .Where(row => row.EntityId == seasonId && row.ImportResultJson != null)
            .OrderByDescending(row => row.UpdatedAt).ThenByDescending(row => row.Id).FirstOrDefaultAsync(token);
        // Older wanted-series imports may have folder provenance without a direct root association.
        // Their captured request root is usable only while it still exists and contains the current
        // season/file layout. Never substitute a current default or a guessed ancestor directory.
        var rootId = await db.EntityLibraryRoots.AsNoTracking().Where(row => row.EntityId == seriesId)
            .Select(row => row.LibraryRootId).SingleOrDefaultAsync(token) ?? receipt?.TargetLibraryRootId;
        var root = await db.LibraryRoots.AsNoTracking().SingleOrDefaultAsync(row => row.Id == rootId && row.Enabled, token);
        if (root is null) return [];
        if (receipt is null || receipt.Kind != EntityKind.VideoSeason || receipt.Status != AcquisitionStatus.Imported
            || receipt.ImportManualReview || receipt.ImportCheckpointJson != null || !AutomaticRelease(receipt.SelectedReleaseJson)
            || receipt.TeardownIntent != null || receipt.TargetLibraryRootId is { } chosenRoot && chosenRoot != root.Id
            || receipt.FinalSourcePath != diskSeason.FolderPath
            || await db.Acquisitions.AsNoTracking().AnyAsync(row => row.Id != receipt.Id
                && row.EntityId != null && scopeIds.Contains(row.EntityId.Value) && (row.TeardownIntent != null
                || row.Status != AcquisitionStatus.Imported && row.Status != AcquisitionStatus.Cancelled && row.Status != AcquisitionStatus.Failed
                || row.Status == AcquisitionStatus.Imported && row.UpdatedAt >= receipt.UpdatedAt), token)) return [];
        if (!AcquisitionImportFileLedgerJson.TryDeserialize(receipt.ImportResultJson, out var ledger)
            || ledger is not { Phase: AcquisitionImportPhase.Imported }) return [];

        var owned = await db.EntityFiles.AsNoTracking()
            .Where(file => episodeIds.Contains(file.EntityId) && file.Role == EntityFileRole.Source).ToArrayAsync(token);
        var paths = owned.Select(file => file.Path).Distinct().ToArray();
        var allOwners = await db.EntityFiles.AsNoTracking().Where(file => file.Role == EntityFileRole.Source && paths.Contains(file.Path))
            .ToArrayAsync(token);
        var alternativeWorkTitles = await EfAcquisitionWorkTitles.ReadAsync(db, seriesId, token);
        var result = new List<Candidate>();
        foreach (var path in paths.Order(FileSystemPathComparison.Comparer)) {
            token.ThrowIfCancellationRequested();
            if (layout.UnresolvedSourcePaths.Contains(path)
                || !IsCurrentLibraryPath(root.Path, diskSeason.FolderPath, path)) continue;
            var entries = ledger.Files.Where(file => file.DestinationRelativePath is not null
                && FileSystemPathComparison.Equals(file.DestinationRelativePath.Replace('\\', '/'), Path.GetRelativePath(root.Path, path).Replace('\\', '/'))).ToArray();
            if (entries.Length != 1 || entries[0] is not { Status: AcquisitionImportFileStatus.Imported,
                    Role: AcquisitionImportFileRole.Media, ContentKind: AcquisitionImportContentKind.Video,
                    Decision: AcquisitionImportDecision.PlaceNew, SizeBytes: > 0 } entry) continue;
            var sources = allOwners.Where(file => file.Path == path).OrderBy(file => file.Id).Select(file =>
                new SourceEvidence(file.Id, file.EntityId, file.SizeBytes, file.MimeType, file.Source, file.CreatedAt, file.UpdatedAt)).ToArray();
            var observation = Observe(path);
            if (observation is null || observation.Length != entry.SizeBytes
                || sources.Any(source => source.SizeBytes != entry.SizeBytes || source.Source != FileSourceKind.Scan.ToCode()
                    || source.CreatedAt > receipt.UpdatedAt || source.UpdatedAt > receipt.UpdatedAt
                    || observation.LastWriteUtc > source.CreatedAt.UtcDateTime)) continue;
            var plan = TvOwnedEpisodeCoveragePlanner.Plan(entry.SourceRelativePath, series.Title, seasonNumber,
                catalog, sources.Select(source => source.EntityId).ToArray(), alternativeWorkTitles);
            if (plan is null || plan.SeasonEntityId != seasonId
                || plan.MissingEpisodes.Any(episode => diskSeason.AmbiguousEpisodeNumbers.Contains(episode.Episode)
                    || owned.Any(file => file.EntityId == episode.EntityId))) continue;
            result.Add(new(path, receipt.Id, receipt.UpdatedAt, receipt.ImportResultJson!, series.Title,
                root.Path, plan, entry, sources, observation));
        }
        return result;
    }

    private static bool AutomaticRelease(string? json) {
        try { return json is not null && JsonSerializer.Deserialize<SelectedRelease>(json) is { ManualPick: false }; }
        catch (JsonException) { return false; }
    }

    private static bool IsCurrentLibraryPath(string root, string season, string path) {
        try {
            return Path.IsPathFullyQualified(path) && FileSystemPathComparison.IsSameOrDescendant(root, season)
                && FileSystemPathComparison.IsSameOrDescendant(season, path)
                && FileSystemPathComparison.Equals(Path.GetFullPath(path), CompletedPayloadFileSystem.CanonicalPath(path, rejectLeafLink: true));
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    private static FileObservation? Observe(string path) {
        try {
            var file = new FileInfo(path);
            return file.Exists && file.Length > 0 && file.LinkTarget is null ? new(file.Length, file.LastWriteTimeUtc) : null;
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    private static bool SameEvidence(Candidate previous, Candidate current) =>
        JsonSerializer.Serialize(previous) == JsonSerializer.Serialize(current);

    private sealed record FileObservation(long Length, DateTime LastWriteUtc);
    private sealed record SourceEvidence(Guid Id, Guid EntityId, long? SizeBytes, string? MimeType,
        string Source, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
    private sealed record Candidate(string Path, Guid ReceiptId, DateTimeOffset ReceiptUpdatedAt, string ReceiptJson,
        string SeriesTitle, string RootPath, TvOwnedEpisodeCoveragePlan Plan, AcquisitionImportFileLedgerEntry Entry,
        SourceEvidence[] Sources, FileObservation Observation);
}
