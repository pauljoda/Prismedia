using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

public sealed partial class EfAcquisitionStore {
    private void AddCandidates(Guid id, IReadOnlyList<ScoredRelease> candidates, DateTimeOffset now) {
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
                Language = release.Language,
                Score = scored.Score,
                Accepted = scored.Accepted,
                RejectionsJson = JsonSerializer.Serialize(scored.Rejections.Select(reason => reason.ToCode()).ToArray()),
                CreatedAt = now
            });
        }
    }

    public async Task<AcquisitionQueueCandidate?> GetQueueCandidateAsync(Guid acquisitionId, Guid candidateId, CancellationToken cancellationToken) {
        var row = await db.ReleaseCandidates
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == candidateId && candidate.AcquisitionId == acquisitionId, cancellationToken);
        return row is null
            ? null
            : new AcquisitionQueueCandidate(row.Id, row.Title, row.IndexerName, row.DownloadUrl, row.MagnetUrl, row.InfoHash, row.InfoUrl,
                row.Protocol, row.IndexerConfigId, row.SizeBytes, row.Seeders, row.Peers, row.Language, row.PublishedAt);
    }

    public async Task<IReadOnlyList<AcquisitionCandidateRef>> ListAcceptedCandidatesAsync(Guid acquisitionId, CancellationToken cancellationToken) {
        var rows = await db.ReleaseCandidates
            .AsNoTracking()
            .Where(candidate => candidate.AcquisitionId == acquisitionId && candidate.Accepted)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Title)
            .ThenBy(candidate => candidate.IndexerName)
            .ThenBy(candidate => candidate.Id)
            .Select(candidate => new { candidate.Id, candidate.Title, candidate.IndexerName, candidate.InfoHash, candidate.Protocol, candidate.Score, candidate.Seeders, candidate.Peers })
            .ToArrayAsync(cancellationToken);
        return rows.Select(row => new AcquisitionCandidateRef(
            row.Id, row.Title, row.IndexerName, row.InfoHash, row.Protocol, row.Score, row.Seeders, row.Peers)).ToArray();
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
            if (!ReleaseIdentity.Matches(identity, row.InfoHash, row.IndexerName, row.Title)) {
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
        if (row is null || row.Status == AcquisitionStatus.Stopping) {
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
}
