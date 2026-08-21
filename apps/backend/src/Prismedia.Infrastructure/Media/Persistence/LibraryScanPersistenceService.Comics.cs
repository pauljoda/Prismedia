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
        ComicSourceProvenance? sourceProvenance,
        CancellationToken cancellationToken) {
        if (sortOrder < 0) throw new ArgumentOutOfRangeException(nameof(sortOrder));
        if (position < 0) throw new ArgumentOutOfRangeException(nameof(position));
        if (string.IsNullOrWhiteSpace(positionLabel)) {
            throw new ArgumentException("A comic installment position label is required.", nameof(positionLabel));
        }

        var existing = sourceProvenance is null
            ? await FindEntityBySourcePath(
                EntityKind.ComicInstallment.ToCode(),
                archivePath,
                cancellationToken)
            : await FindGeneratedComicInstallmentAsync(
                libraryRootId,
                sourceProvenance,
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
        if (sourceProvenance is not null) {
            await EnsureEntitySourceAsync(
                installmentId,
                EntitySourceCode.GeneratedFromFolder.ToCode(),
                sourceProvenance.OriginFolderPath,
                now,
                cancellationToken);
        }
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

    private async Task<EntityRow?> FindGeneratedComicInstallmentAsync(
        Guid libraryRootId,
        ComicSourceProvenance provenance,
        CancellationToken cancellationToken) {
        var generatedFromFolderCode = EntitySourceCode.GeneratedFromFolder.ToCode();
        var exact = await _db.EntitySources.AsNoTracking()
            .Where(source =>
                source.Code == generatedFromFolderCode &&
                source.Value == provenance.OriginFolderPath)
            .Join(
                _db.EntityLibraryRoots.AsNoTracking().Where(root =>
                    root.LibraryRootId == libraryRootId),
                source => source.EntityId,
                root => root.EntityId,
                (source, _) => source)
            .Join(
                _db.Entities.AsNoTracking().Where(entity =>
                    entity.KindCode == EntityKind.ComicInstallment.ToCode()),
                source => source.EntityId,
                entity => entity.Id,
                (_, entity) => entity)
            .FirstOrDefaultAsync(cancellationToken);
        if (exact is not null) {
            return exact;
        }

        // A manual folder rename changes the managed archive path. Rebind only when the previous
        // content signature identifies exactly one installment in this root; ambiguity creates a
        // new Entity rather than silently merging two identical releases.
        var signatureMatches = await _db.EntityLibraryRoots.AsNoTracking()
            .Where(root => root.LibraryRootId == libraryRootId)
            .Join(
                _db.EntitySources.AsNoTracking().Where(source =>
                    source.Code == generatedFromFolderCode),
                root => root.EntityId,
                source => source.EntityId,
                (_, source) => source.EntityId)
            .Join(
                _db.EntityPageManifests.AsNoTracking().Where(manifest =>
                    manifest.SourceSignature == provenance.OriginSignature),
                entityId => entityId,
                manifest => manifest.EntityId,
                (entityId, _) => entityId)
            .Take(2)
            .ToArrayAsync(cancellationToken);
        if (signatureMatches.Length != 1) {
            return null;
        }

        return await _db.Entities.AsNoTracking().FirstOrDefaultAsync(entity =>
            entity.Id == signatureMatches[0] &&
            entity.KindCode == EntityKind.ComicInstallment.ToCode(),
            cancellationToken);
    }
}
