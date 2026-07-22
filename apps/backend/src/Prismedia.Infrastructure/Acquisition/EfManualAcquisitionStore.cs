using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// EF persistence for transiently reviewed replacements and browser uploads. It materializes ordinary
/// acquisition state only when a user selects a release or starts sending content.
/// </summary>
public sealed class EfManualAcquisitionStore(
    PrismediaDbContext db,
    IAcquisitionStore acquisitions,
    IImportTargetIndex? targets = null) : IManualReplacementStore, IAcquisitionUploadStore {
    /// <inheritdoc />
    public async Task<ManualReplacementSearchTarget?> GetSearchTargetAsync(
        Guid entityId,
        CancellationToken cancellationToken) {
        var imported = await db.Acquisitions.AsNoTracking()
            .Where(row => row.EntityId == entityId
                && row.Status == AcquisitionStatus.Imported
                && row.FinalSourcePath != null)
            .OrderByDescending(row => row.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (imported is not null) {
            var existingInput = await acquisitions.GetSearchInputAsync(imported.Id, cancellationToken);
            return existingInput is null || !PathExists(imported.FinalSourcePath)
                ? null
                : new ManualReplacementSearchTarget(entityId, existingInput, OwnedQuality(imported));
        }

        var entity = await db.Entities.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == entityId, cancellationToken);
        if (entity is null || !TryDecodeReplaceableKind(entity.KindCode, out var kind)) {
            return null;
        }

        var sourcePath = kind == EntityKind.AudioLibrary
            ? targets is null
                ? null
                : (await targets.GetAlbumTargetAsync(entityId, cancellationToken))?.AlbumFolderPath
            : await db.EntityFiles.AsNoTracking()
                .Where(file => file.EntityId == entityId && file.Role == EntityFileRole.Source)
                .OrderBy(file => file.CreatedAt)
                .Select(file => file.Path)
                .FirstOrDefaultAsync(cancellationToken);
        if (!PathExists(sourcePath)) {
            return null;
        }

        var (author, series, seasonNumber, episodeNumber) = await ResolveManualContextAsync(
            entity,
            kind,
            cancellationToken);
        var input = new AcquisitionSearchInput(
            Guid.Empty,
            entity.Title,
            author,
            kind,
            entityId,
            Series: series,
            SeasonNumber: seasonNumber,
            EpisodeNumber: episodeNumber,
            BookRendition: kind == EntityKind.Book ? BookRendition.Ebook : null);
        var owned = MediaQualityLadder.IsUpgradeCapableKind(kind) || MediaQualityLadder.IsAudioKind(kind)
            ? new UpgradeOwnedQuality(null, MediaQualityLadder.Detect(kind, Path.GetFileName(sourcePath!)).Code)
            : new UpgradeOwnedQuality(
                new BookQualityRank(BookSourceTier.Unknown, BookFormatDetection.FormatTierFromExtension(sourcePath!)),
                null);
        return new ManualReplacementSearchTarget(entityId, input, owned);
    }

    /// <inheritdoc />
    public async Task<Guid?> CreateReviewedReplacementAsync(
        Guid entityId,
        IReadOnlyList<ReviewedReleaseCandidate> candidates,
        CancellationToken cancellationToken) {
        var target = await GetSearchTargetAsync(entityId, cancellationToken);
        if (target is null) {
            return null;
        }

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var parent = await db.Acquisitions
            .Where(row => row.EntityId == entityId
                && row.Status == AcquisitionStatus.Imported
                && row.FinalSourcePath != null)
            .OrderByDescending(row => row.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (parent is null) {
            var sourcePath = target.Input.Kind == EntityKind.AudioLibrary
                ? targets is null
                    ? null
                    : (await targets.GetAlbumTargetAsync(entityId, cancellationToken))?.AlbumFolderPath
                : await db.EntityFiles.AsNoTracking()
                    .Where(file => file.EntityId == entityId && file.Role == EntityFileRole.Source)
                    .OrderBy(file => file.CreatedAt)
                    .Select(file => file.Path)
                    .FirstOrDefaultAsync(cancellationToken);
            if (!PathExists(sourcePath)) {
                return null;
            }

            var now = DateTimeOffset.UtcNow;
            parent = new AcquisitionRow {
                Id = Guid.NewGuid(),
                Kind = target.Input.Kind,
                BookRendition = target.Input.BookRendition,
                EntityId = entityId,
                Status = AcquisitionStatus.Imported,
                Title = target.Input.Title,
                Author = target.Input.Author,
                Series = target.Input.Series,
                SeasonNumber = target.Input.SeasonNumber,
                EpisodeNumber = target.Input.EpisodeNumber,
                Year = target.Input.Year,
                FinalSourcePath = sourcePath,
                OwnedSourceTier = target.OwnedQuality.BookRank?.Source ?? BookSourceTier.Unknown,
                OwnedFormatTier = target.OwnedQuality.BookRank?.Format ?? BookFormatTier.Unknown,
                OwnedMediaQuality = target.OwnedQuality.MediaQualityCode,
                OwnedMediaRevision = target.OwnedQuality.MediaRevision,
                OwnedFormatScore = target.OwnedQuality.FormatScore,
                UpgradeQualityCaptured = true,
                SelectedReleaseJson = JsonSerializer.Serialize(new SelectedRelease(
                    Path.GetFileName(sourcePath!),
                    "Manual library import",
                    null,
                    ManualPick: true)),
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Acquisitions.Add(parent);
        }

        var reviewedCandidateIds = candidates.Select(candidate => candidate.Id).ToArray();
        if (reviewedCandidateIds.Length > 0) {
            var replayChildId = await db.Acquisitions.AsNoTracking()
                .Where(row => row.UpgradeOfAcquisitionId == parent.Id
                    && db.ReleaseCandidates.Any(candidate =>
                        candidate.AcquisitionId == row.Id
                        && reviewedCandidateIds.Contains(candidate.Id)))
                .OrderByDescending(row => row.CreatedAt)
                .Select(row => (Guid?)row.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (replayChildId is not null) {
                if (transaction is not null) {
                    await transaction.CommitAsync(cancellationToken);
                }
                return replayChildId;
            }
        }

        if (await db.Acquisitions.AsNoTracking().AnyAsync(
                row => row.UpgradeOfAcquisitionId == parent.Id
                    && row.Status != AcquisitionStatus.Cancelled,
                cancellationToken)) {
            return null;
        }

        var monitor = await db.Monitors.FirstOrDefaultAsync(
            row => row.EntityId == entityId && row.Status == MonitorStatus.Active,
            cancellationToken);
        if (monitor?.UpgradeChildAcquisitionId is not null) {
            return null;
        }

        var createdAt = DateTimeOffset.UtcNow;
        var child = CreateUpgradeChild(parent, createdAt);
        child.Status = AcquisitionStatus.AwaitingSelection;
        child.StatusMessage = $"{candidates.Count(candidate => candidate.Release.Accepted)} acceptable of {candidates.Count} release(s). Review required.";
        db.Acquisitions.Add(child);
        AddReviewedCandidates(child.Id, candidates, createdAt);
        if (monitor is not null) {
            monitor.AcquisitionId ??= parent.Id;
            monitor.UpgradeChildAcquisitionId = child.Id;
            monitor.UpdatedAt = createdAt;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) {
            await transaction.CommitAsync(cancellationToken);
        }
        return child.Id;
    }

    /// <inheritdoc />
    public async Task<Guid?> PrepareAsync(Guid entityId, CancellationToken cancellationToken) {
        var active = await db.Acquisitions.AsNoTracking()
            .Where(row => row.EntityId == entityId
                && row.Status != AcquisitionStatus.Imported
                && row.Status != AcquisitionStatus.Stopping)
            .OrderByDescending(row => row.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (active is not null) {
            return active.Status is AcquisitionStatus.Pending
                or AcquisitionStatus.Searching
                or AcquisitionStatus.AwaitingSelection
                or AcquisitionStatus.Failed
                or AcquisitionStatus.ManualImportRequired
                or AcquisitionStatus.Cancelled
                ? active.Id
                : null;
        }

        return await CreateReviewedReplacementAsync(entityId, [], cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> CompleteAsync(
        Guid acquisitionId,
        CompletedAcquisitionUpload upload,
        CancellationToken cancellationToken) {
        var acquisition = await db.Acquisitions.FirstOrDefaultAsync(
            row => row.Id == acquisitionId,
            cancellationToken);
        if (acquisition is null || acquisition.Status is AcquisitionStatus.Imported
            or AcquisitionStatus.Importing
            or AcquisitionStatus.Downloaded
            or AcquisitionStatus.Queued
            or AcquisitionStatus.Downloading
            or AcquisitionStatus.Stopping) {
            return false;
        }

        var existing = await db.DownloadTransfers
            .Where(row => row.AcquisitionId == acquisitionId)
            .ToArrayAsync(cancellationToken);
        db.DownloadTransfers.RemoveRange(existing);
        var now = DateTimeOffset.UtcNow;
        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(),
            AcquisitionId = acquisitionId,
            DownloadClientConfigId = null,
            ClientItemId = upload.ClientItemId,
            ContentPath = upload.ContentPath,
            Progress = 1,
            CreatedAt = now,
            UpdatedAt = now
        });
        acquisition.SelectedReleaseJson = JsonSerializer.Serialize(new SelectedRelease(
            upload.DisplayName,
            "Manual upload",
            null,
            ManualPick: true));
        acquisition.Status = AcquisitionStatus.Downloaded;
        acquisition.StatusMessage = "Upload complete; importing.";
        acquisition.ImportClaimJobId = null;
        acquisition.UpdatedAt = now;
        try {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        } catch (DbUpdateConcurrencyException) {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private static AcquisitionRow CreateUpgradeChild(AcquisitionRow parent, DateTimeOffset now) => new() {
        Id = Guid.NewGuid(),
        Kind = parent.Kind,
        BookRendition = parent.BookRendition,
        EntityId = parent.EntityId,
        ProfileId = parent.ProfileId,
        TargetLibraryRootId = parent.TargetLibraryRootId,
        Status = AcquisitionStatus.Pending,
        Title = parent.Title,
        Author = parent.Author,
        Series = parent.Series,
        SeasonNumber = parent.SeasonNumber,
        EpisodeNumber = parent.EpisodeNumber,
        VolumeNumber = parent.VolumeNumber,
        Year = parent.Year,
        PosterUrl = parent.PosterUrl,
        Description = parent.Description,
        IdentityNamespace = parent.IdentityNamespace,
        IdentityValue = parent.IdentityValue,
        ExternalIdsJson = parent.ExternalIdsJson,
        SourceUrlsJson = parent.SourceUrlsJson,
        UpgradeOfAcquisitionId = parent.Id,
        CreatedAt = now,
        UpdatedAt = now
    };

    private void AddReviewedCandidates(
        Guid acquisitionId,
        IReadOnlyList<ReviewedReleaseCandidate> candidates,
        DateTimeOffset now) {
        foreach (var reviewed in candidates) {
            var scored = reviewed.Release;
            var release = scored.Release;
            db.ReleaseCandidates.Add(new ReleaseCandidateRow {
                Id = reviewed.Id,
                AcquisitionId = acquisitionId,
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
    }

    private async Task<(string? Author, string? Series, int? SeasonNumber, int? EpisodeNumber)> ResolveManualContextAsync(
        EntityRow entity,
        EntityKind kind,
        CancellationToken cancellationToken) {
        if (kind == EntityKind.AudioLibrary) {
            var artist = entity.ParentEntityId is { } artistId
                ? await db.Entities.AsNoTracking().FirstOrDefaultAsync(row => row.Id == artistId, cancellationToken)
                : null;
            return (artist?.Title, null, null, null);
        }

        if (kind != EntityKind.Video || entity.ParentEntityId is not { } seasonId) {
            return (null, null, null, null);
        }

        var season = await db.Entities.AsNoTracking().FirstOrDefaultAsync(row => row.Id == seasonId, cancellationToken);
        var series = season?.ParentEntityId is { } seriesId
            ? await db.Entities.AsNoTracking().FirstOrDefaultAsync(row => row.Id == seriesId, cancellationToken)
            : null;
        return (null, series?.Title, season?.SortOrder, entity.SortOrder);
    }

    private static UpgradeOwnedQuality OwnedQuality(AcquisitionRow parent) =>
        MediaQualityLadder.IsUpgradeCapableKind(parent.Kind) || MediaQualityLadder.IsAudioKind(parent.Kind)
            ? new UpgradeOwnedQuality(null, parent.OwnedMediaQuality, parent.OwnedMediaRevision, parent.OwnedFormatScore)
            : new UpgradeOwnedQuality(
                new BookQualityRank(parent.OwnedSourceTier, parent.OwnedFormatTier),
                null,
                FormatScore: parent.OwnedFormatScore);

    private static bool TryDecodeReplaceableKind(string code, out EntityKind kind) {
        try {
            kind = code.DecodeAs<EntityKind>();
            return kind == EntityKind.Book
                || kind == EntityKind.AudioLibrary
                || MediaQualityLadder.IsUpgradeCapableKind(kind);
        } catch (ArgumentException) {
            kind = default;
            return false;
        }
    }

    private static bool PathExists(string? path) =>
        !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path));
}
