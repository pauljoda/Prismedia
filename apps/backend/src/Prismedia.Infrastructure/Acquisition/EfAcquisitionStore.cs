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
    /// <inheritdoc />
    public async Task<IReadOnlyList<DownloadedAcquisitionCompletion>> ListDownloadedCompletionWorkAsync(
        CancellationToken cancellationToken) {
        var rows = await db.Acquisitions.AsNoTracking()
            .Where(row => row.Status == AcquisitionStatus.Downloaded)
            .OrderBy(row => row.UpdatedAt)
            .ThenBy(row => row.Id)
            .Select(row => new {
                row.Id,
                row.Kind,
                IsUpgrade = row.UpgradeOfAcquisitionId != null,
            })
            .ToArrayAsync(cancellationToken);

        return rows
            .Select(row => new DownloadedAcquisitionCompletion(row.Id, row.Kind, row.IsUpgrade))
            .ToArray();
    }

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
            VolumeNumber = metadata.VolumeNumber,
            Year = metadata.Year,
            PosterUrl = metadata.PosterUrl,
            Description = metadata.Description,
            IdentityNamespace = metadata.ExternalIdentity?.Namespace,
            IdentityValue = metadata.ExternalIdentity?.Value,
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
            .Select(row => new { row.Id, row.Title, row.Author, row.Kind, row.EntityId, row.Year, row.ProfileId, row.Series, row.SeasonNumber, row.EpisodeNumber, row.VolumeNumber })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null) {
            return null;
        }

        // The row's Year is request-time provider metadata and is unreliable for TV children (episode
        // re-air years, or nothing at all for season packs). For entity-linked video acquisitions the
        // library itself knows the work's year identity — the containing series' premiere year or the
        // movie's release year — which is what scene naming appends to disambiguate same-name works,
        // so the search gates compare against that instead.
        var year = row.EntityId is { } entityId && IsVideoKind(row.Kind)
            ? await ResolveWorkYearAsync(entityId, cancellationToken) ?? row.Year
            : row.Year;

        return new AcquisitionSearchInput(
            row.Id, row.Title, row.Author, row.Kind, row.EntityId, year, row.ProfileId,
            row.Series, row.SeasonNumber, row.EpisodeNumber, row.VolumeNumber);
    }

    private static bool IsVideoKind(EntityKind kind) =>
        kind is EntityKind.Movie or EntityKind.Video or EntityKind.VideoSeason or EntityKind.VideoSeries;

    /// <summary>
    /// The year identity of the work an entity belongs to: the topmost video container's (series or
    /// movie, within its cycle-safe ancestor walk) first premiere/release date year. Null when the graph or
    /// dates are missing, so callers keep their request-time fallback.
    /// </summary>
    private async Task<int?> ResolveWorkYearAsync(Guid entityId, CancellationToken cancellationToken) {
        var seriesCode = EntityKindRegistry.VideoSeries.Code;
        var movieCode = EntityKindRegistry.Movie.Code;
        var currentId = (Guid?)entityId;
        var workId = entityId;
        var visited = new HashSet<Guid>();
        while (currentId is { } id && visited.Add(id)) {
            var current = await db.Entities.AsNoTracking()
                .Where(row => row.Id == id)
                .Select(row => new { row.KindCode, row.ParentEntityId })
                .FirstOrDefaultAsync(cancellationToken);
            if (current is null) {
                break;
            }

            if (current.KindCode == seriesCode || current.KindCode == movieCode) {
                workId = id;
                break;
            }

            currentId = current.ParentEntityId;
        }

        // Provider date vocabulary ladder (same order identify uses): the work's première/release date.
        var dates = await db.EntityDates.AsNoTracking()
            .Where(date => date.EntityId == workId && date.SortableValue != null)
            .Select(date => new { date.Code, date.SortableValue })
            .ToArrayAsync(cancellationToken);
        foreach (var code in (string[])["firstAir", "release", "airDate", "date"]) {
            var match = dates.FirstOrDefault(date => date.Code == code);
            if (match?.SortableValue is { } sortable) {
                return sortable.Year;
            }
        }

        return null;
    }

    public async Task<AcquisitionStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken) {
        var row = await db.Acquisitions
            .AsNoTracking()
            .Where(row => row.Id == id)
            .Select(row => (AcquisitionStatus?)row.Status)
            .FirstOrDefaultAsync(cancellationToken);
        return row;
    }

    /// <inheritdoc />
    public async Task<AcquisitionTeardownClaim?> GetTeardownClaimAsync(
        Guid id,
        CancellationToken cancellationToken) {
        var claim = await db.Acquisitions.AsNoTracking()
            .Where(row => row.Id == id
                && row.Status == AcquisitionStatus.Stopping
                && row.TeardownIntent != null
                && row.TeardownOriginalStatus != null)
            .Select(row => new {
                Intent = row.TeardownIntent!.Value,
                OriginalStatus = row.TeardownOriginalStatus!.Value
            })
            .FirstOrDefaultAsync(cancellationToken);
        return claim is null
            ? null
            : new AcquisitionTeardownClaim(claim.Intent, claim.OriginalStatus);
    }

    /// <inheritdoc />
    public async Task<bool> TryClaimTeardownAsync(
        Guid id,
        AcquisitionStatus expectedStatus,
        AcquisitionTeardownIntent intent,
        string message,
        CancellationToken cancellationToken) {
        var row = await db.Acquisitions.FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (row is null
            || row.Status != expectedStatus
            || row.TeardownIntent is not null
            || row.TeardownOriginalStatus is not null) {
            return false;
        }

        row.TeardownOriginalStatus = row.Status;
        row.TeardownIntent = intent;
        row.Status = AcquisitionStatus.Stopping;
        row.StatusMessage = message;
        row.ImportClaimJobId = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        try {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        } catch (DbUpdateConcurrencyException) {
            db.Entry(row).State = EntityState.Detached;
            return false;
        }
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

    public async Task<Guid?> CloneForRetryAsync(Guid id, CancellationToken cancellationToken) {
        var source = await db.Acquisitions.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (source?.EntityId is not { } entityId) {
            return null;
        }

        // Only a still-wanted, fileless placeholder has anything left to chase — an imported or
        // user-deleted entity means the loop is over regardless of who removes the download record.
        if (!await IsWantedFilelessAsync(entityId, cancellationToken)) {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var clone = CreateRetryClone(source, now);
        db.Acquisitions.Add(clone);
        await db.SaveChangesAsync(cancellationToken);
        return clone.Id;
    }

    /// <inheritdoc />
    public async Task<Guid?> GetOrCreateTeardownReplacementAsync(
        Guid id,
        CancellationToken cancellationToken) {
        var source = await db.Acquisitions.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (source?.EntityId is not { } entityId
            || source.Status != AcquisitionStatus.Stopping
            || source.TeardownIntent != AcquisitionTeardownIntent.Reacquire) {
            return null;
        }

        if (source.TeardownReplacementAcquisitionId is { } existingId
            && await db.Acquisitions.AsNoTracking().AnyAsync(row => row.Id == existingId, cancellationToken)) {
            return existingId;
        }
        if (!await IsWantedFilelessAsync(entityId, cancellationToken)) {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var clone = CreateRetryClone(source, now);
        source.TeardownReplacementAcquisitionId = clone.Id;
        source.UpdatedAt = now;
        db.Acquisitions.Add(clone);
        try {
            await db.SaveChangesAsync(cancellationToken);
            return clone.Id;
        } catch (DbUpdateConcurrencyException) {
            db.ChangeTracker.Clear();
            return await db.Acquisitions.AsNoTracking()
                .Where(row => row.Id == id)
                .Select(row => row.TeardownReplacementAcquisitionId)
                .FirstOrDefaultAsync(cancellationToken);
        }
    }

    private async Task<bool> IsWantedFilelessAsync(Guid entityId, CancellationToken cancellationToken) =>
        await db.Entities.AsNoTracking().AnyAsync(
            entity => entity.Id == entityId && entity.IsWanted,
            cancellationToken)
        && !await db.EntityFiles.AsNoTracking().AnyAsync(
            file => file.EntityId == entityId && file.Role == EntityFileRole.Source,
            cancellationToken);

    private static AcquisitionRow CreateRetryClone(AcquisitionRow source, DateTimeOffset now) => new() {
        Id = Guid.NewGuid(),
        Kind = source.Kind,
        EntityId = source.EntityId,
        ProfileId = source.ProfileId,
        TargetLibraryRootId = source.TargetLibraryRootId,
        Status = AcquisitionStatus.Pending,
        Title = source.Title,
        Author = source.Author,
        Series = source.Series,
        SeasonNumber = source.SeasonNumber,
        EpisodeNumber = source.EpisodeNumber,
        VolumeNumber = source.VolumeNumber,
        Year = source.Year,
        PosterUrl = source.PosterUrl,
        Description = source.Description,
        IdentityNamespace = source.IdentityNamespace,
        IdentityValue = source.IdentityValue,
        ExternalIdsJson = source.ExternalIdsJson,
        SourceUrlsJson = source.SourceUrlsJson,
        CreatedAt = now,
        UpdatedAt = now
    };

    public async Task SetStatusAsync(Guid id, AcquisitionStatus status, string? message, CancellationToken cancellationToken) {
        var row = await db.Acquisitions.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (row is null || row.Status == AcquisitionStatus.Stopping) {
            return;
        }

        row.Status = status;
        row.StatusMessage = message;
        if (status is not (AcquisitionStatus.Importing or AcquisitionStatus.Failed)) {
            row.ImportClaimJobId = null;
        }
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryTransitionStatusAsync(
        Guid id,
        IReadOnlyCollection<AcquisitionStatus> expectedStatuses,
        AcquisitionStatus status,
        string? message,
        CancellationToken cancellationToken) {
        if (expectedStatuses.Count == 0) {
            return false;
        }

        var expected = expectedStatuses.Distinct().ToArray();
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational()) {
            return await db.Acquisitions
                .Where(row => row.Id == id && expected.Contains(row.Status))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.Status, status)
                    .SetProperty(row => row.StatusMessage, message)
                    .SetProperty(
                        row => row.ImportClaimJobId,
                        status is AcquisitionStatus.Importing or AcquisitionStatus.Failed
                            ? row => row.ImportClaimJobId
                            : row => null)
                    .SetProperty(row => row.UpdatedAt, now), cancellationToken) == 1;
        }

        var row = await db.Acquisitions.FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (row is null || !expected.Contains(row.Status)) {
            return false;
        }

        row.Status = status;
        row.StatusMessage = message;
        if (status is not (AcquisitionStatus.Importing or AcquisitionStatus.Failed)) {
            row.ImportClaimJobId = null;
        }
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryClaimFailedRecoveryAsync(
        Guid id,
        IReadOnlyCollection<AcquisitionStatus> expectedStatuses,
        SelectedRelease? expectedSelectedRelease,
        string message,
        CancellationToken cancellationToken) {
        if (expectedStatuses.Count == 0) {
            return false;
        }

        var expected = expectedStatuses.Distinct().ToArray();
        var selectedJson = expectedSelectedRelease is null
            ? null
            : JsonSerializer.Serialize(expectedSelectedRelease);
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational()) {
            return await db.Acquisitions
                .Where(row => row.Id == id
                    && expected.Contains(row.Status)
                    && row.SelectedReleaseJson == selectedJson)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.Status, AcquisitionStatus.Failed)
                    .SetProperty(row => row.StatusMessage, message)
                    .SetProperty(row => row.UpdatedAt, now), cancellationToken) == 1;
        }

        var row = await db.Acquisitions.FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (row is null
            || !expected.Contains(row.Status)
            || !string.Equals(row.SelectedReleaseJson, selectedJson, StringComparison.Ordinal)) {
            return false;
        }

        row.Status = AcquisitionStatus.Failed;
        row.StatusMessage = message;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryClaimInitialImportAsync(
        Guid id,
        Guid claimJobId,
        bool allowManualRetry,
        CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational()) {
            return await db.Acquisitions
                .Where(row => row.Id == id
                    && (row.Status == AcquisitionStatus.Downloaded
                        || (allowManualRetry && row.Status == AcquisitionStatus.ManualImportRequired)
                        || ((row.Status == AcquisitionStatus.Importing || row.Status == AcquisitionStatus.Failed)
                            && row.ImportCheckpointJson == null
                            && row.ImportClaimJobId == claimJobId)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.Status, AcquisitionStatus.Importing)
                    .SetProperty(row => row.StatusMessage, (string?)null)
                    .SetProperty(row => row.ImportClaimJobId, claimJobId)
                    .SetProperty(row => row.UpdatedAt, now), cancellationToken) == 1;
        }

        var row = await db.Acquisitions.FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (row is null
            || (row.Status != AcquisitionStatus.Downloaded
                && !(allowManualRetry && row.Status == AcquisitionStatus.ManualImportRequired)
                && !((row.Status == AcquisitionStatus.Importing || row.Status == AcquisitionStatus.Failed)
                    && row.ImportCheckpointJson is null
                    && row.ImportClaimJobId == claimJobId))) {
            return false;
        }

        row.Status = AcquisitionStatus.Importing;
        row.StatusMessage = null;
        row.ImportClaimJobId = claimJobId;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryHoldCorruptImportCheckpointAsync(
        Guid id,
        Guid claimJobId,
        string message,
        CancellationToken cancellationToken) {
        var holdable = new[] {
            AcquisitionStatus.Downloaded,
            AcquisitionStatus.Failed,
            AcquisitionStatus.ManualImportRequired,
        };
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational()) {
            return await db.Acquisitions
                .Where(row => row.Id == id
                    && row.ImportCheckpointJson != null
                    && (holdable.Contains(row.Status)
                        || (row.Status == AcquisitionStatus.Importing
                            && row.ImportClaimJobId == claimJobId)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.Status, AcquisitionStatus.ManualImportRequired)
                    .SetProperty(row => row.StatusMessage, message)
                    .SetProperty(row => row.ImportClaimJobId, (Guid?)null)
                    .SetProperty(row => row.UpdatedAt, now), cancellationToken) == 1;
        }

        var row = await db.Acquisitions.FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (row?.ImportCheckpointJson is null
            || (!holdable.Contains(row.Status)
                && !(row.Status == AcquisitionStatus.Importing
                    && row.ImportClaimJobId == claimJobId))) {
            return false;
        }

        row.Status = AcquisitionStatus.ManualImportRequired;
        row.StatusMessage = message;
        row.ImportClaimJobId = null;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
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
        var child = await db.Acquisitions.AsNoTracking().FirstOrDefaultAsync(
            row => row.Id == childId
                && (row.Status == AcquisitionStatus.Downloaded
                    || row.Status == AcquisitionStatus.Importing),
            cancellationToken);
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
            parent.EntityId,
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
        if (row is null || row.Status == AcquisitionStatus.Stopping) {
            return;
        }

        row.OwnedSourceTier = ownedQuality.Source;
        row.OwnedFormatTier = ownedQuality.Format;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateOwnedMediaQualityAsync(Guid acquisitionId, string ownedMediaQuality, int ownedMediaRevision, int ownedFormatScore, CancellationToken cancellationToken) {
        var row = await db.Acquisitions.FirstOrDefaultAsync(row => row.Id == acquisitionId, cancellationToken);
        if (row is null || row.Status == AcquisitionStatus.Stopping) {
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
        if (row is null || row.Status == AcquisitionStatus.Stopping) {
            return;
        }

        // Prepare every potentially fallible value before the terminal commit. Imported is the final
        // source of truth; malformed optional history metadata must not make the handler overwrite it
        // with Failed after the checkpoint has already been cleared.
        string? selectedTitle = null;
        if (row.SelectedReleaseJson is { Length: > 0 } selectedJson) {
            try {
                selectedTitle = JsonSerializer.Deserialize<SelectedRelease>(selectedJson)?.Title;
            } catch (JsonException ex) {
                logger.LogWarning(ex, "Ignoring malformed selected-release history metadata for acquisition {Id}.", id);
            }
        }
        var qualityCode = ownedMediaQuality ?? $"{ownedQuality.Source.ToCode()}/{ownedQuality.Format.ToCode()}";
        var importedHistory = new AcquisitionHistoryEntry(
            row.Id,
            row.EntityId,
            row.Kind,
            AcquisitionHistoryEvent.Imported,
            row.Title,
            selectedTitle,
            QualityCode: qualityCode,
            FormatScore: ownedFormatScore,
            Message: message);

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
        row.ImportCheckpointJson = null;
        row.ImportClaimJobId = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        // Durable Imported event: the single choke point for all four import engines. History is
        // deliberately best-effort and non-cancellable after the terminal state is committed.
        await history.SafeAddAsync(logger, importedHistory, CancellationToken.None);
    }

    public async Task<bool> TryCompleteSearchAsync(
        Guid id,
        IReadOnlyList<ScoredRelease> candidates,
        string? message,
        CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational()) {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var transitioned = await db.Acquisitions
                .Where(row => row.Id == id && row.Status == AcquisitionStatus.Searching)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.Status, AcquisitionStatus.AwaitingSelection)
                    .SetProperty(row => row.StatusMessage, message)
                    .SetProperty(row => row.ImportClaimJobId, (Guid?)null)
                    .SetProperty(row => row.UpdatedAt, now), cancellationToken);
            if (transitioned != 1) {
                await transaction.RollbackAsync(CancellationToken.None);
                return false;
            }

            await db.ReleaseCandidates
                .Where(candidate => candidate.AcquisitionId == id)
                .ExecuteDeleteAsync(cancellationToken);
            AddCandidates(id, candidates, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        var acquisition = await db.Acquisitions.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (acquisition is null || acquisition.Status != AcquisitionStatus.Searching) {
            return false;
        }

        var existing = await db.ReleaseCandidates
            .Where(candidate => candidate.AcquisitionId == id)
            .ToArrayAsync(cancellationToken);
        db.ReleaseCandidates.RemoveRange(existing);
        AddCandidates(id, candidates, now);
        acquisition.Status = AcquisitionStatus.AwaitingSelection;
        acquisition.StatusMessage = message;
        acquisition.ImportClaimJobId = null;
        acquisition.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

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

    /// <inheritdoc />
    public async Task<bool> BeginTransferAddAsync(
        Guid acquisitionId,
        Guid downloadClientConfigId,
        string correlation,
        string? category,
        TransferSeedGoal? seedGoal,
        CancellationToken cancellationToken) {
        var acquisition = await db.Acquisitions.FirstOrDefaultAsync(
            row => row.Id == acquisitionId,
            cancellationToken);
        if (acquisition is null || acquisition.Status != AcquisitionStatus.Queued) {
            return false;
        }

        var existing = await db.DownloadTransfers
            .Where(transfer => transfer.AcquisitionId == acquisitionId)
            .ToArrayAsync(cancellationToken);
        var addingCode = TransferOwnershipState.Adding.ToCode();
        if (existing.Length == 1
            && existing[0].DownloadClientConfigId == downloadClientConfigId
            && existing[0].State == addingCode
            && string.Equals(existing[0].ClientItemId, correlation, StringComparison.Ordinal)
            && string.Equals(existing[0].Category, category, StringComparison.Ordinal)) {
            return true;
        }

        if (existing.Length > 0) {
            db.DownloadTransfers.RemoveRange(existing);
        }
        var now = DateTimeOffset.UtcNow;
        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(),
            AcquisitionId = acquisitionId,
            DownloadClientConfigId = downloadClientConfigId,
            ClientItemId = correlation,
            Category = category,
            Progress = 0,
            State = addingCode,
            SeedGoalRatio = seedGoal?.Ratio,
            SeedGoalTimeMinutes = seedGoal?.TimeMinutes,
            CreatedAt = now,
            UpdatedAt = now
        });
        acquisition.UpdatedAt = now;
        try {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        } catch (DbUpdateConcurrencyException) {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> CompleteTransferAddAsync(
        Guid acquisitionId,
        Guid downloadClientConfigId,
        string correlation,
        string clientItemId,
        SelectedRelease selectedRelease,
        string queuedMessage,
        CancellationToken cancellationToken) {
        var addingCode = TransferOwnershipState.Adding.ToCode();
        var acquisition = await db.Acquisitions.FirstOrDefaultAsync(
            row => row.Id == acquisitionId,
            cancellationToken);
        var transfer = await db.DownloadTransfers.FirstOrDefaultAsync(
            row => row.AcquisitionId == acquisitionId
                && row.DownloadClientConfigId == downloadClientConfigId
                && row.ClientItemId == correlation
                && row.State == addingCode,
            cancellationToken);
        if (acquisition is null || acquisition.Status != AcquisitionStatus.Queued || transfer is null) {
            return false;
        }

        transfer.ClientItemId = clientItemId;
        transfer.State = null;
        transfer.UpdatedAt = DateTimeOffset.UtcNow;
        acquisition.SelectedReleaseJson = JsonSerializer.Serialize(selectedRelease);
        acquisition.Status = AcquisitionStatus.Queued;
        acquisition.StatusMessage = queuedMessage;
        acquisition.ImportClaimJobId = null;
        acquisition.UpdatedAt = transfer.UpdatedAt;
        try {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        } catch (DbUpdateConcurrencyException) {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    /// <inheritdoc />
    public async Task AbandonTransferAddAsync(
        Guid acquisitionId,
        Guid downloadClientConfigId,
        string correlation,
        CancellationToken cancellationToken) {
        var addingCode = TransferOwnershipState.Adding.ToCode();
        if (db.Database.IsRelational()) {
            await db.DownloadTransfers
                .Where(row => row.AcquisitionId == acquisitionId
                    && row.DownloadClientConfigId == downloadClientConfigId
                    && row.ClientItemId == correlation
                    && row.State == addingCode)
                .ExecuteDeleteAsync(cancellationToken);
            return;
        }

        var rows = await db.DownloadTransfers
            .Where(row => row.AcquisitionId == acquisitionId
                && row.DownloadClientConfigId == downloadClientConfigId
                && row.ClientItemId == correlation
                && row.State == addingCode)
            .ToArrayAsync(cancellationToken);
        db.DownloadTransfers.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> CreateTransferAsync(Guid acquisitionId, Guid? downloadClientConfigId, string clientItemId, string? category, CancellationToken cancellationToken, TransferSeedGoal? seedGoal = null) {
        var acquisition = await db.Acquisitions.FirstOrDefaultAsync(row => row.Id == acquisitionId, cancellationToken);
        if (acquisition is null) {
            return false;
        }

        if (acquisition.Status == AcquisitionStatus.Stopping) {
            // Queue holds the transfer-add row lease before contacting the remote client. Reaching this
            // branch means the caller lacks that lease; refuse persistence so it compensates the remote Add
            // instead of replacing a teardown-owned pointer.
            return false;
        }

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
        // The acquisition status is a concurrency token. Touching its row makes transfer persistence and
        // teardown mutually exclusive: a remote add that loses this race returns false for immediate cleanup.
        acquisition.UpdatedAt = now;
        try {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        } catch (DbUpdateConcurrencyException) {
            db.ChangeTracker.Clear();
            return false;
        }
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
        var active = new[] {
            AcquisitionStatus.Queued,
            AcquisitionStatus.Downloading,
        };
        var adding = TransferOwnershipState.Adding.ToCode();
        var rows = await (
            from transfer in db.DownloadTransfers.AsNoTracking()
            join acquisition in db.Acquisitions.AsNoTracking() on transfer.AcquisitionId equals acquisition.Id
            where active.Contains(acquisition.Status)
                && (transfer.State == null || transfer.State != adding)
            select new { transfer.Id, transfer.AcquisitionId, transfer.DownloadClientConfigId, transfer.ClientItemId, acquisition.Status, transfer.Progress, transfer.UpdatedAt, transfer.StalledSince })
            .ToArrayAsync(cancellationToken);
        return rows
            .Select(row => new ActiveTransfer(row.Id, row.AcquisitionId, row.DownloadClientConfigId, row.ClientItemId, row.Status, row.Progress, row.UpdatedAt, row.StalledSince))
            .ToArray();
    }

    public async Task<bool> HasActiveTransfersAsync(CancellationToken cancellationToken) {
        var active = new[] {
            AcquisitionStatus.Queued,
            AcquisitionStatus.Downloading,
        };
        var adding = TransferOwnershipState.Adding.ToCode();
        // Seeding watches keep the monitor scheduled after import, so seed goals are actually enforced.
        return await (
            from transfer in db.DownloadTransfers.AsNoTracking()
            join acquisition in db.Acquisitions.AsNoTracking() on transfer.AcquisitionId equals acquisition.Id
            where (transfer.State == null || transfer.State != adding)
                && (active.Contains(acquisition.Status) || transfer.SeedingSince != null)
            select transfer.Id).AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListStaleSearchingAsync(TimeSpan olderThan, CancellationToken cancellationToken) {
        var cutoff = DateTimeOffset.UtcNow - olderThan;
        return await db.Acquisitions.AsNoTracking()
            .Where(row => row.Status == AcquisitionStatus.Searching && row.UpdatedAt < cutoff)
            .Select(row => row.Id)
            .ToArrayAsync(cancellationToken);
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

        var externalIdentity = ToExternalIdentity(row.IdentityNamespace, row.IdentityValue);
        var isTvImport = row.Kind is EntityKind.Video or EntityKind.VideoSeason;
        var tvImportCheckpoint = isTvImport
            ? TvImportCheckpointJson.Deserialize(row.ImportCheckpointJson)
            : null;
        var importPlacementCheckpoint = isTvImport
            ? null
            : ImportPlacementCheckpointJson.Deserialize(row.ImportCheckpointJson);

        return new AcquisitionImportContext(
            row.Id, row.Title, row.Author, row.Series, row.Year, row.PosterUrl, externalIdentity,
            row.ProfileId, transfer?.ContentPath, transfer?.ClientItemId, transfer?.DownloadClientConfigId, row.Description,
            row.Kind, row.TargetLibraryRootId, row.SeasonNumber, row.EpisodeNumber, row.EntityId, row.FinalSourcePath,
            tvImportCheckpoint, importPlacementCheckpoint);
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
            .Select(transfer => new {
                transfer.ClientItemId,
                transfer.DownloadClientConfigId,
                transfer.Category,
                transfer.State
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new AcquisitionTransferInfo(
            row.Status,
            row.FinalSourcePath,
            transfer?.ClientItemId,
            transfer?.DownloadClientConfigId,
            transfer?.Category,
            transfer?.State);
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

    public async Task SetTvImportCheckpointAsync(
        Guid acquisitionId,
        TvImportCheckpoint? checkpoint,
        CancellationToken cancellationToken) {
        var row = await db.Acquisitions.FirstOrDefaultAsync(row => row.Id == acquisitionId, cancellationToken);
        if (row is null) {
            return;
        }

        row.ImportCheckpointJson = checkpoint is null ? null : TvImportCheckpointJson.Serialize(checkpoint);
        row.ImportClaimJobId = checkpoint?.ClaimJobId;
        if (checkpoint is null) {
            // Abandoning a superseded partial import clears any legacy final-path anchor and its hint,
            // so neither can masquerade as successful content or bind the next release.
            row.FinalSourcePath = null;
            var staleHints = await db.AcquisitionImportHints
                .Where(hint => hint.AcquisitionId == acquisitionId)
                .ToArrayAsync(cancellationToken);
            db.AcquisitionImportHints.RemoveRange(staleHints);
        }
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryCreateTvImportCheckpointAsync(
        Guid acquisitionId,
        TvImportCheckpoint checkpoint,
        CancellationToken cancellationToken) {
        var checkpointJson = TvImportCheckpointJson.Serialize(checkpoint);
        var transferClientItemId = checkpoint.TransferClientItemId;
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational()) {
            return await db.Acquisitions
                .Where(row => row.Id == acquisitionId
                    && row.Status == AcquisitionStatus.Importing
                    && row.ImportCheckpointJson == null
                    && row.ImportClaimJobId == checkpoint.ClaimJobId
                    && (transferClientItemId == null || db.DownloadTransfers.Any(transfer =>
                        transfer.AcquisitionId == acquisitionId
                        && transfer.ClientItemId == transferClientItemId)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.ImportCheckpointJson, checkpointJson)
                    .SetProperty(row => row.UpdatedAt, now), cancellationToken) == 1;
        }

        var row = await db.Acquisitions.FirstOrDefaultAsync(value => value.Id == acquisitionId, cancellationToken);
        if (row is null
            || row.Status != AcquisitionStatus.Importing
            || row.ImportCheckpointJson is not null
            || row.ImportClaimJobId != checkpoint.ClaimJobId
            || (transferClientItemId is not null && !await db.DownloadTransfers.AnyAsync(transfer =>
                transfer.AcquisitionId == acquisitionId
                && transfer.ClientItemId == transferClientItemId, cancellationToken))) {
            return false;
        }

        row.ImportCheckpointJson = checkpointJson;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryClaimTvImportCheckpointAsync(
        Guid acquisitionId,
        TvImportCheckpoint checkpoint,
        Guid claimJobId,
        CancellationToken cancellationToken) {
        var expectedJson = TvImportCheckpointJson.Serialize(checkpoint);
        var claimedCheckpoint = checkpoint with { ClaimJobId = claimJobId };
        var claimedJson = TvImportCheckpointJson.Serialize(claimedCheckpoint);
        var canResumeImporting = checkpoint.ClaimJobId == Guid.Empty || checkpoint.ClaimJobId == claimJobId;
        var claimable = new[] {
            AcquisitionStatus.Downloaded,
            AcquisitionStatus.Failed,
            AcquisitionStatus.ManualImportRequired,
        };
        var now = DateTimeOffset.UtcNow;

        if (db.Database.IsRelational()) {
            return await db.Acquisitions
                .Where(row => row.Id == acquisitionId
                    && row.ImportCheckpointJson == expectedJson
                    && (claimable.Contains(row.Status)
                        || (canResumeImporting
                            && row.Status == AcquisitionStatus.Importing
                            && row.ImportClaimJobId == claimJobId)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.Status, AcquisitionStatus.Importing)
                    .SetProperty(row => row.StatusMessage, (string?)null)
                    .SetProperty(row => row.ImportCheckpointJson, claimedJson)
                    .SetProperty(row => row.ImportClaimJobId, claimJobId)
                    .SetProperty(row => row.UpdatedAt, now), cancellationToken) == 1;
        }

        var row = await db.Acquisitions.FirstOrDefaultAsync(value => value.Id == acquisitionId, cancellationToken);
        if (row?.ImportCheckpointJson != expectedJson
            || (!claimable.Contains(row.Status)
                && !(canResumeImporting
                    && row.Status == AcquisitionStatus.Importing
                    && row.ImportClaimJobId == claimJobId))) {
            return false;
        }

        row.Status = AcquisitionStatus.Importing;
        row.StatusMessage = null;
        row.ImportCheckpointJson = claimedJson;
        row.ImportClaimJobId = claimJobId;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryClearTvImportCheckpointAsync(
        Guid acquisitionId,
        TvImportCheckpoint checkpoint,
        CancellationToken cancellationToken) {
        var expectedJson = TvImportCheckpointJson.Serialize(checkpoint);
        var supersedable = new[] {
            AcquisitionStatus.AwaitingSelection,
            AcquisitionStatus.Failed,
            AcquisitionStatus.Cancelled,
            AcquisitionStatus.ManualImportRequired,
        };
        var now = DateTimeOffset.UtcNow;
        int affected;
        if (db.Database.IsRelational()) {
            affected = await db.Acquisitions
                .Where(row => row.Id == acquisitionId
                    && row.ImportCheckpointJson == expectedJson
                    && supersedable.Contains(row.Status))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.ImportCheckpointJson, (string?)null)
                    .SetProperty(row => row.FinalSourcePath, (string?)null)
                    .SetProperty(row => row.ImportClaimJobId, (Guid?)null)
                    .SetProperty(row => row.UpdatedAt, now), cancellationToken);
            if (affected == 1) {
                await db.AcquisitionImportHints
                    .Where(hint => hint.AcquisitionId == acquisitionId)
                    .ExecuteDeleteAsync(cancellationToken);
            }
            return affected == 1;
        }

        var row = await db.Acquisitions.FirstOrDefaultAsync(value => value.Id == acquisitionId, cancellationToken);
        if (row?.ImportCheckpointJson != expectedJson || !supersedable.Contains(row.Status)) {
            return false;
        }

        row.ImportCheckpointJson = null;
        row.FinalSourcePath = null;
        row.ImportClaimJobId = null;
        row.UpdatedAt = now;
        db.AcquisitionImportHints.RemoveRange(await db.AcquisitionImportHints
            .Where(hint => hint.AcquisitionId == acquisitionId)
            .ToArrayAsync(cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> IsCurrentTvImportCheckpointAsync(
        Guid acquisitionId,
        TvImportCheckpoint checkpoint,
        CancellationToken cancellationToken) {
        var expectedJson = TvImportCheckpointJson.Serialize(checkpoint);
        return await db.Acquisitions.AsNoTracking().AnyAsync(row =>
            row.Id == acquisitionId
            && row.Status == AcquisitionStatus.Importing
            && row.ImportClaimJobId == checkpoint.ClaimJobId
            && row.ImportCheckpointJson == expectedJson,
            cancellationToken);
    }

    public async Task<bool> TryCreateImportPlacementCheckpointAsync(
        Guid acquisitionId,
        ImportPlacementCheckpoint checkpoint,
        CancellationToken cancellationToken) {
        var checkpointJson = ImportPlacementCheckpointJson.Serialize(checkpoint);
        var transferClientItemId = checkpoint.TransferClientItemId;
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational()) {
            return await db.Acquisitions
                .Where(row => row.Id == acquisitionId
                    && row.Kind == checkpoint.Kind
                    && row.Status == AcquisitionStatus.Importing
                    && row.ImportCheckpointJson == null
                    && row.ImportClaimJobId == checkpoint.ClaimJobId
                    && (transferClientItemId == null || db.DownloadTransfers.Any(transfer =>
                        transfer.AcquisitionId == acquisitionId
                        && transfer.ClientItemId == transferClientItemId)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.ImportCheckpointJson, checkpointJson)
                    .SetProperty(row => row.UpdatedAt, now), cancellationToken) == 1;
        }

        var row = await db.Acquisitions.FirstOrDefaultAsync(value => value.Id == acquisitionId, cancellationToken);
        if (row is null
            || row.Kind != checkpoint.Kind
            || row.Status != AcquisitionStatus.Importing
            || row.ImportCheckpointJson is not null
            || row.ImportClaimJobId != checkpoint.ClaimJobId
            || (transferClientItemId is not null && !await db.DownloadTransfers.AnyAsync(transfer =>
                transfer.AcquisitionId == acquisitionId
                && transfer.ClientItemId == transferClientItemId, cancellationToken))) {
            return false;
        }

        row.ImportCheckpointJson = checkpointJson;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryAdvanceImportPlacementCheckpointAsync(
        Guid acquisitionId,
        ImportPlacementCheckpoint expected,
        ImportPlacementCheckpoint advanced,
        CancellationToken cancellationToken) {
        if (!ImportPlacementCheckpointJson.IsValidAdvance(expected, advanced)) {
            throw new InvalidOperationException("An import placement checkpoint may only complete one immutable unit at a time.");
        }

        var expectedJson = ImportPlacementCheckpointJson.Serialize(expected);
        var advancedJson = ImportPlacementCheckpointJson.Serialize(advanced);
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational()) {
            return await db.Acquisitions
                .Where(row => row.Id == acquisitionId
                    && row.Kind == expected.Kind
                    && row.Status == AcquisitionStatus.Importing
                    && row.ImportClaimJobId == expected.ClaimJobId
                    && row.ImportCheckpointJson == expectedJson)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.ImportCheckpointJson, advancedJson)
                    .SetProperty(row => row.UpdatedAt, now), cancellationToken) == 1;
        }

        var row = await db.Acquisitions.FirstOrDefaultAsync(value => value.Id == acquisitionId, cancellationToken);
        if (row is null
            || row.Kind != expected.Kind
            || row.Status != AcquisitionStatus.Importing
            || row.ImportClaimJobId != expected.ClaimJobId
            || row.ImportCheckpointJson != expectedJson) {
            return false;
        }

        row.ImportCheckpointJson = advancedJson;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryClaimImportPlacementCheckpointAsync(
        Guid acquisitionId,
        ImportPlacementCheckpoint checkpoint,
        Guid claimJobId,
        CancellationToken cancellationToken) {
        var expectedJson = ImportPlacementCheckpointJson.Serialize(checkpoint);
        var claimedCheckpoint = checkpoint with { ClaimJobId = claimJobId };
        var claimedJson = ImportPlacementCheckpointJson.Serialize(claimedCheckpoint);
        var canResumeImporting = checkpoint.ClaimJobId == Guid.Empty || checkpoint.ClaimJobId == claimJobId;
        var claimable = new[] {
            AcquisitionStatus.Downloaded,
            AcquisitionStatus.Failed,
            AcquisitionStatus.ManualImportRequired,
        };
        var now = DateTimeOffset.UtcNow;

        if (db.Database.IsRelational()) {
            return await db.Acquisitions
                .Where(row => row.Id == acquisitionId
                    && row.Kind == checkpoint.Kind
                    && row.ImportCheckpointJson == expectedJson
                    && (claimable.Contains(row.Status)
                        || (canResumeImporting
                            && row.Status == AcquisitionStatus.Importing
                            && row.ImportClaimJobId == claimJobId)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.Status, AcquisitionStatus.Importing)
                    .SetProperty(row => row.StatusMessage, (string?)null)
                    .SetProperty(row => row.ImportCheckpointJson, claimedJson)
                    .SetProperty(row => row.ImportClaimJobId, claimJobId)
                    .SetProperty(row => row.UpdatedAt, now), cancellationToken) == 1;
        }

        var row = await db.Acquisitions.FirstOrDefaultAsync(value => value.Id == acquisitionId, cancellationToken);
        if (row?.Kind != checkpoint.Kind
            || row.ImportCheckpointJson != expectedJson
            || (!claimable.Contains(row.Status)
                && !(canResumeImporting
                    && row.Status == AcquisitionStatus.Importing
                    && row.ImportClaimJobId == claimJobId))) {
            return false;
        }

        row.Status = AcquisitionStatus.Importing;
        row.StatusMessage = null;
        row.ImportCheckpointJson = claimedJson;
        row.ImportClaimJobId = claimJobId;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryClearImportPlacementCheckpointAsync(
        Guid acquisitionId,
        ImportPlacementCheckpoint checkpoint,
        CancellationToken cancellationToken) {
        var expectedJson = ImportPlacementCheckpointJson.Serialize(checkpoint);
        var supersedable = new[] {
            AcquisitionStatus.AwaitingSelection,
            AcquisitionStatus.Failed,
            AcquisitionStatus.Cancelled,
            AcquisitionStatus.ManualImportRequired,
        };
        var now = DateTimeOffset.UtcNow;
        int affected;
        if (db.Database.IsRelational()) {
            affected = await db.Acquisitions
                .Where(row => row.Id == acquisitionId
                    && row.Kind == checkpoint.Kind
                    && row.ImportCheckpointJson == expectedJson
                    && supersedable.Contains(row.Status))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.ImportCheckpointJson, (string?)null)
                    .SetProperty(row => row.FinalSourcePath, (string?)null)
                    .SetProperty(row => row.ImportClaimJobId, (Guid?)null)
                    .SetProperty(row => row.UpdatedAt, now), cancellationToken);
            if (affected == 1) {
                await db.AcquisitionImportHints
                    .Where(hint => hint.AcquisitionId == acquisitionId)
                    .ExecuteDeleteAsync(cancellationToken);
            }
            return affected == 1;
        }

        var row = await db.Acquisitions.FirstOrDefaultAsync(value => value.Id == acquisitionId, cancellationToken);
        if (row?.Kind != checkpoint.Kind
            || row.ImportCheckpointJson != expectedJson
            || !supersedable.Contains(row.Status)) {
            return false;
        }

        row.ImportCheckpointJson = null;
        row.FinalSourcePath = null;
        row.ImportClaimJobId = null;
        row.UpdatedAt = now;
        db.AcquisitionImportHints.RemoveRange(await db.AcquisitionImportHints
            .Where(hint => hint.AcquisitionId == acquisitionId)
            .ToArrayAsync(cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> IsCurrentImportPlacementCheckpointAsync(
        Guid acquisitionId,
        ImportPlacementCheckpoint checkpoint,
        CancellationToken cancellationToken) {
        var expectedJson = ImportPlacementCheckpointJson.Serialize(checkpoint);
        return await db.Acquisitions.AsNoTracking().AnyAsync(row =>
            row.Id == acquisitionId
            && row.Kind == checkpoint.Kind
            && row.Status == AcquisitionStatus.Importing
            && row.ImportClaimJobId == checkpoint.ClaimJobId
            && row.ImportCheckpointJson == expectedJson,
            cancellationToken);
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
        if (context.ExternalIdentity is { } externalIdentity) {
            externalIds[externalIdentity.Namespace] = externalIdentity.Value;
        }

        db.AcquisitionImportHints.Add(new AcquisitionImportHintRow {
            Id = Guid.NewGuid(),
            AcquisitionId = acquisitionId,
            EntityId = wantedEntityId,
            SourcePath = sourcePath,
            IdentityNamespace = context.ExternalIdentity?.Namespace,
            IdentityValue = context.ExternalIdentity?.Value,
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

    private static ExternalIdentity? ToExternalIdentity(string? identityNamespace, string? identityValue) {
        if (string.IsNullOrWhiteSpace(identityNamespace) || string.IsNullOrWhiteSpace(identityValue)) {
            return null;
        }

        try {
            return new ExternalIdentity(identityNamespace, identityValue);
        } catch (ArgumentException) {
            // Legacy rows may contain a partial or transient locator from before acquisition identities
            // were validated at the application boundary. Treat those rows as having no stable identity.
            return null;
        }
    }

    public async Task<bool> AnyOpenForEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
        await db.Acquisitions.AsNoTracking().AnyAsync(
            row => row.EntityId == entityId
                && row.Status != AcquisitionStatus.Imported
                && row.Status != AcquisitionStatus.Cancelled,
            cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListIdsForEntityAsync(
        Guid entityId,
        CancellationToken cancellationToken) {
        var result = await db.Acquisitions.AsNoTracking()
            .Where(row => row.EntityId == entityId)
            .OrderByDescending(row => row.CreatedAt)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);
        var visited = result.ToHashSet();
        IReadOnlyList<Guid> frontier = result.ToArray();
        while (frontier.Count > 0) {
            var parentIds = frontier.ToArray();
            var children = await db.Acquisitions.AsNoTracking()
                .Where(row => row.UpgradeOfAcquisitionId != null
                    && parentIds.Contains(row.UpgradeOfAcquisitionId.Value))
                .OrderBy(row => row.CreatedAt)
                .Select(row => row.Id)
                .ToArrayAsync(cancellationToken);
            var next = new List<Guid>(children.Length);
            foreach (var childId in children) {
                if (!visited.Add(childId)) {
                    continue;
                }

                result.Add(childId);
                next.Add(childId);
            }
            frontier = next;
        }
        return result;
    }

    public async Task<AcquisitionDetail?> GetLatestForEntityAsync(Guid entityId, CancellationToken cancellationToken) {
        var id = await db.Acquisitions
            .AsNoTracking()
            .Where(row => row.EntityId == entityId)
            .OrderByDescending(row => row.CreatedAt)
            .Select(row => (Guid?)row.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return id is { } acquisitionId ? await GetAsync(acquisitionId, cancellationToken) : null;
    }

    public async Task<IReadOnlyDictionary<Guid, AcquisitionSummary>> ListLatestSummariesForEntityIdsAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken) {
        var requestedIds = entityIds.Distinct().ToArray();
        if (requestedIds.Length == 0) {
            return new Dictionary<Guid, AcquisitionSummary>();
        }

        var rows = await db.Acquisitions.AsNoTracking()
            .Where(row => row.EntityId != null && requestedIds.Contains(row.EntityId.Value))
            .OrderByDescending(row => row.CreatedAt)
            .ToArrayAsync(cancellationToken);
        var latest = rows
            .GroupBy(row => row.EntityId!.Value)
            .Select(group => group.First())
            .ToArray();
        var progress = await LatestProgressAsync(latest.Select(row => row.Id).ToArray(), cancellationToken);
        return latest.ToDictionary(
            row => row.EntityId!.Value,
            row => ToSummary(row, progress.GetValueOrDefault(row.Id)));
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
            progress, row.CreatedAt, row.UpdatedAt, row.Description, row.Kind, row.EntityId,
            HasResumableImport: row.ImportCheckpointJson is not null);

    private static ReleaseCandidateView ToView(ReleaseCandidateRow row) =>
        new(row.Id, row.IndexerName, row.Title, row.SizeBytes, row.Seeders, row.Peers, row.Protocol, row.Accepted,
            row.Score, DecodeRejections(row.RejectionsJson), row.InfoUrl, row.PublishedAt);

    private static IReadOnlyList<ReleaseRejectionReason> DecodeRejections(string json) {
        var codes = JsonSerializer.Deserialize<string[]>(json) ?? [];
        return codes.Select(code => code.DecodeAs<ReleaseRejectionReason>()).ToArray();
    }
}
