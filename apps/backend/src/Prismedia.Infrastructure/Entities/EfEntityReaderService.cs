using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Prismedia.Application.Entities;
using Prismedia.Application.Files;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// EF-backed generic reader and manifest store. Page bytes are resolved only through the owning
/// Entity's source file and exact persisted archive member; callers cannot supply a member path.
/// </summary>
public sealed class EfEntityReaderService(
    PrismediaDbContext db,
    IEntityVisibilityChecker visibility) : IEntityReaderService, IEntityPageManifestStore {
    /// <inheritdoc />
    public async Task<EntityReaderManifestResponse?> GetManifestAsync(
        Guid entityId,
        CancellationToken cancellationToken) {
        if (entityId == Guid.Empty || !await visibility.IsVisibleAsync(entityId, cancellationToken)) {
            return null;
        }
        if (!await HasSourceAsync(entityId, cancellationToken)) {
            return null;
        }

        var header = await db.EntityPageManifests.AsNoTracking()
            .SingleOrDefaultAsync(row => row.EntityId == entityId, cancellationToken);
        if (header is null) {
            return null;
        }
        var rows = await db.EntityPageEntries.AsNoTracking()
            .Where(row => row.EntityId == entityId)
            .OrderBy(row => row.Ordinal)
            .ToArrayAsync(cancellationToken);

        var manifest = TryConstruct(header, rows);
        return manifest is null
            ? null
            : new EntityReaderManifestResponse(
                manifest.EntityId,
                manifest.Direction,
                manifest.DefaultMode,
                manifest.CoverOrdinal,
                manifest.Pages.Select(page => new EntityReaderManifestPage(
                    page.Ordinal,
                    page.MimeType,
                    page.Width,
                    page.Height,
                    page.PageType,
                    page.IsDoublePage,
                    page.Checksum)).ToArray());
    }

    /// <inheritdoc />
    public async Task<EntityReaderPageSource?> GetPageAsync(
        Guid entityId,
        int ordinal,
        CancellationToken cancellationToken) {
        if (entityId == Guid.Empty || ordinal < 0 ||
            !await visibility.IsVisibleAsync(entityId, cancellationToken)) {
            return null;
        }

        var source = await db.EntityFiles.AsNoTracking()
            .Where(row => row.EntityId == entityId && row.Role == EntityFileRole.Source)
            .Select(row => row.Path)
            .SingleOrDefaultAsync(cancellationToken);
        var row = await db.EntityPageEntries.AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.EntityId == entityId && candidate.Ordinal == ordinal,
                cancellationToken);
        if (string.IsNullOrWhiteSpace(source) || row is null || !IsValid(row)) {
            return null;
        }

        var path = Directory.Exists(source)
            ? ResolveDirectoryMember(source, row.ArchiveMember)
            : EntitySourcePath.ArchiveMember(source, row.ArchiveMember);
        return path is null ? null : new EntityReaderPageSource(path, row.MimeType);
    }

    /// <inheritdoc />
    public async Task<bool> ReplaceAsync(
        EntityPageManifest manifest,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!await db.Entities.AsNoTracking().AnyAsync(
                row => row.Id == manifest.EntityId,
                cancellationToken) ||
            !await HasSourceAsync(manifest.EntityId, cancellationToken)) {
            throw new InvalidOperationException(
                $"Entity '{manifest.EntityId}' must exist and own a source before receiving a page manifest.");
        }

        var header = await db.EntityPageManifests
            .SingleOrDefaultAsync(row => row.EntityId == manifest.EntityId, cancellationToken);
        var existingPages = await db.EntityPageEntries
            .Where(row => row.EntityId == manifest.EntityId)
            .OrderBy(row => row.Ordinal)
            .ToArrayAsync(cancellationToken);
        if (Matches(header, existingPages, manifest)) {
            return false;
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try {
            db.EntityPageEntries.RemoveRange(existingPages);
            if (header is null) {
                header = new EntityPageManifestRow { EntityId = manifest.EntityId };
                db.EntityPageManifests.Add(header);
            }
            header.Direction = manifest.Direction;
            header.DefaultMode = manifest.DefaultMode;
            header.CoverOrdinal = manifest.CoverOrdinal;
            header.SourceSignature = manifest.SourceSignature;
            header.UpdatedAt = DateTimeOffset.UtcNow;

            // Flush removals before reusing ordinals or exact-member unique keys. PostgreSQL keeps
            // both saves atomic inside this transaction; the in-memory provider is test-only.
            await db.SaveChangesAsync(cancellationToken);
            db.EntityPageEntries.AddRange(manifest.Pages.Select(page => new EntityPageEntryRow {
                EntityId = manifest.EntityId,
                Ordinal = page.Ordinal,
                ArchiveMember = page.ArchiveMember,
                MimeType = page.MimeType,
                Width = page.Width,
                Height = page.Height,
                PageType = page.PageType,
                IsDoublePage = page.IsDoublePage,
                Checksum = page.Checksum
            }));
            await EntityPageCountPersistence.SetAsync(
                db,
                manifest.EntityId,
                manifest.Pages.Count,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) {
                await transaction.CommitAsync(cancellationToken);
            }
            return true;
        } catch {
            if (transaction is not null) {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(Guid entityId, CancellationToken cancellationToken) {
        var header = await db.EntityPageManifests
            .SingleOrDefaultAsync(row => row.EntityId == entityId, cancellationToken);
        if (header is null) {
            return false;
        }

        var pages = await db.EntityPageEntries
            .Where(row => row.EntityId == entityId)
            .ToArrayAsync(cancellationToken);
        db.EntityPageEntries.RemoveRange(pages);
        db.EntityPageManifests.Remove(header);
        await EntityPageCountPersistence.SetAsync(db, entityId, 0, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private Task<bool> HasSourceAsync(Guid entityId, CancellationToken cancellationToken) =>
        db.EntityFiles.AsNoTracking().AnyAsync(
            row => row.EntityId == entityId && row.Role == EntityFileRole.Source,
            cancellationToken);

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken) =>
        db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private static EntityPageManifest? TryConstruct(
        EntityPageManifestRow header,
        IReadOnlyList<EntityPageEntryRow> rows) {
        try {
            return new EntityPageManifest(
                header.EntityId,
                header.Direction,
                header.DefaultMode,
                header.CoverOrdinal,
                header.SourceSignature,
                rows.Select(ToDomain));
        } catch (ArgumentException) {
            return null;
        }
    }

    private static bool IsValid(EntityPageEntryRow row) {
        try {
            _ = ToDomain(row);
            return true;
        } catch (ArgumentException) {
            return false;
        }
    }

    private static EntityPageEntry ToDomain(EntityPageEntryRow row) => new(
        row.Ordinal,
        row.ArchiveMember,
        row.MimeType,
        row.Width,
        row.Height,
        row.PageType,
        row.IsDoublePage,
        row.Checksum);

    private static bool Matches(
        EntityPageManifestRow? header,
        IReadOnlyList<EntityPageEntryRow> rows,
        EntityPageManifest manifest) =>
        header is not null &&
        header.SourceSignature == manifest.SourceSignature &&
        header.Direction == manifest.Direction &&
        header.DefaultMode == manifest.DefaultMode &&
        header.CoverOrdinal == manifest.CoverOrdinal &&
        rows.Count == manifest.Pages.Count &&
        rows.Zip(manifest.Pages).All(pair =>
            pair.First.Ordinal == pair.Second.Ordinal &&
            pair.First.ArchiveMember == pair.Second.ArchiveMember &&
            pair.First.MimeType == pair.Second.MimeType &&
            pair.First.Width == pair.Second.Width &&
            pair.First.Height == pair.Second.Height &&
            pair.First.PageType == pair.Second.PageType &&
            pair.First.IsDoublePage == pair.Second.IsDoublePage &&
            pair.First.Checksum == pair.Second.Checksum);

    private static string? ResolveDirectoryMember(string directory, string member) {
        var root = Path.GetFullPath(directory);
        var candidate = Path.GetFullPath(Path.Combine(
            root,
            member.Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(root, candidate);
        return relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            ? null
            : candidate;
    }
}
