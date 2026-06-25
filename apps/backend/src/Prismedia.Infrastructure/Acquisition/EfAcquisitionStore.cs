using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>EF-backed store for acquisition records and their scored release candidates.</summary>
public sealed class EfAcquisitionStore(PrismediaDbContext db) : IAcquisitionStore {
    public async Task<AcquisitionSummary> CreateAsync(AcquisitionMetadata metadata, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var row = new AcquisitionRow {
            Id = Guid.NewGuid(),
            RequestHistoryId = metadata.RequestHistoryId,
            Status = AcquisitionStatus.Pending,
            Title = metadata.Title,
            Author = metadata.Author,
            Series = metadata.Series,
            Year = metadata.Year,
            PosterUrl = metadata.PosterUrl,
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
            .Select(row => new { row.Id, row.Title, row.Author })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : new AcquisitionSearchInput(row.Id, row.Title, row.Author);
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
            progress, row.CreatedAt, row.UpdatedAt);

    private static ReleaseCandidateView ToView(ReleaseCandidateRow row) =>
        new(row.Id, row.IndexerName, row.Title, row.SizeBytes, row.Seeders, row.Peers, row.Protocol, row.Accepted,
            row.Score, DecodeRejections(row.RejectionsJson), row.InfoUrl, row.PublishedAt);

    private static IReadOnlyList<ReleaseRejectionReason> DecodeRejections(string json) {
        var codes = JsonSerializer.Deserialize<string[]>(json) ?? [];
        return codes.Select(code => code.DecodeAs<ReleaseRejectionReason>()).ToArray();
    }
}
