using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Media.Persistence;

/// <summary>Serialized-comic scan upserts and cleanup.</summary>
public sealed partial class LibraryScanPersistenceService {
    /// <inheritdoc />
    public async Task<Guid> UpsertComicSeriesAsync(
        string? folderPath,
        string title,
        Guid libraryRootId,
        bool isNsfw,
        CancellationToken cancellationToken) {
        var existing = folderPath is not null
            ? await FindEntityByFolderSourcePathAsync(
                EntityKind.ComicSeries.ToCode(),
                folderPath,
                cancellationToken)
            : await FindRootComicSeriesByTitleAsync(libraryRootId, title, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var seriesId = existing?.Id ?? Guid.NewGuid();

        if (existing is null) {
            _db.Entities.Add(new EntityRow {
                Id = seriesId,
                KindCode = EntityKind.ComicSeries.ToCode(),
                Title = title,
                IsNsfw = isNsfw,
                CreatedAt = now,
                UpdatedAt = now
            });
            _db.ComicSeriesDetails.Add(new ComicSeriesDetailRow { EntityId = seriesId });
            _db.EntityLibraryRoots.Add(new EntityLibraryRootRow {
                EntityId = seriesId,
                LibraryRootId = libraryRootId
            });
        } else {
            var tracked = await FindMutableEntityAsync(seriesId, cancellationToken);
            if (tracked is not null) {
                if (!tracked.IsOrganized && !await HasExternalIdentityAsync(seriesId, cancellationToken)) {
                    tracked.Title = title;
                }
                if (isNsfw) tracked.IsNsfw = true;
                tracked.UpdatedAt = now;
            }
            if (!await _db.ComicSeriesDetails.AnyAsync(
                    row => row.EntityId == seriesId,
                    cancellationToken)) {
                _db.ComicSeriesDetails.Add(new ComicSeriesDetailRow { EntityId = seriesId });
            }
            await SetEntityLibraryRootAsync(seriesId, libraryRootId, cancellationToken);
        }

        if (folderPath is not null) {
            await EnsureEntitySourceAsync(
                seriesId,
                EntitySourceCode.Folder.ToCode(),
                folderPath,
                now,
                cancellationToken);
        }

        await SaveChangesWithLifecycleAsync(cancellationToken);
        return seriesId;
    }

    /// <inheritdoc />
    public async Task<Guid> UpsertComicVolumeAsync(
        Guid seriesEntityId,
        string title,
        int volumeNumber,
        bool isNsfw,
        CancellationToken cancellationToken) {
        if (volumeNumber < 0) {
            throw new ArgumentOutOfRangeException(nameof(volumeNumber));
        }

        var existing = await _db.Entities.FirstOrDefaultAsync(row =>
            row.KindCode == EntityKind.ComicVolume.ToCode() &&
            row.ParentEntityId == seriesEntityId &&
            row.SortOrder == volumeNumber,
            cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var volumeId = existing?.Id ?? Guid.NewGuid();
        if (existing is null) {
            _db.Entities.Add(new EntityRow {
                Id = volumeId,
                KindCode = EntityKind.ComicVolume.ToCode(),
                Title = title,
                IsNsfw = isNsfw,
                CreatedAt = now,
                UpdatedAt = now
            });
        } else {
            if (!existing.IsOrganized && !await HasExternalIdentityAsync(volumeId, cancellationToken)) {
                existing.Title = title;
            }
            if (isNsfw) existing.IsNsfw = true;
            existing.UpdatedAt = now;
        }

        await UpsertPositionAsync(
            volumeId,
            EntityPositionCodes.Volume,
            volumeNumber,
            volumeNumber.ToString(),
            now,
            cancellationToken);
        await UpsertStructuralChildLinkAsync(
            seriesEntityId,
            volumeId,
            volumeNumber,
            now,
            cancellationToken);
        await SaveChangesWithLifecycleAsync(cancellationToken);
        return volumeId;
    }

    /// <inheritdoc />
    public async Task<Guid> UpsertComicInstallmentAsync(
        string archivePath,
        string title,
        Guid libraryRootId,
        Guid parentEntityId,
        int sortOrder,
        int position,
        string positionLabel,
        ComicInstallmentKind installmentKind,
        long? sizeBytes,
        bool isNsfw,
        CancellationToken cancellationToken) {
        if (sortOrder < 0) throw new ArgumentOutOfRangeException(nameof(sortOrder));
        if (position < 0) throw new ArgumentOutOfRangeException(nameof(position));
        if (string.IsNullOrWhiteSpace(positionLabel)) {
            throw new ArgumentException("A comic installment position label is required.", nameof(positionLabel));
        }

        var existing = await FindEntityBySourcePath(
            EntityKind.ComicInstallment.ToCode(),
            archivePath,
            cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var installmentId = existing?.Id ?? Guid.NewGuid();
        if (existing is null) {
            _db.Entities.Add(new EntityRow {
                Id = installmentId,
                KindCode = EntityKind.ComicInstallment.ToCode(),
                Title = title,
                IsNsfw = isNsfw,
                CreatedAt = now,
                UpdatedAt = now
            });
            _db.ComicInstallmentDetails.Add(new ComicInstallmentDetailRow {
                EntityId = installmentId,
                InstallmentKind = installmentKind
            });
            _db.EntityLibraryRoots.Add(new EntityLibraryRootRow {
                EntityId = installmentId,
                LibraryRootId = libraryRootId
            });
        } else {
            var tracked = await FindMutableEntityAsync(installmentId, cancellationToken);
            if (tracked is not null) {
                if (!tracked.IsOrganized && !await HasExternalIdentityAsync(installmentId, cancellationToken)) {
                    tracked.Title = title;
                }
                if (isNsfw) tracked.IsNsfw = true;
                tracked.UpdatedAt = now;
            }
            var detail = await _db.ComicInstallmentDetails.FindAsync([installmentId], cancellationToken);
            if (detail is null) {
                _db.ComicInstallmentDetails.Add(new ComicInstallmentDetailRow {
                    EntityId = installmentId,
                    InstallmentKind = installmentKind
                });
            } else {
                detail.InstallmentKind = installmentKind;
            }
            await SetEntityLibraryRootAsync(installmentId, libraryRootId, cancellationToken);
        }

        await EnsureEntityFileAsync(
            installmentId,
            EntityFileRole.Source,
            archivePath,
            sizeBytes,
            now,
            cancellationToken);
        await UpsertPositionAsync(
            installmentId,
            EntityPositionCodes.Chapter,
            position,
            positionLabel,
            now,
            cancellationToken);
        await UpsertStructuralChildLinkAsync(
            parentEntityId,
            installmentId,
            sortOrder,
            now,
            cancellationToken);
        await SaveChangesWithLifecycleAsync(cancellationToken);
        return installmentId;
    }

    /// <inheritdoc />
    public async Task<int> RemoveStaleComicInstallmentsInRootAsync(
        Guid rootId,
        IReadOnlySet<string> validArchivePaths,
        CancellationToken cancellationToken) {
        var installmentIds = await _db.EntityLibraryRoots.AsNoTracking()
            .Where(row => row.LibraryRootId == rootId)
            .Join(
                _db.Entities.AsNoTracking().Where(row =>
                    row.KindCode == EntityKind.ComicInstallment.ToCode()),
                root => root.EntityId,
                entity => entity.Id,
                (_, entity) => entity.Id)
            .ToListAsync(cancellationToken);
        return await RemoveStaleEntitiesBySourcePath(
            installmentIds,
            validArchivePaths,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> RemoveEmptyComicContainersAsync(CancellationToken cancellationToken) =>
        DerivedEntityContainerPruner.PruneAsync(
            _db,
            SaveChangesWithLifecycleAsync,
            cancellationToken);

    private async Task<EntityRow?> FindRootComicSeriesByTitleAsync(
        Guid libraryRootId,
        string title,
        CancellationToken cancellationToken) {
        var candidates = await _db.EntityLibraryRoots.AsNoTracking()
            .Where(root => root.LibraryRootId == libraryRootId)
            .Join(
                _db.Entities.AsNoTracking().Where(entity =>
                    entity.KindCode == EntityKind.ComicSeries.ToCode() &&
                    entity.ParentEntityId == null),
                root => root.EntityId,
                entity => entity.Id,
                (_, entity) => entity)
            .ToArrayAsync(cancellationToken);
        return candidates.FirstOrDefault(entity =>
            entity.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
    }

    private Task<bool> HasExternalIdentityAsync(Guid entityId, CancellationToken cancellationToken) =>
        _db.EntityExternalIds.AsNoTracking().AnyAsync(
            row => row.EntityId == entityId,
            cancellationToken);
}
