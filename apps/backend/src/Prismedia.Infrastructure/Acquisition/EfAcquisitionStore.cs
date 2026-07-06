using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>EF-backed store for acquisition records and their scored release candidates.</summary>
public sealed class EfAcquisitionStore(PrismediaDbContext db, IAcquisitionHistoryStore history, ILogger<EfAcquisitionStore> logger) : IAcquisitionStore {
    public async Task<AcquisitionSummary> CreateAsync(AcquisitionMetadata metadata, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var row = new AcquisitionRow {
            Id = Guid.NewGuid(),
            Kind = metadata.Kind,
            EntityId = metadata.EntityId,
            ProfileId = metadata.ProfileId,
            TargetLibraryRootId = metadata.TargetLibraryRootId,
            Status = AcquisitionStatus.Pending,
            Title = metadata.Title,
            Author = metadata.Author,
            Series = metadata.Series,
            SeasonNumber = metadata.SeasonNumber,
            EpisodeNumber = metadata.EpisodeNumber,
            Year = metadata.Year,
            PosterUrl = metadata.PosterUrl,
            Description = metadata.Description,
            PluginId = metadata.PluginId,
            PluginItemId = metadata.PluginItemId,
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Acquisitions.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return ToSummary(row, null);
    }

    public async Task<IReadOnlyList<AcquisitionSummary>> ListAsync(CancellationToken cancellationToken) {
        var rows = await db.Acquisitions
            .AsNoTracking()
            .OrderByDescending(row => row.CreatedAt)
            .ToArrayAsync(cancellationToken);
        var ids = rows.Select(row => row.Id).ToArray();
        var progress = await LatestProgressAsync(ids, cancellationToken);
        return rows.Select(row => ToSummary(row, progress.GetValueOrDefault(row.Id))).ToArray();
    }

    public async Task<AcquisitionDetail?> GetAsync(Guid id, CancellationToken cancellationToken) {
        var row = await db.Acquisitions.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (row is null) {
            return null;
        }

        var candidates = await db.ReleaseCandidates
            .AsNoTracking()
            .Where(candidate => candidate.AcquisitionId == id)
            .OrderByDescending(candidate => candidate.Accepted)
            .ThenByDescending(candidate => candidate.Score)
            .ToArrayAsync(cancellationToken);
        var progress = (await LatestProgressAsync([id], cancellationToken)).GetValueOrDefault(id);

        return new AcquisitionDetail(ToSummary(row, progress), candidates.Select(ToView).ToArray());
    }

    public async Task<AcquisitionSearchInput?> GetSearchInputAsync(Guid id, CancellationToken cancellationToken) {
        var row = await db.Acquisitions
            .AsNoTracking()
            .Where(row => row.Id == id)
            .Select(row => new { row.Id, row.Title, row.Author, row.Kind, row.EntityId, row.Year, row.ProfileId, row.Series, row.SeasonNumber, row.EpisodeNumber })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new AcquisitionSearchInput(
                row.Id, row.Title, row.Author, row.Kind, row.EntityId, row.Year, row.ProfileId,
                row.Series, row.SeasonNumber, row.EpisodeNumber);
    }

    public async Task<AcquisitionStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken) {
        var row = await db.Acquisitions
            .AsNoTracking()
            .Where(row => row.Id == id)
            .Select(row => (AcquisitionStatus?)row.Status)
            .FirstOrDefaultAsync(cancellationToken);
        return row;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) {
        var row = await db.Acquisitions.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (row is null) {
            return false;
        }

        // Candidates, transfers, and import hints cascade on the acquisition FK.
        db.Acquisitions.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task SetStatusAsync(Guid id, AcquisitionStatus status, string? message, CancellationToken cancellationToken) {
        var row = await db.Acquisitions.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (row is null) {
            return;
        }

        row.Status = status;
        row.StatusMessage = message;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<UpgradeOwnedQuality?> GetUpgradeOwnedQualityAsync(Guid acquisitionId, CancellationToken cancellationToken) {
        var parentId = await db.Acquisitions.AsNoTracking()
            .Where(row => row.Id == acquisitionId)
            .Select(row => row.UpgradeOfAcquisitionId)
            .FirstOrDefaultAsync(cancellationToken);
        if (parentId is not { } id) {
            return null;
        }

        // The parent carries owned quality in its kind's vocabulary; the child inherits the parent's kind, so a
        // media parent populates the ladder code and a book parent the source/format rank. Reading BOTH and
        // discriminating by kind keeps this one query, symmetric with CreateUpgradeChildAsync copying the parent.
        var parent = await db.Acquisitions.AsNoTracking()
            .Where(row => row.Id == id)
            .Select(row => new { row.Kind, row.OwnedSourceTier, row.OwnedFormatTier, row.OwnedMediaQuality, row.OwnedMediaRevision, row.OwnedFormatScore })
            .FirstOrDefaultAsync(cancellationToken);
        if (parent is null) {
            return null;
        }

        return MediaQualityLadder.IsUpgradeCapableKind(parent.Kind)
            ? new UpgradeOwnedQuality(null, parent.OwnedMediaQuality, parent.OwnedMediaRevision, parent.OwnedFormatScore)
            : new UpgradeOwnedQuality(new BookQualityRank(parent.OwnedSourceTier, parent.OwnedFormatTier), null, FormatScore: parent.OwnedFormatScore);
    }

    public async Task<UpgradeReplaceTarget?> GetUpgradeReplaceTargetAsync(Guid childId, CancellationToken cancellationToken) {
        var child = await db.Acquisitions.AsNoTracking().FirstOrDefaultAsync(row => row.Id == childId, cancellationToken);
        if (child is null || child.UpgradeOfAcquisitionId is not { } parentId) {
            return null;
        }

        var parent = await db.Acquisitions.AsNoTracking().FirstOrDefaultAsync(row => row.Id == parentId, cancellationToken);
        if (parent is null) {
            return null;
        }

        var transfer = await db.DownloadTransfers.AsNoTracking()
            .Where(row => row.AcquisitionId == childId)
            .OrderByDescending(row => row.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var selectedTitle = child.SelectedReleaseJson is { Length: > 0 } json
            ? JsonSerializer.Deserialize<SelectedRelease>(json)?.Title
            : null;

        return new UpgradeReplaceTarget(
            parentId,
            parent.FinalSourcePath,
            new BookQualityRank(parent.OwnedSourceTier, parent.OwnedFormatTier),
            selectedTitle,
            transfer?.ContentPath,
            transfer?.ClientItemId,
            transfer?.DownloadClientConfigId,
            parent.Kind,
            parent.OwnedMediaQuality,
            parent.OwnedMediaRevision,
            parent.ProfileId,
            parent.OwnedFormatScore);
    }

    public async Task EnrichMetadataAsync(Guid acquisitionId, string? description, string? posterUrl, int? year, CancellationToken cancellationToken) {
        var row = await db.Acquisitions.FirstOrDefaultAsync(row => row.Id == acquisitionId, cancellationToken);
        if (row is null) {
            return;
        }

        var changed = false;
        if (string.IsNullOrWhiteSpace(row.PosterUrl) && !string.IsNullOrWhiteSpace(posterUrl)) {
            row.PosterUrl = posterUrl;
            changed = true;
        }

        if (row.Year is null && year is not null) {
            row.Year = year;
            changed = true;
        }

        // Gap-only, like the other fields: fill a description only when none was captured at request time.
        // Length is not a reliable proxy for "better", so a held description from the search result is kept.
        if (string.IsNullOrWhiteSpace(row.Description) && !string.IsNullOrWhiteSpace(description)) {
            row.Description = description;
            changed = true;
        }

        if (changed) {
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task UpdateOwnedQualityAsync(Guid acquisitionId, BookQualityRank ownedQuality, CancellationToken cancellationToken) {
        var row = await db.Acquisitions.FirstOrDefaultAsync(row => row.Id == acquisitionId, cancellationToken);
        if (row is null) {
            return;
        }

        row.OwnedSourceTier = ownedQuality.Source;
        row.OwnedFormatTier = ownedQuality.Format;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateOwnedMediaQualityAsync(Guid acquisitionId, string ownedMediaQuality, int ownedMediaRevision, int ownedFormatScore, CancellationToken cancellationToken) {
        var row = await db.Acquisitions.FirstOrDefaultAsync(row => row.Id == acquisitionId, cancellationToken);
        if (row is null) {
            return;
        }

        row.OwnedMediaQuality = ownedMediaQuality;
        row.OwnedMediaRevision = ownedMediaRevision;
        row.OwnedFormatScore = ownedFormatScore;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkImportedWithQualityAsync(Guid id, BookQualityRank ownedQuality, string? message, CancellationToken cancellationToken, string? ownedMediaQuality = null, int ownedMediaRevision = 1, int ownedFormatScore = 0) {
        var row = await db.Acquisitions.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (row is null) {
            return;
        }

        row.Status = AcquisitionStatus.Imported;
        row.StatusMessage = message;
        row.OwnedSourceTier = ownedQuality.Source;
        row.OwnedFormatTier = ownedQuality.Format;
        // A media kind (movie/TV/music) records its ladder code and revision; book kinds leave both at the
        // default (null code, revision 1) and use the source/format tiers. The custom-format score is
        // captured for every kind so the format-score cutoff can advance regardless of ladder vocabulary.
        if (ownedMediaQuality is not null) {
            row.OwnedMediaQuality = ownedMediaQuality;
            row.OwnedMediaRevision = ownedMediaRevision;
        }

        row.OwnedFormatScore = ownedFormatScore;
        row.UpgradeQualityCaptured = true;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        // Durable Imported event: the single choke point for all four import engines. Record the quality in
        // the kind's vocabulary (the media ladder code for movies/TV/music, the source/format rank for books)
        // and the release that landed. Best-effort — a history hiccup must never fail the import.
        var selectedTitle = row.SelectedReleaseJson is { Length: > 0 } json
            ? JsonSerializer.Deserialize<SelectedRelease>(json)?.Title
            : null;
        var qualityCode = ownedMediaQuality ?? $"{ownedQuality.Source.ToCode()}/{ownedQuality.Format.ToCode()}";
        await history.SafeAddAsync(logger, new AcquisitionHistoryEntry(
            row.Id,
            row.EntityId,
            row.Kind,
            AcquisitionHistoryEvent.Imported,
            row.Title,
            selectedTitle,
            QualityCode: qualityCode,
            FormatScore: ownedFormatScore,
            Message: message),
            cancellationToken);
    }

    public async Task ReplaceCandidatesAsync(Guid id, IReadOnlyList<ScoredRelease> candidates, CancellationToken cancellationToken) {
        var existing = await db.ReleaseCandidates.Where(candidate => candidate.AcquisitionId == id).ToArrayAsync(cancellationToken);
        db.ReleaseCandidates.RemoveRange(existing);

        var now = DateTimeOffset.UtcNow;
        foreach (var scored in candidates) {
            var release = scored.Release;
            db.ReleaseCandidates.Add(new ReleaseCandidateRow {
                Id = Guid.NewGuid(),
                AcquisitionId = id,
                IndexerConfigId = scored.IndexerConfigId,
                IndexerName = scored.IndexerName,
                Title = release.Title,
                SizeBytes = release.SizeBytes,
                Seeders = release.Seeders,
                Peers = release.Peers,
                Protocol = release.Protocol,
                DownloadUrl = release.DownloadUrl,
                MagnetUrl = release.MagnetUrl,
                InfoHash = release.InfoHash,
                InfoUrl = release.InfoUrl,
                PublishedAt = release.PublishedAt,
                Score = scored.Score,
                Accepted = scored.Accepted,
                RejectionsJson = JsonSerializer.Serialize(scored.Rejections.Select(reason => reason.ToCode()).ToArray()),
                CreatedAt = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AcquisitionQueueCandidate?> GetQueueCandidateAsync(Guid acquisitionId, Guid candidateId, CancellationToken cancellationToken) {
        var row = await db.ReleaseCandidates
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == candidateId && candidate.AcquisitionId == acquisitionId, cancellationToken);
        return row is null
            ? null
            : new AcquisitionQueueCandidate(row.Id, row.Title, row.IndexerName, row.DownloadUrl, row.MagnetUrl, row.InfoHash, row.InfoUrl, row.Protocol, row.IndexerConfigId);
    }

    public async Task<IReadOnlyList<AcquisitionCandidateRef>> ListAcceptedCandidatesAsync(Guid acquisitionId, CancellationToken cancellationToken) {
        var rows = await db.ReleaseCandidates
            .AsNoTracking()
            .Where(candidate => candidate.AcquisitionId == acquisitionId && candidate.Accepted)
            .OrderByDescending(candidate => candidate.Score)
            .Select(candidate => new { candidate.Id, candidate.Title, candidate.IndexerName, candidate.InfoHash })
            .ToArrayAsync(cancellationToken);
        return rows.Select(row => new AcquisitionCandidateRef(row.Id, row.Title, row.IndexerName, row.InfoHash)).ToArray();
    }

    public async Task MarkCandidatesBlocklistedAsync(Guid acquisitionId, string identity, CancellationToken cancellationToken) {
        var rows = await db.ReleaseCandidates
            .Where(candidate => candidate.AcquisitionId == acquisitionId)
            .ToArrayAsync(cancellationToken);
        var blocklistedCode = ReleaseRejectionReason.Blocklisted.ToCode();
        var changed = false;

        foreach (var row in rows) {
            // Mark every row that resolves to the same release identity — a duplicate from another indexer
            // (e.g. the same info hash) must not stay selectable once the release is blocklisted.
            if (ReleaseIdentity.For(row.InfoHash, row.IndexerName, row.Title) != identity) {
                continue;
            }

            row.Accepted = false;
            var reasons = (JsonSerializer.Deserialize<string[]>(row.RejectionsJson) ?? []).ToList();
            if (!reasons.Contains(blocklistedCode)) {
                reasons.Add(blocklistedCode);
            }

            row.RejectionsJson = JsonSerializer.Serialize(reasons);
            changed = true;
        }

        if (changed) {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task SetSelectedReleaseAsync(Guid acquisitionId, SelectedRelease selected, CancellationToken cancellationToken) {
        var row = await db.Acquisitions.FirstOrDefaultAsync(row => row.Id == acquisitionId, cancellationToken);
        if (row is null) {
            return;
        }

        row.SelectedReleaseJson = JsonSerializer.Serialize(selected);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<SelectedRelease?> GetSelectedReleaseAsync(Guid acquisitionId, CancellationToken cancellationToken) {
        var json = await db.Acquisitions
            .AsNoTracking()
            .Where(row => row.Id == acquisitionId)
            .Select(row => row.SelectedReleaseJson)
            .FirstOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<SelectedRelease>(json);
    }

    public async Task CreateTransferAsync(Guid acquisitionId, Guid? downloadClientConfigId, string clientItemId, string? category, CancellationToken cancellationToken, TransferSeedGoal? seedGoal = null) {
        var now = DateTimeOffset.UtcNow;
        // One in-flight transfer per acquisition: re-queueing (after a failed/cancelled attempt) supersedes
        // any prior transfer. Leaving stale rows would make the monitor poll torrents that no longer exist
        // and wrongly fail the acquisition based on their ancient last-seen timestamps.
        var existing = await db.DownloadTransfers.Where(transfer => transfer.AcquisitionId == acquisitionId).ToListAsync(cancellationToken);
        if (existing.Count > 0) {
            db.DownloadTransfers.RemoveRange(existing);
        }

        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(),
            AcquisitionId = acquisitionId,
            DownloadClientConfigId = downloadClientConfigId,
            ClientItemId = clientItemId,
            Category = category,
            Progress = 0,
            SeedGoalRatio = seedGoal?.Ratio,
            SeedGoalTimeMinutes = seedGoal?.TimeMinutes,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SeedingTransfer>> ListSeedingTransfersAsync(CancellationToken cancellationToken) {
        var rows = await db.DownloadTransfers.AsNoTracking()
            .Where(transfer => transfer.SeedingSince != null)
            .ToArrayAsync(cancellationToken);
        return rows
            .Select(row => new SeedingTransfer(row.Id, row.AcquisitionId, row.DownloadClientConfigId, row.ClientItemId, row.SeedGoalRatio, row.SeedGoalTimeMinutes, row.SeedingSince!.Value))
            .ToArray();
    }

    public async Task<bool> MarkTransferSeedingAsync(Guid acquisitionId, DateTimeOffset since, CancellationToken cancellationToken) {
        var row = await db.DownloadTransfers.FirstOrDefaultAsync(transfer => transfer.AcquisitionId == acquisitionId, cancellationToken);
        // No goal captured at grab time means the client's own rules govern this torrent — no watch.
        if (row is null || (row.SeedGoalRatio is null && row.SeedGoalTimeMinutes is null)) {
            return false;
        }

        row.SeedingSince = since;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task ClearTransferSeedingAsync(Guid transferId, CancellationToken cancellationToken) {
        var row = await db.DownloadTransfers.FirstOrDefaultAsync(transfer => transfer.Id == transferId, cancellationToken);
        if (row is null) {
            return;
        }

        row.SeedingSince = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ActiveTransfer>> ListActiveTransfersAsync(CancellationToken cancellationToken) {
        var active = new[] { AcquisitionStatus.Queued, AcquisitionStatus.Downloading };
        var rows = await (
            from transfer in db.DownloadTransfers.AsNoTracking()
            join acquisition in db.Acquisitions.AsNoTracking() on transfer.AcquisitionId equals acquisition.Id
            where active.Contains(acquisition.Status)
            select new { transfer.Id, transfer.AcquisitionId, transfer.DownloadClientConfigId, transfer.ClientItemId, acquisition.Status, transfer.Progress, transfer.UpdatedAt, transfer.StalledSince })
            .ToArrayAsync(cancellationToken);
        return rows
            .Select(row => new ActiveTransfer(row.Id, row.AcquisitionId, row.DownloadClientConfigId, row.ClientItemId, row.Status, row.Progress, row.UpdatedAt, row.StalledSince))
            .ToArray();
    }

    public async Task<bool> HasActiveTransfersAsync(CancellationToken cancellationToken) {
        var active = new[] { AcquisitionStatus.Queued, AcquisitionStatus.Downloading };
        // Seeding watches keep the monitor scheduled after import, so seed goals are actually enforced.
        return await (
            from transfer in db.DownloadTransfers.AsNoTracking()
            join acquisition in db.Acquisitions.AsNoTracking() on transfer.AcquisitionId equals acquisition.Id
            where active.Contains(acquisition.Status) || transfer.SeedingSince != null
            select transfer.Id).AnyAsync(cancellationToken);
    }

    public async Task UpdateTransferAsync(Guid transferId, double progress, string? state, string? contentPath, CancellationToken cancellationToken) {
        var row = await db.DownloadTransfers.FirstOrDefaultAsync(transfer => transfer.Id == transferId, cancellationToken);
        if (row is null) {
            return;
        }

        row.Progress = progress;
        row.State = state;
        if (!string.IsNullOrWhiteSpace(contentPath)) {
            row.ContentPath = contentPath;
        }

        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkTransferStalledAsync(Guid transferId, DateTimeOffset? stalledSince, CancellationToken cancellationToken) {
        var row = await db.DownloadTransfers.FirstOrDefaultAsync(transfer => transfer.Id == transferId, cancellationToken);
        if (row is null) {
            return;
        }

        row.StalledSince = stalledSince;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AcquisitionImportContext?> GetImportContextAsync(Guid acquisitionId, CancellationToken cancellationToken) {
        var row = await db.Acquisitions.AsNoTracking().FirstOrDefaultAsync(row => row.Id == acquisitionId, cancellationToken);
        if (row is null) {
            return null;
        }

        var transfer = await db.DownloadTransfers
            .AsNoTracking()
            .Where(transfer => transfer.AcquisitionId == acquisitionId)
            .OrderByDescending(transfer => transfer.CreatedAt)
            .Select(transfer => new { transfer.ContentPath, transfer.ClientItemId, transfer.DownloadClientConfigId })
            .FirstOrDefaultAsync(cancellationToken);

        return new AcquisitionImportContext(
            row.Id, row.Title, row.Author, row.Series, row.Year, row.PosterUrl, row.PluginId, row.PluginItemId,
            row.ProfileId, transfer?.ContentPath, transfer?.ClientItemId, transfer?.DownloadClientConfigId, row.Description,
            row.Kind, row.TargetLibraryRootId, row.SeasonNumber, row.EpisodeNumber);
    }

    public async Task<AcquisitionTransferInfo?> GetTransferInfoAsync(Guid acquisitionId, CancellationToken cancellationToken) {
        var row = await db.Acquisitions
            .AsNoTracking()
            .Where(row => row.Id == acquisitionId)
            .Select(row => new { row.Status, row.FinalSourcePath })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null) {
            return null;
        }

        var transfer = await db.DownloadTransfers
            .AsNoTracking()
            .Where(transfer => transfer.AcquisitionId == acquisitionId)
            .OrderByDescending(transfer => transfer.CreatedAt)
            .Select(transfer => new { transfer.ClientItemId, transfer.DownloadClientConfigId })
            .FirstOrDefaultAsync(cancellationToken);

        return new AcquisitionTransferInfo(row.Status, row.FinalSourcePath, transfer?.ClientItemId, transfer?.DownloadClientConfigId);
    }

    public async Task SetFinalSourcePathAsync(Guid acquisitionId, string finalSourcePath, CancellationToken cancellationToken) {
        var row = await db.Acquisitions.FirstOrDefaultAsync(row => row.Id == acquisitionId, cancellationToken);
        if (row is null) {
            return;
        }

        row.FinalSourcePath = finalSourcePath;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task WriteImportHintAsync(Guid acquisitionId, string sourcePath, AcquisitionImportContext context, BookQualityRank ownedQuality, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var existing = await db.AcquisitionImportHints
            .Where(hint => hint.AcquisitionId == acquisitionId)
            .ToArrayAsync(cancellationToken);
        db.AcquisitionImportHints.RemoveRange(existing);

        // Carry the acquisition's wanted-entity link onto the path-keyed hint so the book scan can bind
        // the imported path to that entity instead of creating a duplicate.
        var wantedEntityId = await db.Acquisitions
            .AsNoTracking()
            .Where(row => row.Id == acquisitionId)
            .Select(row => row.EntityId)
            .FirstOrDefaultAsync(cancellationToken);

        var externalIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(context.PluginId) && !string.IsNullOrWhiteSpace(context.PluginItemId)) {
            externalIds[context.PluginId] = context.PluginItemId;
        }

        db.AcquisitionImportHints.Add(new AcquisitionImportHintRow {
            Id = Guid.NewGuid(),
            AcquisitionId = acquisitionId,
            EntityId = wantedEntityId,
            SourcePath = sourcePath,
            PluginId = context.PluginId,
            PluginItemId = context.PluginItemId,
            ExternalIdsJson = JsonSerializer.Serialize(externalIds),
            SourceUrlsJson = "[]",
            Title = context.Title,
            Author = context.Author,
            Series = context.Series,
            Year = context.Year,
            PosterUrl = context.PosterUrl,
            Description = context.Description,
            OwnedSourceTier = ownedQuality.Source,
            OwnedFormatTier = ownedQuality.Format,
            Consumed = false,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> AnyForEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
        await db.Acquisitions.AsNoTracking().AnyAsync(row => row.EntityId == entityId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListIdsForEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
        await db.Acquisitions.AsNoTracking()
            .Where(row => row.EntityId == entityId)
            .OrderByDescending(row => row.CreatedAt)
            .Select(row => row.Id)
            .ToArrayAsync(cancellationToken);

    public async Task<AcquisitionDetail?> GetLatestForEntityAsync(Guid entityId, CancellationToken cancellationToken) {
        var id = await db.Acquisitions
            .AsNoTracking()
            .Where(row => row.EntityId == entityId)
            .OrderByDescending(row => row.CreatedAt)
            .Select(row => (Guid?)row.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return id is { } acquisitionId ? await GetAsync(acquisitionId, cancellationToken) : null;
    }

    private async Task<Dictionary<Guid, double?>> LatestProgressAsync(IReadOnlyList<Guid> acquisitionIds, CancellationToken cancellationToken) {
        if (acquisitionIds.Count == 0) {
            return [];
        }

        // One in-flight transfer per acquisition in v1; surface its progress on the summary.
        var transfers = await db.DownloadTransfers
            .AsNoTracking()
            .Where(transfer => acquisitionIds.Contains(transfer.AcquisitionId))
            .GroupBy(transfer => transfer.AcquisitionId)
            .Select(group => new { AcquisitionId = group.Key, Progress = group.Max(transfer => transfer.Progress) })
            .ToArrayAsync(cancellationToken);
        return transfers.ToDictionary(transfer => transfer.AcquisitionId, transfer => (double?)transfer.Progress);
    }

    private static AcquisitionSummary ToSummary(AcquisitionRow row, double? progress) =>
        new(row.Id, row.Status, row.StatusMessage, row.Title, row.Author, row.Series, row.Year, row.PosterUrl,
            progress, row.CreatedAt, row.UpdatedAt, row.Description, row.Kind, row.EntityId);

    private static ReleaseCandidateView ToView(ReleaseCandidateRow row) =>
        new(row.Id, row.IndexerName, row.Title, row.SizeBytes, row.Seeders, row.Peers, row.Protocol, row.Accepted,
            row.Score, DecodeRejections(row.RejectionsJson), row.InfoUrl, row.PublishedAt);

    private static IReadOnlyList<ReleaseRejectionReason> DecodeRejections(string json) {
        var codes = JsonSerializer.Deserialize<string[]>(json) ?? [];
        return codes.Select(code => code.DecodeAs<ReleaseRejectionReason>()).ToArray();
    }
}
