using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Settings;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Settings;

namespace Prismedia.Infrastructure.Media.Persistence;

/// <summary>
/// Implements entity persistence operations for library scanning against the entity schema.
/// </summary>

public sealed partial class LibraryScanPersistenceService {
    // ── Entity upsert ──

    public async Task<Guid> UpsertVideoAsync(string filePath, string title, Guid libraryRootId, bool isNsfw, CancellationToken cancellationToken) {
        var existing = await FindEntityBySourcePath(EntityKindRegistry.Video.Code, filePath, cancellationToken);
        if (existing is not null) {
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        _db.Entities.Add(new EntityRow { Id = id, KindCode = EntityKindRegistry.Video.Code, Title = title, IsNsfw = isNsfw, CreatedAt = now, UpdatedAt = now });
        _db.VideoDetails.Add(new VideoDetailRow { EntityId = id, LibraryRootId = libraryRootId });
        _db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = id,
            Role = EntityFileRole.Source,
            Path = filePath,
            SizeBytes = LibraryScanFileSystem.TryGetFileSize(filePath),
            CreatedAt = now,
            UpdatedAt = now
        });

        await _db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<Guid> UpsertImageAsync(string filePath, string title, Guid? galleryEntityId, long? sizeBytes, int sortOrder, bool isNsfw, CancellationToken cancellationToken) {
        var id = await UpsertImageCoreAsync(
            new ImageUpsertItem(filePath, title, galleryEntityId, sizeBytes, sortOrder, isNsfw),
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<IReadOnlyList<Guid>> UpsertImagesBatchAsync(
        IReadOnlyList<ImageUpsertItem> items, CancellationToken cancellationToken) {
        if (items.Count == 0) return [];

        var ids = new List<Guid>(items.Count);
        foreach (var item in items) {
            ids.Add(await UpsertImageCoreAsync(item, cancellationToken));
        }

        await _db.SaveChangesAsync(cancellationToken);
        return ids;
    }

    private async Task<Guid> UpsertImageCoreAsync(
        ImageUpsertItem item, CancellationToken cancellationToken) {
        var (filePath, title, galleryEntityId, sizeBytes, sortOrder, isNsfw) = item;
        var existing = await FindEntityBySourcePath(EntityKindRegistry.Image.Code, filePath, cancellationToken);
        if (existing is not null) {
            var updatedAt = DateTimeOffset.UtcNow;
            var tracked = await _db.Entities.FindAsync([existing.Id], cancellationToken);
            if (tracked is not null) {
                var shouldMarkAncestors = ShouldMarkAutoIdentifyAncestors(tracked, galleryEntityId);
                tracked.Title = title;
                tracked.ParentEntityId = galleryEntityId;
                tracked.SortOrder = galleryEntityId is null ? null : sortOrder;
                tracked.UpdatedAt = updatedAt;
                if (isNsfw) tracked.IsNsfw = true;
                if (shouldMarkAncestors) {
                    await MarkAutoIdentifyAncestorsUnorganizedAsync(galleryEntityId, updatedAt, cancellationToken);
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        _db.Entities.Add(new EntityRow { Id = id, KindCode = EntityKindRegistry.Image.Code, Title = title, ParentEntityId = galleryEntityId, SortOrder = galleryEntityId is null ? null : sortOrder, IsNsfw = isNsfw, CreatedAt = now, UpdatedAt = now });
        _db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = id,
            Role = EntityFileRole.Source,
            Path = filePath,
            SizeBytes = sizeBytes,
            CreatedAt = now,
            UpdatedAt = now
        });

        if (galleryEntityId is not null) {
            await UpsertStructuralChildLinkAsync(
                galleryEntityId.Value,
                id,
                sortOrder,
                now,
                    cancellationToken);
        }

        return id;
    }

    public async Task<Guid> UpsertGalleryAsync(
        string folderPath,
        string title,
        Guid libraryRootId,
        Guid? parentGalleryEntityId,
        int sortOrder,
        bool isNsfw,
        CancellationToken cancellationToken) {
        var id = await UpsertGalleryCoreAsync(
            new GalleryUpsertItem(folderPath, title, libraryRootId, parentGalleryEntityId, sortOrder, isNsfw),
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<IReadOnlyList<Guid>> UpsertGalleriesBatchAsync(
        IReadOnlyList<GalleryUpsertItem> items, CancellationToken cancellationToken) {
        if (items.Count == 0) return [];

        var ids = new List<Guid>(items.Count);
        foreach (var item in items) {
            ids.Add(await UpsertGalleryCoreAsync(item, cancellationToken));
        }

        await _db.SaveChangesAsync(cancellationToken);
        return ids;
    }

    private async Task<Guid> UpsertGalleryCoreAsync(
        GalleryUpsertItem item, CancellationToken cancellationToken) {
        var (folderPath, title, libraryRootId, parentGalleryEntityId, sortOrder, isNsfw) = item;
        var existing = await FindEntityBySourcePath(EntityKindRegistry.Gallery.Code, folderPath, cancellationToken);
        if (existing is not null) {
            var tracked = await _db.Entities.FindAsync([existing.Id], cancellationToken);
            if (tracked is not null) {
                var updatedAt = DateTimeOffset.UtcNow;
                var shouldMarkAncestors = ShouldMarkAutoIdentifyAncestors(tracked, parentGalleryEntityId);
                tracked.Title = title;
                tracked.ParentEntityId = parentGalleryEntityId;
                tracked.SortOrder = sortOrder;
                tracked.UpdatedAt = updatedAt;
                if (isNsfw) tracked.IsNsfw = true;
                if (shouldMarkAncestors) {
                    await MarkAutoIdentifyAncestorsUnorganizedAsync(parentGalleryEntityId, updatedAt, cancellationToken);
                }
            }

            var detail = await _db.GalleryDetails.FindAsync([existing.Id], cancellationToken);
            if (detail is not null) detail.LibraryRootId = libraryRootId;
            else _db.GalleryDetails.Add(new GalleryDetailRow { EntityId = existing.Id, GalleryType = GalleryType.Folder, LibraryRootId = libraryRootId });
            return existing.Id;
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        _db.Entities.Add(new EntityRow { Id = id, KindCode = EntityKindRegistry.Gallery.Code, Title = title, ParentEntityId = parentGalleryEntityId, SortOrder = sortOrder, IsNsfw = isNsfw, CreatedAt = now, UpdatedAt = now });
        _db.GalleryDetails.Add(new GalleryDetailRow { EntityId = id, GalleryType = GalleryType.Folder, LibraryRootId = libraryRootId });
        _db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = id,
            Role = EntityFileRole.Source,
            Path = folderPath,
            CreatedAt = now,
            UpdatedAt = now
        });
        await MarkAutoIdentifyAncestorsUnorganizedAsync(parentGalleryEntityId, now, cancellationToken);

        return id;
    }

    public async Task<Guid> UpsertAudioTrackAsync(string filePath, string title, Guid? audioLibraryId, int sortOrder, string? sectionLabel, int sectionOrder, bool isNsfw, CancellationToken cancellationToken) {
        var id = await UpsertAudioTrackCoreAsync(
            new AudioTrackUpsertItem(filePath, title, audioLibraryId, sortOrder, sectionLabel, sectionOrder, isNsfw),
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<IReadOnlyList<Guid>> UpsertAudioTracksBatchAsync(
        IReadOnlyList<AudioTrackUpsertItem> items, CancellationToken cancellationToken) {
        if (items.Count == 0) return [];

        var ids = new List<Guid>(items.Count);
        foreach (var item in items) {
            ids.Add(await UpsertAudioTrackCoreAsync(item, cancellationToken));
        }

        await _db.SaveChangesAsync(cancellationToken);
        return ids;
    }

    private async Task<Guid> UpsertAudioTrackCoreAsync(
        AudioTrackUpsertItem item, CancellationToken cancellationToken) {
        var (filePath, title, audioLibraryId, sortOrder, sectionLabel, sectionOrder, isNsfw) = item;
        var existing = await FindEntityBySourcePath(EntityKindRegistry.AudioTrack.Code, filePath, cancellationToken);
        if (existing is not null) {
            var updatedAt = DateTimeOffset.UtcNow;
            var tracked = await _db.Entities.FindAsync([existing.Id], cancellationToken);
            if (tracked is not null) {
                var shouldMarkAncestors = ShouldMarkAutoIdentifyAncestors(tracked, audioLibraryId);
                if (!tracked.IsOrganized) {
                    tracked.Title = title;
                }
                tracked.ParentEntityId = audioLibraryId;
                tracked.SortOrder = audioLibraryId is null ? null : sortOrder;
                tracked.UpdatedAt = updatedAt;
                if (isNsfw) tracked.IsNsfw = true;
                if (shouldMarkAncestors) {
                    await MarkAutoIdentifyAncestorsUnorganizedAsync(audioLibraryId, updatedAt, cancellationToken);
                }
            }

            await EnsureAudioTrackDetailAsync(existing.Id, sectionLabel, sectionOrder, cancellationToken);
            return existing.Id;
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        _db.Entities.Add(new EntityRow { Id = id, KindCode = EntityKindRegistry.AudioTrack.Code, Title = title, ParentEntityId = audioLibraryId, SortOrder = audioLibraryId is null ? null : sortOrder, IsNsfw = isNsfw, CreatedAt = now, UpdatedAt = now });
        _db.AudioTrackDetails.Add(new AudioTrackDetailRow { EntityId = id, SectionLabel = sectionLabel, SectionOrder = sectionOrder });
        _db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = id,
            Role = EntityFileRole.Source,
            Path = filePath,
            SizeBytes = LibraryScanFileSystem.TryGetFileSize(filePath),
            CreatedAt = now,
            UpdatedAt = now
        });
        await MarkAutoIdentifyAncestorsUnorganizedAsync(audioLibraryId, now, cancellationToken);

        return id;
    }

    public async Task<Guid> UpsertAudioLibraryAsync(
        string folderPath,
        string title,
        Guid libraryRootId,
        Guid? parentEntityId,
        int sortOrder,
        bool isNsfw,
        CancellationToken cancellationToken) {
        var id = await UpsertAudioLibraryCoreAsync(
            new AudioLibraryUpsertItem(folderPath, title, libraryRootId, parentEntityId, sortOrder, isNsfw),
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<IReadOnlyList<Guid>> UpsertAudioLibrariesBatchAsync(
        IReadOnlyList<AudioLibraryUpsertItem> items, CancellationToken cancellationToken) {
        if (items.Count == 0) return [];

        var ids = new List<Guid>(items.Count);
        foreach (var item in items) {
            ids.Add(await UpsertAudioLibraryCoreAsync(item, cancellationToken));
        }

        await _db.SaveChangesAsync(cancellationToken);
        return ids;
    }

    private async Task<Guid> UpsertAudioLibraryCoreAsync(
        AudioLibraryUpsertItem item, CancellationToken cancellationToken) {
        var (folderPath, title, libraryRootId, parentEntityId, sortOrder, isNsfw) = item;
        var existing = await FindEntityBySourcePath(EntityKindRegistry.AudioLibrary.Code, folderPath, cancellationToken);
        if (existing is not null) {
            var tracked = await _db.Entities.FindAsync([existing.Id], cancellationToken);
            if (tracked is not null) {
                var updatedAt = DateTimeOffset.UtcNow;
                var shouldMarkAncestors = ShouldMarkAutoIdentifyAncestors(tracked, parentEntityId);
                if (!tracked.IsOrganized) {
                    tracked.Title = title;
                }
                tracked.ParentEntityId = parentEntityId;
                tracked.SortOrder = sortOrder;
                tracked.UpdatedAt = updatedAt;
                if (isNsfw) tracked.IsNsfw = true;
                if (shouldMarkAncestors) {
                    await MarkAutoIdentifyAncestorsUnorganizedAsync(parentEntityId, updatedAt, cancellationToken);
                }
            }

            var detail = await _db.AudioLibraryDetails.FindAsync([existing.Id], cancellationToken);
            if (detail is not null) detail.LibraryRootId = libraryRootId;
            else _db.AudioLibraryDetails.Add(new AudioLibraryDetailRow { EntityId = existing.Id, LibraryRootId = libraryRootId });
            return existing.Id;
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        _db.Entities.Add(new EntityRow { Id = id, KindCode = EntityKindRegistry.AudioLibrary.Code, Title = title, ParentEntityId = parentEntityId, SortOrder = sortOrder, IsNsfw = isNsfw, CreatedAt = now, UpdatedAt = now });
        _db.AudioLibraryDetails.Add(new AudioLibraryDetailRow { EntityId = id, LibraryRootId = libraryRootId });
        _db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = id,
            Role = EntityFileRole.Source,
            Path = folderPath,
            CreatedAt = now,
            UpdatedAt = now
        });
        await MarkAutoIdentifyAncestorsUnorganizedAsync(parentEntityId, now, cancellationToken);

        return id;
    }

    public async Task<Guid> UpsertMusicArtistAsync(
        string folderPath,
        string title,
        Guid libraryRootId,
        int sortOrder,
        bool isNsfw,
        CancellationToken cancellationToken) {
        var id = await UpsertMusicArtistCoreAsync(
            new MusicArtistUpsertItem(folderPath, title, libraryRootId, sortOrder, isNsfw),
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<IReadOnlyList<Guid>> UpsertMusicArtistsBatchAsync(
        IReadOnlyList<MusicArtistUpsertItem> items, CancellationToken cancellationToken) {
        if (items.Count == 0) return [];

        var ids = new List<Guid>(items.Count);
        foreach (var item in items) {
            ids.Add(await UpsertMusicArtistCoreAsync(item, cancellationToken));
        }

        await _db.SaveChangesAsync(cancellationToken);
        return ids;
    }

    private async Task<Guid> UpsertMusicArtistCoreAsync(
        MusicArtistUpsertItem item, CancellationToken cancellationToken) {
        var (folderPath, title, libraryRootId, sortOrder, isNsfw) = item;
        var existing = await FindEntityBySourcePath(EntityKindRegistry.MusicArtist.Code, folderPath, cancellationToken);
        if (existing is not null) {
            var tracked = await _db.Entities.FindAsync([existing.Id], cancellationToken);
            if (tracked is not null) {
                tracked.Title = title;
                tracked.ParentEntityId = null;
                tracked.SortOrder = sortOrder;
                tracked.UpdatedAt = DateTimeOffset.UtcNow;
                if (isNsfw) tracked.IsNsfw = true;
            }

            var detail = await _db.MusicArtistDetails.FindAsync([existing.Id], cancellationToken);
            if (detail is not null) detail.LibraryRootId = libraryRootId;
            else _db.MusicArtistDetails.Add(new MusicArtistDetailRow { EntityId = existing.Id, LibraryRootId = libraryRootId });
            return existing.Id;
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        _db.Entities.Add(new EntityRow { Id = id, KindCode = EntityKindRegistry.MusicArtist.Code, Title = title, ParentEntityId = null, SortOrder = sortOrder, IsNsfw = isNsfw, CreatedAt = now, UpdatedAt = now });
        _db.MusicArtistDetails.Add(new MusicArtistDetailRow { EntityId = id, LibraryRootId = libraryRootId });
        _db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = id,
            Role = EntityFileRole.Source,
            Path = folderPath,
            CreatedAt = now,
            UpdatedAt = now
        });

        return id;
    }

    public async Task<Guid> UpsertBookAuthorAsync(
        string folderPath, string title, int? sortOrder, bool isNsfw, CancellationToken cancellationToken) {
        var existing = await FindEntityBySourcePath(EntityKindRegistry.BookAuthor.Code, folderPath, cancellationToken);
        if (existing is not null) {
            var tracked = await _db.Entities.FindAsync([existing.Id], cancellationToken);
            if (tracked is not null) {
                tracked.Title = title;
                tracked.ParentEntityId = null;
                tracked.SortOrder = sortOrder;
                tracked.UpdatedAt = DateTimeOffset.UtcNow;
                if (isNsfw) tracked.IsNsfw = true;
            }

            await _db.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        _db.Entities.Add(new EntityRow { Id = id, KindCode = EntityKindRegistry.BookAuthor.Code, Title = title, ParentEntityId = null, SortOrder = sortOrder, IsNsfw = isNsfw, CreatedAt = now, UpdatedAt = now });
        _db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = id,
            Role = EntityFileRole.Source,
            Path = folderPath,
            CreatedAt = now,
            UpdatedAt = now
        });
        await _db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<int> RemoveEmptyBookAuthorsAsync(CancellationToken cancellationToken) {
        // An author grouping with no remaining child books is stale (its books were removed or moved).
        var emptyAuthorIds = await _db.Entities
            .Where(entity => entity.KindCode == EntityKindRegistry.BookAuthor.Code)
            .Where(entity => !_db.Entities.Any(child => child.ParentEntityId == entity.Id))
            .Select(entity => entity.Id)
            .ToListAsync(cancellationToken);
        if (emptyAuthorIds.Count == 0) {
            return 0;
        }

        var rows = await _db.Entities.Where(entity => emptyAuthorIds.Contains(entity.Id)).ToListAsync(cancellationToken);
        _db.Entities.RemoveRange(rows);
        await _db.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    public async Task<Guid> UpsertBookAsync(string sourcePath, string title, Guid libraryRootId, bool isNsfw, CancellationToken cancellationToken) {
        var existing = await FindEntityBySourcePath(EntityKindRegistry.Book.Code, sourcePath, cancellationToken);
        if (existing is not null) {
            var tracked = await _db.Entities.FindAsync([existing.Id], cancellationToken);
            if (tracked is not null) {
                tracked.Title = title;
                tracked.ParentEntityId = null;
                tracked.SortOrder = null;
                tracked.UpdatedAt = DateTimeOffset.UtcNow;
                if (isNsfw) tracked.IsNsfw = true;
            }
            var detail = await _db.BookDetails.FindAsync([existing.Id], cancellationToken);
            if (detail is not null) {
                detail.LibraryRootId = libraryRootId;
                detail.BookType = BookType.Comic;
                detail.Format = BookFormat.ImageArchive;
            } else {
                _db.BookDetails.Add(new BookDetailRow { EntityId = existing.Id, BookType = BookType.Comic, Format = BookFormat.ImageArchive, LibraryRootId = libraryRootId });
            }
            await _db.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        _db.Entities.Add(new EntityRow { Id = id, KindCode = EntityKindRegistry.Book.Code, Title = title, IsNsfw = isNsfw, CreatedAt = now, UpdatedAt = now });
        _db.BookDetails.Add(new BookDetailRow { EntityId = id, BookType = BookType.Comic, Format = BookFormat.ImageArchive, LibraryRootId = libraryRootId });
        _db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = id,
            Role = EntityFileRole.Source,
            Path = sourcePath,
            SizeBytes = LibraryScanFileSystem.TryGetFileSize(sourcePath),
            CreatedAt = now,
            UpdatedAt = now
        });

        await _db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<Guid> UpsertBookSeriesAsync(
        string folderPath,
        string title,
        Guid libraryRootId,
        bool isNsfw,
        BookType bookType,
        BookFormat format,
        CancellationToken cancellationToken) {
        var existing = await FindEntityBySourcePath(EntityKindRegistry.Book.Code, folderPath, cancellationToken);
        if (existing is not null) {
            var tracked = await _db.Entities.FindAsync([existing.Id], cancellationToken);
            if (tracked is not null) {
                tracked.Title = title;
                tracked.ParentEntityId = null;
                tracked.SortOrder = null;
                tracked.UpdatedAt = DateTimeOffset.UtcNow;
                if (isNsfw) tracked.IsNsfw = true;
            }
            var detail = await _db.BookDetails.FindAsync([existing.Id], cancellationToken);
            if (detail is not null) {
                detail.LibraryRootId = libraryRootId;
                detail.BookType = bookType;
                detail.Format = format;
            } else {
                _db.BookDetails.Add(new BookDetailRow { EntityId = existing.Id, BookType = bookType, Format = format, LibraryRootId = libraryRootId });
            }
            await ReparentSingleFileBooksUnderSeriesAsync(existing.Id, folderPath, libraryRootId, DateTimeOffset.UtcNow, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        _db.Entities.Add(new EntityRow { Id = id, KindCode = EntityKindRegistry.Book.Code, Title = title, IsNsfw = isNsfw, CreatedAt = now, UpdatedAt = now });
        _db.BookDetails.Add(new BookDetailRow { EntityId = id, BookType = bookType, Format = format, LibraryRootId = libraryRootId });
        _db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = id,
            Role = EntityFileRole.Source,
            Path = folderPath,
            CreatedAt = now,
            UpdatedAt = now
        });
        await ReparentSingleFileBooksUnderSeriesAsync(id, folderPath, libraryRootId, now, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return id;
    }

    private async Task ReparentSingleFileBooksUnderSeriesAsync(
        Guid seriesId,
        string folderPath,
        Guid libraryRootId,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        var folderPrefix = EnsureTrailingSeparator(folderPath);
        var candidates = await _db.EntityFiles.AsNoTracking()
            .Where(file => file.Role == EntityFileRole.Source && file.Path.StartsWith(folderPath))
            .Join(
                _db.BookDetails,
                file => file.EntityId,
                detail => detail.EntityId,
                (file, detail) => new { file.EntityId, file.Path, detail.LibraryRootId, detail.Format })
            .Where(candidate => candidate.LibraryRootId == libraryRootId && candidate.Format != BookFormat.ImageArchive)
            .ToArrayAsync(cancellationToken);
        var childCandidates = candidates
            .Where(candidate => IsDescendantSourcePath(candidate.Path, folderPath, folderPrefix))
            .OrderBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var sortOrder = 0; sortOrder < childCandidates.Length; sortOrder++) {
            var child = _db.Entities.Local.FirstOrDefault(row => row.Id == childCandidates[sortOrder].EntityId)
                ?? await _db.Entities.FirstOrDefaultAsync(row =>
                    row.Id == childCandidates[sortOrder].EntityId &&
                    row.KindCode == EntityKindRegistry.Book.Code,
                    cancellationToken);
            if (child is null) {
                continue;
            }

            var shouldMarkAncestors = ShouldMarkAutoIdentifyAncestors(child, seriesId);
            child.ParentEntityId = seriesId;
            child.SortOrder = sortOrder;
            child.UpdatedAt = now;
            if (shouldMarkAncestors) {
                await MarkAutoIdentifyAncestorsUnorganizedAsync(seriesId, now, cancellationToken);
            }
        }
    }

    private static string EnsureTrailingSeparator(string path) {
        if (path.EndsWith(Path.DirectorySeparatorChar) ||
            path.EndsWith(Path.AltDirectorySeparatorChar)) {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }

    private static bool IsDescendantSourcePath(string sourcePath, string folderPath, string folderPrefix) =>
        !sourcePath.Equals(folderPath, StringComparison.OrdinalIgnoreCase) &&
        sourcePath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase);

    public async Task<Guid> UpsertSingleFileBookAsync(
        string sourcePath,
        string title,
        Guid libraryRootId,
        bool isNsfw,
        BookType bookType,
        BookFormat format,
        string contentType,
        Guid? parentBookEntityId,
        int? sortOrder,
        CancellationToken cancellationToken) {
        var existing = await FindEntityBySourcePath(EntityKindRegistry.Book.Code, sourcePath, cancellationToken);
        if (existing is not null) {
            var tracked = await _db.Entities.FindAsync([existing.Id], cancellationToken);
            if (tracked is not null) {
                var updatedAt = DateTimeOffset.UtcNow;
                var shouldMarkAncestors = ShouldMarkAutoIdentifyAncestors(tracked, parentBookEntityId);
                tracked.Title = title;
                tracked.ParentEntityId = parentBookEntityId;
                tracked.SortOrder = sortOrder;
                tracked.UpdatedAt = updatedAt;
                if (isNsfw) tracked.IsNsfw = true;
                if (shouldMarkAncestors) {
                    await MarkAutoIdentifyAncestorsUnorganizedAsync(parentBookEntityId, updatedAt, cancellationToken);
                }
            }
            var detail = await _db.BookDetails.FindAsync([existing.Id], cancellationToken);
            if (detail is not null) {
                detail.LibraryRootId = libraryRootId;
                detail.BookType = bookType;
                detail.Format = format;
            }
            await _db.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        _db.Entities.Add(new EntityRow { Id = id, KindCode = EntityKindRegistry.Book.Code, Title = title, ParentEntityId = parentBookEntityId, SortOrder = sortOrder, IsNsfw = isNsfw, CreatedAt = now, UpdatedAt = now });
        _db.BookDetails.Add(new BookDetailRow { EntityId = id, BookType = bookType, Format = format, LibraryRootId = libraryRootId });
        _db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = id,
            Role = EntityFileRole.Source,
            Path = sourcePath,
            MimeType = contentType,
            SizeBytes = LibraryScanFileSystem.TryGetFileSize(sourcePath),
            CreatedAt = now,
            UpdatedAt = now
        });
        await MarkAutoIdentifyAncestorsUnorganizedAsync(parentBookEntityId, now, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<Guid> UpsertBookVolumeAsync(string folderPath, string title, Guid bookEntityId, int sortOrder, bool isNsfw, CancellationToken cancellationToken) {
        var existing = await FindEntityBySourcePath(EntityKindRegistry.BookVolume.Code, folderPath, cancellationToken);
        if (existing is not null) {
            existing.Title = title;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await UpsertStructuralChildLinkAsync(
                bookEntityId,
                existing.Id,
                sortOrder,
                DateTimeOffset.UtcNow,
                cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        _db.Entities.Add(new EntityRow { Id = id, KindCode = EntityKindRegistry.BookVolume.Code, Title = title, ParentEntityId = bookEntityId, SortOrder = sortOrder, IsNsfw = isNsfw, CreatedAt = now, UpdatedAt = now });
        _db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = id,
            Role = EntityFileRole.Source,
            Path = folderPath,
            CreatedAt = now,
            UpdatedAt = now
        });
        await UpsertStructuralChildLinkAsync(
            bookEntityId,
            id,
            sortOrder,
            now,
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<Guid> UpsertBookChapterAsync(string archivePath, string title, Guid parentEntityId, int sortOrder, int pageCount, bool isNsfw, CancellationToken cancellationToken) {
        var existing = await FindEntityBySourcePath(EntityKindRegistry.BookChapter.Code, archivePath, cancellationToken);
        if (existing is not null) {
            existing.Title = title;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await UpsertStructuralChildLinkAsync(
                parentEntityId,
                existing.Id,
                sortOrder,
                DateTimeOffset.UtcNow,
                cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        _db.Entities.Add(new EntityRow { Id = id, KindCode = EntityKindRegistry.BookChapter.Code, Title = title, ParentEntityId = parentEntityId, SortOrder = sortOrder, IsNsfw = isNsfw, CreatedAt = now, UpdatedAt = now });
        _db.BookChapterDetails.Add(new BookChapterDetailRow { EntityId = id });
        _db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = id,
            Role = EntityFileRole.Source,
            Path = archivePath,
            CreatedAt = now,
            UpdatedAt = now
        });
        await UpsertStructuralChildLinkAsync(
            parentEntityId,
            id,
            sortOrder,
            now,
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<Guid> UpsertBookPageAsync(string filePath, string title, Guid bookEntityId, Guid chapterEntityId, int sortOrder, bool isNsfw, CancellationToken cancellationToken) {
        var id = await UpsertBookPageCoreAsync(
            new BookPageUpsertItem(filePath, title, bookEntityId, chapterEntityId, sortOrder, isNsfw),
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<IReadOnlyList<Guid>> UpsertBookPagesBatchAsync(
        IReadOnlyList<BookPageUpsertItem> items, CancellationToken cancellationToken) {
        if (items.Count == 0) return [];

        var ids = new List<Guid>(items.Count);
        foreach (var item in items) {
            ids.Add(await UpsertBookPageCoreAsync(item, cancellationToken));
        }

        await _db.SaveChangesAsync(cancellationToken);
        return ids;
    }

    private async Task<Guid> UpsertBookPageCoreAsync(
        BookPageUpsertItem item, CancellationToken cancellationToken) {
        var (filePath, title, bookEntityId, chapterEntityId, sortOrder, isNsfw) = item;
        var existing = await FindEntityBySourcePath(EntityKindRegistry.BookPage.Code, filePath, cancellationToken);
        if (existing is not null) {
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await UpsertStructuralChildLinkAsync(
                chapterEntityId,
                existing.Id,
                sortOrder,
                DateTimeOffset.UtcNow,
                cancellationToken);
            return existing.Id;
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        _db.Entities.Add(new EntityRow { Id = id, KindCode = EntityKindRegistry.BookPage.Code, Title = title, ParentEntityId = chapterEntityId, SortOrder = sortOrder, IsNsfw = isNsfw, CreatedAt = now, UpdatedAt = now });
        _db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = id,
            Role = EntityFileRole.Source,
            Path = filePath,
            CreatedAt = now,
            UpdatedAt = now
        });
        await UpsertStructuralChildLinkAsync(
            chapterEntityId,
            id,
            sortOrder,
            now,
            cancellationToken);

        return id;
    }

}
