using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Files;
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
        if (existing is null && folderPath is not null) {
            existing = await AdoptLegacyComicSeriesAsync(folderPath, cancellationToken);
        }
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
        existing ??= await AdoptLegacyComicVolumeAsync(
            seriesEntityId,
            volumeNumber,
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
        if (existing is null && sourceProvenance is null) {
            existing = await AdoptLegacyComicInstallmentAsync(
                archivePath,
                cancellationToken);
        }
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
        await PromoteLegacyComicProgressAsync(installmentId, now, cancellationToken);
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
        var removed = await RemoveStaleEntitiesBySourcePath(
            installmentIds,
            validArchivePaths,
            cancellationToken);

        // Any rooted image-archive Book still present after materialization was not adopted by
        // an extant archive. Comic scanning owns this final legacy cleanup; the prose-book scan
        // deliberately leaves these rows alone so independent job ordering remains safe.
        var legacyRootIds = await _db.EntityLibraryRoots.AsNoTracking()
            .Where(root => root.LibraryRootId == rootId)
            .Join(
                _db.Entities.AsNoTracking().Where(entity =>
                    entity.KindCode == EntityKind.Book.ToCode()),
                root => root.EntityId,
                entity => entity.Id,
                (_, entity) => entity.Id)
            .Join(
                _db.BookDetails.AsNoTracking().Where(detail =>
                    detail.Format == BookFormat.ImageArchive),
                entityId => entityId,
                detail => detail.EntityId,
                (entityId, _) => entityId)
            .ToListAsync(cancellationToken);
        if (legacyRootIds.Count > 0) {
            removed += await RemoveEntitiesByIdAsync(legacyRootIds, cancellationToken);
        }
        return removed;
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

    private async Task<EntityRow?> AdoptLegacyComicSeriesAsync(
        string folderPath,
        CancellationToken cancellationToken) {
        var bookCode = EntityKind.Book.ToCode();
        var candidates = await _db.EntityFiles.AsNoTracking()
            .Where(file => file.Role == EntityFileRole.Source)
            .Join(
                _db.Entities.Where(entity => entity.KindCode == bookCode),
                file => file.EntityId,
                entity => entity.Id,
                (file, entity) => new { file.Path, EntityId = entity.Id })
            .Join(
                _db.BookDetails.Where(detail => detail.Format == BookFormat.ImageArchive),
                candidate => candidate.EntityId,
                detail => detail.EntityId,
                (candidate, _) => candidate)
            .ToArrayAsync(cancellationToken);
        var candidate = candidates.FirstOrDefault(item =>
            FileSystemPathComparison.Equals(item.Path, folderPath));
        if (candidate is null) {
            return null;
        }

        var entity = await _db.Entities.FindAsync([candidate.EntityId], cancellationToken);
        if (entity is null) {
            return null;
        }
        entity.KindCode = EntityKind.ComicSeries.ToCode();
        await RemoveLegacyBookPayloadAsync(entity.Id, cancellationToken);
        var sourceFiles = await _db.EntityFiles
            .Where(file =>
                file.EntityId == entity.Id &&
                file.Role == EntityFileRole.Source &&
                file.Path == candidate.Path)
            .ToArrayAsync(cancellationToken);
        _db.EntityFiles.RemoveRange(sourceFiles);
        return entity;
    }

    private async Task<EntityRow?> AdoptLegacyComicVolumeAsync(
        Guid seriesEntityId,
        int volumeNumber,
        CancellationToken cancellationToken) {
        var legacy = await _db.Entities.FirstOrDefaultAsync(row =>
            row.KindCode == EntityKind.BookVolume.ToCode() &&
            row.ParentEntityId == seriesEntityId &&
            row.SortOrder == volumeNumber,
            cancellationToken);
        if (legacy is not null) {
            legacy.KindCode = EntityKind.ComicVolume.ToCode();
        }
        return legacy;
    }

    private async Task<EntityRow?> AdoptLegacyComicInstallmentAsync(
        string archivePath,
        CancellationToken cancellationToken) {
        var bookCode = EntityKind.Book.ToCode();
        var chapterCode = EntityKind.BookChapter.ToCode();
        var candidates = await _db.EntityFiles.AsNoTracking()
            .Where(file => file.Role == EntityFileRole.Source)
            .Join(
                _db.Entities.Where(entity =>
                    entity.KindCode == chapterCode ||
                    (entity.KindCode == bookCode && _db.BookDetails.Any(detail =>
                        detail.EntityId == entity.Id &&
                        detail.Format == BookFormat.ImageArchive))),
                file => file.EntityId,
                entity => entity.Id,
                (file, entity) => new { file.Path, EntityId = entity.Id, entity.KindCode })
            .ToArrayAsync(cancellationToken);
        var candidate = candidates.FirstOrDefault(item =>
            FileSystemPathComparison.Equals(item.Path, archivePath));
        if (candidate is null) {
            return null;
        }

        var entity = await _db.Entities.FindAsync([candidate.EntityId], cancellationToken);
        if (entity is null) {
            return null;
        }
        var wasBook = candidate.KindCode == bookCode;
        entity.KindCode = EntityKind.ComicInstallment.ToCode();
        if (wasBook) {
            await RemoveLegacyBookPayloadAsync(entity.Id, cancellationToken);
        } else {
            var chapterDetail = await _db.BookChapterDetails.FindAsync(
                [entity.Id],
                cancellationToken);
            if (chapterDetail is not null) {
                _db.BookChapterDetails.Remove(chapterDetail);
            }
        }
        var legacyPages = await _db.Entities
            .Where(entity =>
                entity.ParentEntityId == candidate.EntityId &&
                entity.KindCode == EntityKind.BookPage.ToCode())
            .ToArrayAsync(cancellationToken);
        _db.Entities.RemoveRange(legacyPages);
        return entity;
    }

    private async Task RemoveLegacyBookPayloadAsync(
        Guid entityId,
        CancellationToken cancellationToken) {
        var bookDetail = await _db.BookDetails.FindAsync([entityId], cancellationToken);
        if (bookDetail is not null) {
            _db.BookDetails.Remove(bookDetail);
        }

        _db.BookReadingChapters.RemoveRange(await _db.BookReadingChapters
            .Where(row => row.BookId == entityId)
            .ToArrayAsync(cancellationToken));
        _db.BookChapterAudioMappings.RemoveRange(await _db.BookChapterAudioMappings
            .Where(row => row.BookId == entityId)
            .ToArrayAsync(cancellationToken));
        var contentState = await _db.BookContentStates.FindAsync([entityId], cancellationToken);
        if (contentState is not null) {
            _db.BookContentStates.Remove(contentState);
        }
    }

    private async Task PromoteLegacyComicProgressAsync(
        Guid installmentId,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        var legacyStates = await _db.UserEntityStates
            .Where(state =>
                state.EntityId != installmentId &&
                state.ProgressCurrentEntityId == installmentId)
            .ToArrayAsync(cancellationToken);
        foreach (var legacy in legacyStates) {
            var target = await _db.UserEntityStates.FindAsync(
                [legacy.UserId, installmentId],
                cancellationToken);
            if (target is null) {
                target = new UserEntityStateRow {
                    UserId = legacy.UserId,
                    EntityId = installmentId,
                    UpdatedAt = now
                };
                _db.UserEntityStates.Add(target);
            } else if (target.ProgressUpdatedAt is not null &&
                (legacy.ProgressUpdatedAt is null || target.ProgressUpdatedAt >= legacy.ProgressUpdatedAt)) {
                continue;
            }

            target.ProgressCurrentEntityId = installmentId;
            target.ProgressUnit = legacy.ProgressUnit;
            target.ProgressIndex = legacy.ProgressIndex;
            target.ProgressTotal = legacy.ProgressTotal;
            target.ProgressMode = legacy.ProgressMode;
            target.ProgressLocation = legacy.ProgressLocation;
            target.ProgressCompletedAt = legacy.ProgressCompletedAt;
            target.ProgressUpdatedAt = legacy.ProgressUpdatedAt;
            target.ProgressConsumedCount = legacy.ProgressConsumedCount;
            target.UpdatedAt = now;
        }
    }

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
