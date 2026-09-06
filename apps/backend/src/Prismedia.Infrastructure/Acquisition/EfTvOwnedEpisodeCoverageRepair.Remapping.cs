using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Repairs unreviewed filename-derived owners using retained automatic-import evidence.</summary>
public sealed partial class EfTvOwnedEpisodeCoverageRepair {
    private async Task<int> RepairIncorrectOwnersAsync(Guid monitorId, Guid destinationId,
        Func<Guid, CancellationToken, Task> enqueue, CancellationToken token) {
        var proposed = await ReadRemappingCandidatesAsync(monitorId, destinationId, token);
        if (proposed.Count == 0) return 0;
        var ids = proposed.SelectMany(candidate => candidate.Plan.Episodes.Select(episode => episode.EntityId!.Value)
            .Append(candidate.Owner.Id)).Append(destinationId).Distinct().ToArray();
        var restored = 0;
        await lifecycle.ExecuteManyAsync(ids, async leaseToken => {
            var current = await ReadRemappingCandidatesAsync(monitorId, destinationId, leaseToken);
            var accepted = current.Where(candidate => proposed.Any(previous =>
                JsonSerializer.Serialize(previous) == JsonSerializer.Serialize(candidate))).ToArray();
            var contested = accepted.SelectMany(candidate => candidate.Plan.Episodes.Select(episode => episode.EntityId!.Value))
                .GroupBy(id => id).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
            foreach (var candidate in accepted.Where(candidate => !candidate.Plan.Episodes.Any(episode => contested.Contains(episode.EntityId!.Value)))) {
                if (Observe(candidate.Path) != candidate.Observation) continue;
                var owner = await db.Entities.SingleAsync(row => row.Id == candidate.Owner.Id, leaseToken);
                await db.Entry(owner).ReloadAsync(leaseToken);
                var source = await db.EntityFiles.SingleAsync(row => row.Id == candidate.Source.Id, leaseToken);
                await db.Entry(source).ReloadAsync(leaseToken);
                var now = DateTimeOffset.UtcNow;
                var episodeIds = candidate.Plan.Episodes.Select(episode => episode.EntityId!.Value).ToArray();
                var episodes = await db.Entities.Where(row => episodeIds.Contains(row.Id)).ToArrayAsync(leaseToken);
                foreach (var episode in episodes) await db.Entry(episode).ReloadAsync(leaseToken);
                if (source.EntityId != owner.Id || source.Path != candidate.Path || episodes.Length != episodeIds.Length
                    || episodes.Any(episode => !episode.IsWanted || episode.ParentEntityId != destinationId)) continue;

                // Persist the original Source row's new owner before retiring the disposable placeholder.
                // The surrounding lifecycle transaction also owns follow-up queue publication: a failure
                // rolls back every binding and the retirement. No filesystem operation participates.
                source.EntityId = episodeIds[0];
                source.UpdatedAt = now;
                foreach (var id in episodeIds.Skip(1)) {
                    db.EntityFiles.Add(new EntityFileRow {
                        Id = Guid.NewGuid(), EntityId = id, Role = EntityFileRole.Source, Path = candidate.Path,
                        SizeBytes = candidate.Observation.Length, MimeType = candidate.Source.MimeType,
                        Source = FileSourceKind.Scan.ToCode(), CreatedAt = now, UpdatedAt = now
                    });
                }
                foreach (var episode in episodes) {
                    episode.IsWanted = false;
                    episode.UpdatedAt = now;
                }
                await InvalidateRemappedSourceEvidenceAsync(episodeIds, leaseToken);
                await db.SaveChangesAsync(leaseToken);
                db.Entities.Remove(owner);
                db.AcquisitionHistory.Add(new AcquisitionHistoryRow {
                    Id = Guid.NewGuid(), AcquisitionId = candidate.ReceiptId, EntityId = destinationId,
                    Kind = EntityKind.VideoSeason, Event = AcquisitionHistoryEvent.MappingRepaired,
                    Title = candidate.SeriesTitle, CreatedAt = now,
                    Message = $"Repaired episode coverage from {owner.Title} to "
                        + string.Join(", ", candidate.Plan.Episodes.Select(episode =>
                            $"S{candidate.Plan.SeasonNumber:00}E{episode.Episode:00} — {episode.Title}"))
                        + ". The existing file was retained without moving or replacing it."
                });
                await db.SaveChangesAsync(leaseToken);
                foreach (var id in episodeIds) await enqueue(id, leaseToken);
                restored += episodeIds.Length;
            }
        }, token);
        return restored;
    }

    private async Task<IReadOnlyList<RemappingCandidate>> ReadRemappingCandidatesAsync(Guid monitorId, Guid destinationId,
        CancellationToken token) {
        if (!await db.Monitors.AsNoTracking().AnyAsync(row => row.Id == monitorId && row.EntityId == destinationId
                && row.Kind == EntityKind.VideoSeason && row.Status == MonitorStatus.Active, token)) return [];
        var destination = await db.Entities.AsNoTracking().SingleOrDefaultAsync(row => row.Id == destinationId, token);
        if (destination is not { SortOrder: > 0, ParentEntityId: { } seriesId }
            || destination.KindCode != EntityKind.VideoSeason.ToCode()) return [];
        var catalog = await targets.GetSeriesEpisodeCatalogAsync(destinationId, token);
        if (catalog.Count(season => season.SeasonEntityId == destinationId) != 1
            || catalog.Single(season => season.SeasonEntityId == destinationId).Episodes.Count(episode => episode.IsWanted) < 2) return [];
        var series = await db.Entities.AsNoTracking().SingleOrDefaultAsync(row => row.Id == seriesId, token);
        if (series?.KindCode != EntityKind.VideoSeries.ToCode()) return [];
        var layout = await targets.GetTvLayoutAsync(destinationId, token);
        if (layout is null) return [];
        var seasonIds = catalog.Select(season => season.SeasonEntityId).OfType<Guid>().ToArray();
        var episodeCode = EntityKind.VideoEpisode.ToCode();
        var owners = await db.Entities.AsNoTracking().Where(row => row.KindCode == episodeCode && row.ParentEntityId != null
            && seasonIds.Contains(row.ParentEntityId.Value) && !row.IsWanted && !row.IsOrganized).ToArrayAsync(token);
        var ownerIds = owners.Select(owner => owner.Id).ToArray();
        var sources = await db.EntityFiles.AsNoTracking().Where(row => ownerIds.Contains(row.EntityId)
            && row.Role == EntityFileRole.Source).ToArrayAsync(token);
        var ownersById = owners.ToDictionary(owner => owner.Id);
        var candidates = sources.Where(source => {
            var owner = ownersById[source.EntityId];
            return owner.Title == Path.GetFileNameWithoutExtension(source.Path)
                && TvReleaseTokens.ParseEpisodes(owner.Title) is { Episodes.Count: 1 } declared
                && owner.SortOrder == declared.Episodes[0];
        }).ToArray();
        // Most modern imports already have descriptive canonical owners. Do not load their large
        // historical season ledgers on every monitor tick when no scanner placeholder is eligible.
        if (candidates.Length == 0) return [];
        var paths = candidates.Select(source => source.Path).Distinct().ToArray();
        var allOwners = await db.EntityFiles.AsNoTracking().Where(row => row.Role == EntityFileRole.Source && paths.Contains(row.Path)).ToArrayAsync(token);
        var receiptSeasonIds = candidates.Select(source => ownersById[source.EntityId].ParentEntityId!.Value).Distinct().ToArray();
        var receiptHistory = await db.Acquisitions.AsNoTracking().Where(row => row.EntityId != null
                && receiptSeasonIds.Contains(row.EntityId.Value) && row.ImportResultJson != null)
            .OrderByDescending(row => row.UpdatedAt).ThenByDescending(row => row.Id).ToArrayAsync(token);
        var receipts = receiptHistory
            .GroupBy(row => row.EntityId!.Value).ToDictionary(group => group.Key, group => group.First());
        var rootId = await db.EntityLibraryRoots.AsNoTracking().Where(row => row.EntityId == seriesId)
            .Select(row => row.LibraryRootId).SingleOrDefaultAsync(token);
        var alternatives = await EfAcquisitionWorkTitles.ReadAsync(db, seriesId, token);
        var result = new List<RemappingCandidate>();
        foreach (var source in candidates.OrderBy(source => source.Id)) {
            var owner = ownersById[source.EntityId];
            var declared = TvReleaseTokens.ParseEpisodes(owner.Title);
            if (owner.Title != Path.GetFileNameWithoutExtension(source.Path) || declared is null
                || declared.Value.Episodes.Count != 1 || owner.SortOrder != declared.Value.Episodes[0]
                || !layout.Seasons.TryGetValue(declared.Value.Season, out var sourceSeason)
                || sourceSeason.SeasonEntityId != owner.ParentEntityId || sourceSeason.FolderPath is null
                || allOwners.Count(row => row.Path == source.Path) != 1
                || sources.Count(row => row.EntityId == owner.Id) != 1
                || !receipts.TryGetValue(sourceSeason.SeasonEntityId, out var receipt)) continue;
            if (receipt.Kind != EntityKind.VideoSeason || receipt.Status != AcquisitionStatus.Imported
                || receipt.ImportManualReview || receipt.ImportCheckpointJson != null || receipt.TeardownIntent != null
                || !AutomaticRelease(receipt.SelectedReleaseJson) || receipt.FinalSourcePath != sourceSeason.FolderPath) continue;
            var capturedRootId = rootId ?? receipt.TargetLibraryRootId;
            var root = await db.LibraryRoots.AsNoTracking().SingleOrDefaultAsync(row => row.Id == capturedRootId
                && row.Enabled && row.ScanVideos, token);
            if (root is null || receipt.TargetLibraryRootId is { } chosenRoot && chosenRoot != root.Id
                || !IsCurrentLibraryPath(root.Path, sourceSeason.FolderPath, source.Path)) continue;
            if (!AcquisitionImportFileLedgerJson.TryDeserialize(receipt.ImportResultJson, out var ledger)
                || ledger is not { Phase: AcquisitionImportPhase.Imported }) continue;
            var relative = Path.GetRelativePath(root.Path, source.Path).Replace('\\', '/');
            var entries = ledger.Files.Where(entry => entry.DestinationRelativePath is not null
                && FileSystemPathComparison.Equals(entry.DestinationRelativePath.Replace('\\', '/'), relative)).ToArray();
            if (entries.Length != 1 || entries[0] is not { Status: AcquisitionImportFileStatus.Imported,
                    Role: AcquisitionImportFileRole.Media, ContentKind: AcquisitionImportContentKind.Video,
                    SizeBytes: > 0 } entry) continue;
            var lineage = ReadRemappingReceiptLineage(receipt, entry, source, root.Id, receiptHistory);
            if (lineage is null) continue;
            var observation = Observe(source.Path);
            if (observation is null || observation.Length != entry.SizeBytes || source.SizeBytes != entry.SizeBytes
                || source.Source != FileSourceKind.Scan.ToCode() || source.CreatedAt > receipt.UpdatedAt
                || source.UpdatedAt > receipt.UpdatedAt || observation.LastWriteUtc > source.CreatedAt.UtcDateTime) continue;
            var plan = TvOwnedEpisodeCoveragePlanner.PlanReassignment(entry.SourceRelativePath, series.Title,
                declared.Value.Season, catalog, [owner.Id], alternatives);
            if (plan is null || plan.SeasonEntityId != destinationId) continue;
            var targetsIds = plan.Episodes.Select(episode => episode.EntityId!.Value).ToArray();
            if (await db.EntityFiles.AsNoTracking().AnyAsync(row => targetsIds.Contains(row.EntityId)
                && row.Role == EntityFileRole.Source, token) || await HasProtectedRemappingContentAsync(owner, declared.Value.Season, token)) continue;
            var scopeIds = targetsIds.Append(owner.Id).Append(destinationId).Append(sourceSeason.SeasonEntityId).Append(seriesId).ToArray();
            if (await db.Acquisitions.AsNoTracking().AnyAsync(row => row.Id != receipt.Id && row.EntityId != null
                && scopeIds.Contains(row.EntityId.Value) && (row.TeardownIntent != null
                    || row.Status != AcquisitionStatus.Imported && row.Status != AcquisitionStatus.Cancelled && row.Status != AcquisitionStatus.Failed
                    || row.Status == AcquisitionStatus.Imported && row.UpdatedAt >= receipt.UpdatedAt), token)) continue;
            // Provider rebinding must invalidate a proposal even when display titles stayed the same.
            var identities = await db.EntityExternalIds.AsNoTracking().Where(row => scopeIds.Contains(row.EntityId))
                .OrderBy(row => row.EntityId).ThenBy(row => row.Provider).ThenBy(row => row.Value).ToArrayAsync(token);
            var routes = await db.EntityProviderIdentities.AsNoTracking().Where(row => scopeIds.Contains(row.EntityId))
                .OrderBy(row => row.EntityId).ToArrayAsync(token);
            result.Add(new(source.Path, receipt.Id, receipt.UpdatedAt, receipt.ImportResultJson!, root.Path,
                series.Title, plan, owner, new(source.Id, source.EntityId, source.SizeBytes, source.MimeType,
                    source.Source, source.CreatedAt, source.UpdatedAt), observation, JsonSerializer.Serialize(new { identities, routes }), lineage));
        }
        return result;
    }

    private static string? ReadRemappingReceiptLineage(AcquisitionRow latest, AcquisitionImportFileLedgerEntry entry,
        EntityFileRow source, Guid rootId, IReadOnlyList<AcquisitionRow> history) {
        if (entry.Decision == AcquisitionImportDecision.PlaceNew) return string.Empty;
        if (entry.Decision != AcquisitionImportDecision.AdoptExisting) return null;
        var lineage = history.Where(row => row.EntityId == latest.EntityId && row.UpdatedAt >= source.CreatedAt
                && row.UpdatedAt <= latest.UpdatedAt).OrderBy(row => row.UpdatedAt).ThenBy(row => row.Id).ToArray();
        var origin = Array.FindIndex(lineage, row => row.CreatedAt <= source.CreatedAt
            && ReceiptEntry(row, entry.DestinationRelativePath!)?.Decision == AcquisitionImportDecision.PlaceNew);
        if (origin < 0) return null;
        var confirmed = lineage.Skip(origin).ToArray();
        for (var index = 0; index < confirmed.Length; index++) {
            var receipt = confirmed[index];
            var evidence = ReceiptEntry(receipt, entry.DestinationRelativePath!);
            if (receipt.Kind != EntityKind.VideoSeason || receipt.Status != AcquisitionStatus.Imported
                || receipt.ImportManualReview || receipt.ImportCheckpointJson != null || receipt.TeardownIntent != null
                || !AutomaticRelease(receipt.SelectedReleaseJson) || receipt.FinalSourcePath != latest.FinalSourcePath
                || receipt.TargetLibraryRootId is { } chosenRoot && chosenRoot != rootId
                || evidence is not { Role: AcquisitionImportFileRole.Media, ContentKind: AcquisitionImportContentKind.Video,
                    Status: AcquisitionImportFileStatus.Imported }
                || evidence.SizeBytes != entry.SizeBytes || evidence.SourceRelativePath != entry.SourceRelativePath
                || evidence.Decision != (index == 0 ? AcquisitionImportDecision.PlaceNew : AcquisitionImportDecision.AdoptExisting)) return null;
        }
        if (confirmed.LastOrDefault()?.Id != latest.Id) return null;
        // Keep the entire accepted chain in the lease snapshot. Editing or replacing an older receipt
        // must invalidate the repair just as changing the most recent adoption does.
        return JsonSerializer.Serialize(confirmed.Select(row => new {
            row.Id, row.CreatedAt, row.UpdatedAt, row.ImportResultJson, row.SelectedReleaseJson
        }));
    }

    private static AcquisitionImportFileLedgerEntry? ReceiptEntry(AcquisitionRow receipt, string relativePath) {
        if (!AcquisitionImportFileLedgerJson.TryDeserialize(receipt.ImportResultJson, out var ledger)
            || ledger is not { Phase: AcquisitionImportPhase.Imported }) return null;
        var entries = ledger.Files.Where(entry => entry.DestinationRelativePath is not null
            && FileSystemPathComparison.Equals(entry.DestinationRelativePath.Replace('\\', '/'), relativePath.Replace('\\', '/'))).ToArray();
        return entries.Length == 1 ? entries[0] : null;
    }

    private async Task<bool> HasProtectedRemappingContentAsync(EntityRow owner, int season, CancellationToken token) {
        var id = owner.Id;
        var allowedRoles = EntityKindRegistry.Describe(EntityKind.VideoEpisode).Processing.GeneratedFileRoles
            .Append(EntityFileRole.Source).ToArray();
        if (await db.EntityFiles.AsNoTracking().AnyAsync(row => row.EntityId == id
                && (row.Source != FileSourceKind.Scan.ToCode() || !allowedRoles.Contains(row.Role)), token)) return true;
        var target = id.ToString();
        if (await db.JobRuns.AsNoTracking().AnyAsync(row => row.TargetEntityId == target
            && (row.Status == JobRunStatus.Queued || row.Status == JobRunStatus.Running), token)) return true;
        return await db.Entities.AsNoTracking().AnyAsync(entity => entity.Id == id && (
            db.EntityExternalIds.Any(row => row.EntityId == id)
            || db.EntityProviderIdentities.Any(row => row.EntityId == id)
            || db.EntityDescriptions.Any(row => row.EntityId == id)
            || db.EntityRelationshipLinks.Any(row => row.EntityId == id || row.TargetEntityId == id)
            || db.EntityUrls.Any(row => row.EntityId == id)
            || db.EntityMarkers.Any(row => row.EntityId == id)
            || db.EntitySubtitles.Any(row => row.EntityId == id)
            || db.UserEntityStates.Any(row => row.EntityId == id || row.ProgressCurrentEntityId == id)
            || db.EntityConsumptionEvents.Any(row => row.EntityId == id)
            || db.EntityStats.Any(row => row.EntityId == id)
            || db.EntityDates.Any(row => row.EntityId == id)
            || db.EntitySources.Any(row => row.EntityId == id)
            || db.EntityPositions.Any(row => row.EntityId == id &&
                (row.Code != EntityPositionCodes.Episode && row.Code != EntityPositionCodes.Season
                    || row.Code == EntityPositionCodes.Episode && row.Value != owner.SortOrder
                    || row.Code == EntityPositionCodes.Season && row.Value != season))
            || db.EntityClassifications.Any(row => row.EntityId == id)
            || db.EntityLifetimes.Any(row => row.EntityId == id)
            || db.IdentifyResults.Any(row => row.EntityId == id)
            || db.IdentifyQueueItems.Any(row => row.EntityId == id)
            || db.FingerprintSubmissions.Any(row => row.EntityId == id)
            || db.Acquisitions.Any(row => row.EntityId == id)
            || db.AcquisitionImportHints.Any(row => row.EntityId == id)
            || db.AcquisitionHistory.Any(row => row.EntityId == id)
            || db.Monitors.Any(row => row.EntityId == id)
            || db.CollectionItemDetails.Any(row => row.CollectionEntityId == id || row.ItemEntityId == id)
            || db.CollectionDetails.Any(row => row.CoverItemEntityId == id)
            || db.GalleryDetails.Any(row => row.CoverImageEntityId == id)
            || db.Entities.Any(row => row.ParentEntityId == id)), token);
    }

    private async Task InvalidateRemappedSourceEvidenceAsync(Guid[] ids, CancellationToken token) {
        // A wanted episode can retain stale failed probes or hashes after its previous file vanished.
        // Refresh derived evidence for the newly attached source while preserving personal state,
        // canonical metadata, and manually supplied subtitles/artwork.
        db.MediaStreams.RemoveRange(await db.MediaStreams.Where(row => ids.Contains(row.EntityId)).ToArrayAsync(token));
        db.MediaSources.RemoveRange(await db.MediaSources.Where(row => ids.Contains(row.EntityId)).ToArrayAsync(token));
        db.EntityTechnical.RemoveRange(await db.EntityTechnical.Where(row => ids.Contains(row.EntityId)).ToArrayAsync(token));
        db.EntityFileFingerprints.RemoveRange(await db.EntityFileFingerprints.Where(row => ids.Contains(row.EntityId)).ToArrayAsync(token));
        db.EntitySubtitles.RemoveRange(await db.EntitySubtitles.Where(row => ids.Contains(row.EntityId)
            && row.Source != EntitySubtitleSource.Manual).ToArrayAsync(token));
        foreach (var state in await db.EntitySubtitleStates.Where(row => ids.Contains(row.EntityId)).ToArrayAsync(token)) {
            state.SubtitlesExtractedAt = null;
            state.SubtitleSidecarSignature = null;
        }
        var generatedRoles = EntityKindRegistry.Describe(EntityKind.VideoEpisode).Processing.GeneratedFileRoles.ToArray();
        db.EntityFiles.RemoveRange(await db.EntityFiles.Where(row => ids.Contains(row.EntityId)
            && row.Source == FileSourceKind.Scan.ToCode() && generatedRoles.Contains(row.Role)).ToArrayAsync(token));
        db.TrickplayInfos.RemoveRange(await db.TrickplayInfos.Where(row => ids.Contains(row.EntityId)).ToArrayAsync(token));
    }

    private sealed record RemappingCandidate(string Path, Guid ReceiptId, DateTimeOffset ReceiptUpdatedAt,
        string ReceiptJson, string RootPath, string SeriesTitle, TvOwnedEpisodeRemappingPlan Plan, EntityRow Owner,
        SourceEvidence Source, FileObservation Observation, string IdentitySnapshot, string ReceiptLineageSnapshot);
}
