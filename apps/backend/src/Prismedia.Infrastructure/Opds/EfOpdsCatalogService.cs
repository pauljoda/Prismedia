using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Opds;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Opds;

/// <summary>
/// EF-backed OPDS catalog projection. Every feed starts from <see cref="VisibleBooksQuery"/>
/// so library, NSFW, and acquisition-file checks are applied before any navigation
/// group or count can expose metadata.
/// </summary>
public sealed class EfOpdsCatalogService(
    PrismediaDbContext db,
    AssetPathService assets) : IOpdsCatalogService {
    private static readonly string BookKindCode = EntityKindRegistry.Book.Code;
    private static readonly string PersonKindCode = EntityKindRegistry.Person.Code;
    private static readonly string CollectionKindCode = EntityKindRegistry.Collection.Code;
    private static readonly string TagKindCode = EntityKindRegistry.Tag.Code;
    private static readonly string CastRelationshipCode = RelationshipKind.Cast.ToCode();
    private static readonly string CreditsRelationshipCode = RelationshipKind.Credits.ToCode();
    private static readonly string TagsRelationshipCode = RelationshipKind.Tags.ToCode();
    private static readonly EntityFileRole[] CoverRoles = [.. EntityCoverSelection.CoverRoles, EntityFileRole.GridThumbnail];

    public async Task<int> CountVisibleBooksAsync(bool hideNsfw, CancellationToken cancellationToken) =>
        await VisibleBooksQuery(hideNsfw).CountAsync(cancellationToken);

    public Task<OpdsCatalogPage<OpdsBookEntry>> ListRecentAsync(
        bool hideNsfw,
        OpdsPageRequest page,
        CancellationToken cancellationToken) =>
        PageBooksAsync(
            VisibleBooksQuery(hideNsfw)
                .OrderByDescending(book => book.CreatedAt)
                .ThenBy(book => book.Id),
            hideNsfw,
            page,
            cancellationToken);

    public async Task<OpdsCatalogPage<OpdsNavigationEntry>> ListLibrariesAsync(
        bool hideNsfw,
        OpdsPageRequest page,
        CancellationToken cancellationToken) =>
        PageNavigationRows(await LibraryNavigationRowsAsync(hideNsfw, cancellationToken), page);

    public async Task<OpdsCatalogPage<OpdsBookEntry>?> ListLibraryBooksAsync(
        Guid libraryId,
        bool hideNsfw,
        OpdsPageRequest page,
        CancellationToken cancellationToken) {
        var rootVisible = await db.LibraryRoots.AsNoTracking()
            .AnyAsync(root =>
                root.Id == libraryId &&
                root.Enabled &&
                root.ScanBooks &&
                (!hideNsfw || !root.IsNsfw),
                cancellationToken);
        if (!rootVisible) {
            return null;
        }

        return await PageBooksAsync(
            VisibleBooksQuery(hideNsfw)
                .Where(book => book.LibraryRootId == libraryId)
                .OrderBy(book => book.SortName)
                .ThenBy(book => book.Id),
            hideNsfw,
            page,
            cancellationToken);
    }

    public async Task<OpdsCatalogPage<OpdsNavigationEntry>> ListAuthorsAsync(
        bool hideNsfw,
        OpdsPageRequest page,
        CancellationToken cancellationToken) =>
        PageNavigationRows(await AuthorNavigationRowsAsync(hideNsfw, cancellationToken), page);

    public async Task<OpdsCatalogPage<OpdsBookEntry>?> ListAuthorBooksAsync(
        Guid authorId,
        bool hideNsfw,
        OpdsPageRequest page,
        CancellationToken cancellationToken) {
        if (!await IsVisibleNavigationEntityAsync(authorId, PersonKindCode, hideNsfw, cancellationToken)) {
            return null;
        }

        var linkedBookIds = await AuthorBookIdsAsync(authorId, cancellationToken);
        var query = VisibleBooksQuery(hideNsfw)
            .Where(book => linkedBookIds.Contains(book.Id));
        if (!await query.AnyAsync(cancellationToken)) {
            return null;
        }

        return await PageBooksAsync(
            query
                .OrderBy(book => book.SortName)
                .ThenBy(book => book.Id),
            hideNsfw,
            page,
            cancellationToken);
    }

    public async Task<OpdsCatalogPage<OpdsNavigationEntry>> ListSeriesAsync(
        bool hideNsfw,
        OpdsPageRequest page,
        CancellationToken cancellationToken) =>
        PageNavigationRows(await SeriesNavigationRowsAsync(hideNsfw, cancellationToken), page);

    public async Task<OpdsCatalogPage<OpdsBookEntry>?> ListSeriesBooksAsync(
        Guid seriesId,
        bool hideNsfw,
        OpdsPageRequest page,
        CancellationToken cancellationToken) {
        if (!await IsVisibleNavigationEntityAsync(seriesId, BookKindCode, hideNsfw, cancellationToken)) {
            return null;
        }

        var query = VisibleBooksQuery(hideNsfw)
            .Where(book => book.SeriesId == seriesId);
        if (!await query.AnyAsync(cancellationToken)) {
            return null;
        }

        return await PageBooksAsync(
            query
                .OrderBy(book => book.SortName)
                .ThenBy(book => book.Id),
            hideNsfw,
            page,
            cancellationToken);
    }

    public async Task<OpdsCatalogPage<OpdsNavigationEntry>> ListCollectionsAsync(
        bool hideNsfw,
        OpdsPageRequest page,
        CancellationToken cancellationToken) =>
        PageNavigationRows(await CollectionNavigationRowsAsync(hideNsfw, cancellationToken), page);

    public async Task<OpdsCatalogPage<OpdsBookEntry>?> ListCollectionBooksAsync(
        Guid collectionId,
        bool hideNsfw,
        OpdsPageRequest page,
        CancellationToken cancellationToken) {
        if (!await IsVisibleNavigationEntityAsync(collectionId, CollectionKindCode, hideNsfw, cancellationToken)) {
            return null;
        }

        var linkedBookIds = await CollectionBookIdsAsync(collectionId, cancellationToken);
        var query = VisibleBooksQuery(hideNsfw)
            .Where(book => linkedBookIds.Contains(book.Id));
        if (!await query.AnyAsync(cancellationToken)) {
            return null;
        }

        return await PageBooksAsync(
            query
                .OrderBy(book => book.SortName)
                .ThenBy(book => book.Id),
            hideNsfw,
            page,
            cancellationToken);
    }

    public async Task<OpdsCatalogPage<OpdsNavigationEntry>> ListTagsAsync(
        bool hideNsfw,
        OpdsPageRequest page,
        CancellationToken cancellationToken) =>
        PageNavigationRows(await TagNavigationRowsAsync(hideNsfw, cancellationToken), page);

    public async Task<OpdsCatalogPage<OpdsBookEntry>?> ListTagBooksAsync(
        Guid tagId,
        bool hideNsfw,
        OpdsPageRequest page,
        CancellationToken cancellationToken) {
        if (!await IsVisibleNavigationEntityAsync(tagId, TagKindCode, hideNsfw, cancellationToken)) {
            return null;
        }

        var linkedBookIds = await TagBookIdsAsync(tagId, cancellationToken);
        var query = VisibleBooksQuery(hideNsfw)
            .Where(book => linkedBookIds.Contains(book.Id));
        if (!await query.AnyAsync(cancellationToken)) {
            return null;
        }

        return await PageBooksAsync(
            query
                .OrderBy(book => book.SortName)
                .ThenBy(book => book.Id),
            hideNsfw,
            page,
            cancellationToken);
    }

    public async Task<OpdsCatalogPage<OpdsBookEntry>> SearchBooksAsync(
        string? query,
        bool hideNsfw,
        OpdsPageRequest page,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(query)) {
            return new OpdsCatalogPage<OpdsBookEntry>([], 0, page.Page, page.Limit);
        }

        var term = query.Trim().ToLower();
        var visible = VisibleBooksQuery(hideNsfw)
            .Where(book =>
                book.Title.ToLower().Contains(term) ||
                db.EntityDescriptions.Any(description =>
                    description.EntityId == book.Id &&
                    description.Value.ToLower().Contains(term)) ||
                db.EntityRelationshipLinks.Any(link =>
                    link.EntityId == book.Id &&
                    db.Entities.Any(target =>
                        target.Id == link.TargetEntityId &&
                        (!hideNsfw || !target.IsNsfw) &&
                        target.Title.ToLower().Contains(term))));

        return await PageBooksAsync(
            visible
                .OrderBy(book => book.SortName)
                .ThenBy(book => book.Id),
            hideNsfw,
            page,
            cancellationToken);
    }

    public async Task<OpdsBookEntry?> GetBookAsync(
        Guid bookId,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var row = await VisibleBooksQuery(hideNsfw)
            .Where(book => book.Id == bookId)
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null) {
            return null;
        }

        return (await MaterializeBooksAsync([row], hideNsfw, cancellationToken)).SingleOrDefault();
    }

    public async Task<OpdsFileContent?> GetBookDownloadAsync(
        Guid bookId,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var row = await VisibleBooksQuery(hideNsfw)
            .Where(book => book.Id == bookId)
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null) {
            return null;
        }

        var downloadPath = DownloadPathFor(row.SourcePath);
        var contentType = ContentTypeFor(row.Format, downloadPath, row.SourceMimeType);
        return new OpdsFileContent(
            row.Id,
            EntityFileRole.Source,
            downloadPath,
            contentType,
            DownloadFileNameFor(downloadPath, contentType));
    }

    public async Task<OpdsFileContent?> GetBookCoverAsync(
        Guid bookId,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (!await VisibleBooksQuery(hideNsfw).AnyAsync(book => book.Id == bookId, cancellationToken)) {
            return null;
        }

        var files = await db.EntityFiles.AsNoTracking()
            .Where(file => file.EntityId == bookId && CoverRoles.Contains(file.Role))
            .ToArrayAsync(cancellationToken);
        var selected = EntityCoverSelection.Select(files.Where(file => file.Role != EntityFileRole.GridThumbnail)) ??
            files.FirstOrDefault(file => file.Role == EntityFileRole.GridThumbnail);

        return selected is null ? null : ToFileContent(selected);
    }

    private IQueryable<VisibleBookRow> VisibleBooksQuery(bool hideNsfw) {
        var sourceRows = db.EntityFiles.AsNoTracking()
            .Where(file => file.Role == EntityFileRole.Source);

        return
            from entity in db.Entities.AsNoTracking()
            join detail in db.BookDetails.AsNoTracking() on entity.Id equals detail.EntityId
            join source in sourceRows on entity.Id equals source.EntityId
            join rootCandidate in db.LibraryRoots.AsNoTracking() on detail.LibraryRootId equals rootCandidate.Id into rootRows
            from root in rootRows.DefaultIfEmpty()
            join parentCandidate in db.Entities.AsNoTracking() on entity.ParentEntityId equals parentCandidate.Id into parentRows
            from parent in parentRows.DefaultIfEmpty()
            where entity.KindCode == BookKindCode &&
                  (detail.LibraryRootId == null || (root != null && root.Enabled)) &&
                  (detail.Format == BookFormat.Epub ||
                   detail.Format == BookFormat.Pdf ||
                   detail.Format == BookFormat.ImageArchive) &&
                  (!hideNsfw ||
                   (!entity.IsNsfw &&
                    (root == null || !root.IsNsfw) &&
                    (parent == null || !parent.IsNsfw)))
            select new VisibleBookRow {
                Id = entity.Id,
                Title = entity.Title,
                SortName = entity.SortName,
                SeriesId = entity.ParentEntityId,
                SeriesTitle = parent == null ? null : parent.Title,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt,
                BookType = detail.BookType,
                Format = detail.Format,
                LibraryRootId = detail.LibraryRootId,
                SourcePath = source.Path,
                SourceMimeType = source.MimeType,
                SizeBytes = source.SizeBytes
            };
    }

    private async Task<IReadOnlyList<NavigationProjection>> LibraryNavigationRowsAsync(
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var visible = await VisibleBooksQuery(hideNsfw)
            .Where(book => book.LibraryRootId != null)
            .ToArrayAsync(cancellationToken);
        if (visible.Length == 0) {
            return [];
        }

        var rootIds = visible.Select(book => book.LibraryRootId!.Value).Distinct().ToArray();
        var roots = await db.LibraryRoots.AsNoTracking()
            .Where(root =>
                rootIds.Contains(root.Id) &&
                root.Enabled &&
                root.ScanBooks &&
                (!hideNsfw || !root.IsNsfw))
            .Select(root => new { root.Id, Title = root.Label, SortName = root.Label })
            .ToArrayAsync(cancellationToken);
        var rootsById = roots.ToDictionary(root => root.Id);

        return visible
            .Where(book => book.LibraryRootId is { } rootId && rootsById.ContainsKey(rootId))
            .GroupBy(book => book.LibraryRootId!.Value)
            .Select(group => {
                var root = rootsById[group.Key];
                return new NavigationProjection(
                    root.Id,
                    root.Title,
                    root.SortName,
                    group.Select(book => book.Id).Distinct().Count(),
                    group.Max(book => book.UpdatedAt));
            })
            .OrderBy(row => row.SortName)
            .ThenBy(row => row.Id)
            .ToArray();
    }

    private Task<IReadOnlyList<NavigationProjection>> AuthorNavigationRowsAsync(
        bool hideNsfw,
        CancellationToken cancellationToken) =>
        RelationshipNavigationRowsAsync(
            hideNsfw,
            PersonKindCode,
            [CastRelationshipCode, CreditsRelationshipCode],
            cancellationToken);

    private async Task<IReadOnlyList<NavigationProjection>> SeriesNavigationRowsAsync(
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var visible = await VisibleBooksQuery(hideNsfw)
            .Where(book => book.SeriesId != null)
            .ToArrayAsync(cancellationToken);
        if (visible.Length == 0) {
            return [];
        }

        var seriesIds = visible.Select(book => book.SeriesId!.Value).Distinct().ToArray();
        var seriesRows = await db.Entities.AsNoTracking()
            .Where(entity =>
                seriesIds.Contains(entity.Id) &&
                entity.KindCode == BookKindCode &&
                (!hideNsfw || !entity.IsNsfw))
            .Select(entity => new { entity.Id, entity.Title, entity.SortName })
            .ToArrayAsync(cancellationToken);
        var seriesById = seriesRows.ToDictionary(row => row.Id);

        return visible
            .Where(book => book.SeriesId is { } seriesId && seriesById.ContainsKey(seriesId))
            .GroupBy(book => book.SeriesId!.Value)
            .Select(group => {
                var series = seriesById[group.Key];
                return new NavigationProjection(
                    series.Id,
                    series.Title,
                    SortNameFor(series.SortName, series.Title),
                    group.Select(book => book.Id).Distinct().Count(),
                    group.Max(book => book.UpdatedAt));
            })
            .OrderBy(row => row.SortName)
            .ThenBy(row => row.Id)
            .ToArray();
    }

    private async Task<IReadOnlyList<NavigationProjection>> CollectionNavigationRowsAsync(
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var visible = await VisibleBooksQuery(hideNsfw).ToArrayAsync(cancellationToken);
        if (visible.Length == 0) {
            return [];
        }

        var visibleIds = visible.Select(book => book.Id).Distinct().ToArray();
        var updatedByBookId = visible.ToDictionary(book => book.Id, book => book.UpdatedAt);
        var items = await db.CollectionItemDetails.AsNoTracking()
            .Where(item => visibleIds.Contains(item.ItemEntityId))
            .Select(item => new { item.CollectionEntityId, item.ItemEntityId })
            .ToArrayAsync(cancellationToken);
        if (items.Length == 0) {
            return [];
        }

        var collectionIds = items.Select(item => item.CollectionEntityId).Distinct().ToArray();
        var collections = await db.Entities.AsNoTracking()
            .Where(entity =>
                collectionIds.Contains(entity.Id) &&
                entity.KindCode == CollectionKindCode &&
                (!hideNsfw || !entity.IsNsfw))
            .Select(entity => new { entity.Id, entity.Title, entity.SortName })
            .ToArrayAsync(cancellationToken);
        var collectionsById = collections.ToDictionary(collection => collection.Id);

        return items
            .Where(item => collectionsById.ContainsKey(item.CollectionEntityId))
            .GroupBy(item => item.CollectionEntityId)
            .Select(group => {
                var collection = collectionsById[group.Key];
                return new NavigationProjection(
                    collection.Id,
                    collection.Title,
                    SortNameFor(collection.SortName, collection.Title),
                    group.Select(item => item.ItemEntityId).Distinct().Count(),
                    group.Max(item => updatedByBookId[item.ItemEntityId]));
            })
            .OrderBy(row => row.SortName)
            .ThenBy(row => row.Id)
            .ToArray();
    }

    private Task<IReadOnlyList<NavigationProjection>> TagNavigationRowsAsync(
        bool hideNsfw,
        CancellationToken cancellationToken) =>
        RelationshipNavigationRowsAsync(
            hideNsfw,
            TagKindCode,
            [TagsRelationshipCode],
            cancellationToken);

    private async Task<IReadOnlyList<NavigationProjection>> RelationshipNavigationRowsAsync(
        bool hideNsfw,
        string targetKindCode,
        IReadOnlyCollection<string> relationshipCodes,
        CancellationToken cancellationToken) {
        var visible = await VisibleBooksQuery(hideNsfw).ToArrayAsync(cancellationToken);
        if (visible.Length == 0) {
            return [];
        }

        var visibleIds = visible.Select(book => book.Id).Distinct().ToArray();
        var updatedByBookId = visible.ToDictionary(book => book.Id, book => book.UpdatedAt);
        var links = await db.EntityRelationshipLinks.AsNoTracking()
            .Where(link =>
                visibleIds.Contains(link.EntityId) &&
                link.TargetKindCode == targetKindCode &&
                relationshipCodes.Contains(link.RelationshipCode))
            .Select(link => new { link.EntityId, link.TargetEntityId })
            .ToArrayAsync(cancellationToken);
        if (links.Length == 0) {
            return [];
        }

        var targetIds = links.Select(link => link.TargetEntityId).Distinct().ToArray();
        var targets = await db.Entities.AsNoTracking()
            .Where(entity =>
                targetIds.Contains(entity.Id) &&
                entity.KindCode == targetKindCode &&
                (!hideNsfw || !entity.IsNsfw))
            .Select(entity => new { entity.Id, entity.Title, entity.SortName })
            .ToArrayAsync(cancellationToken);
        var targetsById = targets.ToDictionary(target => target.Id);

        return links
            .Where(link => targetsById.ContainsKey(link.TargetEntityId))
            .GroupBy(link => link.TargetEntityId)
            .Select(group => {
                var target = targetsById[group.Key];
                return new NavigationProjection(
                    target.Id,
                    target.Title,
                    SortNameFor(target.SortName, target.Title),
                    group.Select(link => link.EntityId).Distinct().Count(),
                    group.Max(link => updatedByBookId[link.EntityId]));
            })
            .OrderBy(row => row.SortName)
            .ThenBy(row => row.Id)
            .ToArray();
    }

    private Task<bool> IsVisibleNavigationEntityAsync(
        Guid id,
        string kindCode,
        bool hideNsfw,
        CancellationToken cancellationToken) =>
        db.Entities.AsNoTracking()
            .AnyAsync(entity =>
                entity.Id == id &&
                entity.KindCode == kindCode &&
                (!hideNsfw || !entity.IsNsfw),
                cancellationToken);

    private async Task<Guid[]> AuthorBookIdsAsync(Guid authorId, CancellationToken cancellationToken) =>
        await db.EntityRelationshipLinks.AsNoTracking()
            .Where(link =>
                link.TargetEntityId == authorId &&
                link.TargetKindCode == PersonKindCode &&
                (link.RelationshipCode == CastRelationshipCode ||
                 link.RelationshipCode == CreditsRelationshipCode))
            .Select(link => link.EntityId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

    private async Task<Guid[]> CollectionBookIdsAsync(Guid collectionId, CancellationToken cancellationToken) =>
        await db.CollectionItemDetails.AsNoTracking()
            .Where(item => item.CollectionEntityId == collectionId)
            .Select(item => item.ItemEntityId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

    private async Task<Guid[]> TagBookIdsAsync(Guid tagId, CancellationToken cancellationToken) =>
        await db.EntityRelationshipLinks.AsNoTracking()
            .Where(link =>
                link.TargetEntityId == tagId &&
                link.TargetKindCode == TagKindCode &&
                link.RelationshipCode == TagsRelationshipCode)
            .Select(link => link.EntityId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

    private async Task<OpdsCatalogPage<OpdsBookEntry>> PageBooksAsync(
        IQueryable<VisibleBookRow> query,
        bool hideNsfw,
        OpdsPageRequest page,
        CancellationToken cancellationToken) {
        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .Skip(page.Offset)
            .Take(page.Limit)
            .ToArrayAsync(cancellationToken);
        var entries = await MaterializeBooksAsync(rows, hideNsfw, cancellationToken);
        return new OpdsCatalogPage<OpdsBookEntry>(entries, total, page.Page, page.Limit);
    }

    private static OpdsCatalogPage<OpdsNavigationEntry> PageNavigationRows(
        IReadOnlyList<NavigationProjection> rows,
        OpdsPageRequest page) {
        var total = rows.Count;
        var pageRows = rows
            .Skip(page.Offset)
            .Take(page.Limit)
            .ToArray();
        return new OpdsCatalogPage<OpdsNavigationEntry>(
            pageRows.Select(row => new OpdsNavigationEntry(row.Id, row.Title, row.VisibleBookCount, row.UpdatedAt)).ToArray(),
            total,
            page.Page,
            page.Limit);
    }

    private async Task<IReadOnlyList<OpdsBookEntry>> MaterializeBooksAsync(
        IReadOnlyList<VisibleBookRow> rows,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (rows.Count == 0) {
            return [];
        }

        var ids = rows.Select(row => row.Id).Distinct().ToArray();
        var descriptions = await db.EntityDescriptions.AsNoTracking()
            .Where(description => ids.Contains(description.EntityId))
            .ToDictionaryAsync(description => description.EntityId, description => description.Value, cancellationToken);
        var authorsByBook = await AuthorsByBookAsync(ids, hideNsfw, cancellationToken);
        var categoriesByBook = await CategoriesByBookAsync(ids, hideNsfw, cancellationToken);
        var coversByBook = await CoversByBookAsync(ids, cancellationToken);

        return rows
            .Select(row => {
                var cover = coversByBook.GetValueOrDefault(row.Id);
                var downloadPath = DownloadPathFor(row.SourcePath);
                var contentType = ContentTypeFor(row.Format, downloadPath, row.SourceMimeType);
                return new OpdsBookEntry(
                    row.Id,
                    row.Title,
                    descriptions.GetValueOrDefault(row.Id),
                    row.CreatedAt,
                    row.UpdatedAt,
                    row.BookType,
                    row.Format,
                    row.SeriesId,
                    row.SeriesTitle,
                    authorsByBook.GetValueOrDefault(row.Id) ?? [],
                    categoriesByBook.GetValueOrDefault(row.Id) ?? [],
                    contentType,
                    SizeBytesFor(downloadPath, row.SizeBytes),
                    cover?.CoverContentType,
                    cover?.ThumbnailContentType);
            })
            .ToArray();
    }

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<OpdsContributor>>> AuthorsByBookAsync(
        IReadOnlyCollection<Guid> ids,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var rows = await (
            from link in db.EntityRelationshipLinks.AsNoTracking()
            join person in db.Entities.AsNoTracking() on link.TargetEntityId equals person.Id
            where ids.Contains(link.EntityId) &&
                  link.TargetKindCode == PersonKindCode &&
                  person.KindCode == PersonKindCode &&
                  (!hideNsfw || !person.IsNsfw) &&
                  (link.RelationshipCode == CastRelationshipCode ||
                   link.RelationshipCode == CreditsRelationshipCode)
            orderby link.SortOrder, person.SortName, person.Id
            select new { link.EntityId, person.Id, person.Title }).ToArrayAsync(cancellationToken);

        return rows
            .GroupBy(row => row.EntityId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<OpdsContributor>)group
                    .GroupBy(row => row.Id)
                    .Select(item => new OpdsContributor(item.Key, item.First().Title))
                    .ToArray());
    }

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<OpdsCategory>>> CategoriesByBookAsync(
        IReadOnlyCollection<Guid> ids,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var rows = await (
            from link in db.EntityRelationshipLinks.AsNoTracking()
            join tag in db.Entities.AsNoTracking() on link.TargetEntityId equals tag.Id
            where ids.Contains(link.EntityId) &&
                  link.RelationshipCode == TagsRelationshipCode &&
                  link.TargetKindCode == TagKindCode &&
                  tag.KindCode == TagKindCode &&
                  (!hideNsfw || !tag.IsNsfw)
            orderby link.SortOrder, tag.SortName, tag.Id
            select new { link.EntityId, tag.Id, tag.Title }).ToArrayAsync(cancellationToken);

        return rows
            .GroupBy(row => row.EntityId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<OpdsCategory>)group
                    .GroupBy(row => row.Id)
                    .Select(item => new OpdsCategory(item.Key, item.First().Title))
                    .ToArray());
    }

    private async Task<IReadOnlyDictionary<Guid, CoverProjection>> CoversByBookAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken) {
        var rows = await db.EntityFiles.AsNoTracking()
            .Where(file => ids.Contains(file.EntityId) && CoverRoles.Contains(file.Role))
            .ToArrayAsync(cancellationToken);

        return rows
            .GroupBy(file => file.EntityId)
            .Select(group => {
                var cover = EntityCoverSelection.Select(group.Where(file => file.Role != EntityFileRole.GridThumbnail));
                var thumbnail = group.FirstOrDefault(file => file.Role == EntityFileRole.GridThumbnail) ?? cover;
                return new {
                    EntityId = group.Key,
                    Cover = cover is not null && HasUsableFilePath(cover.Path)
                        ? MimeForPath(cover.Path, cover.MimeType)
                        : null,
                    Thumbnail = thumbnail is not null && HasUsableFilePath(thumbnail.Path)
                        ? MimeForPath(thumbnail.Path, thumbnail.MimeType)
                        : null
                };
            })
            .Where(item => item.Cover is not null || item.Thumbnail is not null)
            .ToDictionary(
                item => item.EntityId,
                item => new CoverProjection(item.Cover, item.Thumbnail));
    }

    private OpdsFileContent? ToFileContent(EntityFileRow file) {
        var path = ResolveFilePath(file.Path);
        if (path is null) {
            return null;
        }

        return new OpdsFileContent(
            file.EntityId,
            file.Role,
            path,
            MimeForPath(file.Path, file.MimeType),
            Path.GetFileName(path));
    }

    private bool HasUsableFilePath(string path) =>
        ResolveFilePath(path) is { } resolved && File.Exists(resolved);

    private string? ResolveFilePath(string path) =>
        path.StartsWith("/assets/", StringComparison.Ordinal)
            ? assets.ResolveAssetDiskPath(path)
            : path;

    private static string SortNameFor(string? sortName, string title) =>
        string.IsNullOrWhiteSpace(sortName) ? title : sortName;

    private static string ContentTypeFor(BookFormat format, string sourcePath, string? storedMime) {
        if (!string.IsNullOrWhiteSpace(storedMime) &&
            !storedMime.Equals(MediaContentTypes.OctetStream, StringComparison.OrdinalIgnoreCase)) {
            return storedMime;
        }

        return format switch {
            BookFormat.Epub => MediaContentTypes.Epub,
            BookFormat.Pdf => MediaContentTypes.Pdf,
            BookFormat.ImageArchive => ImageArchiveMimeForPath(sourcePath, storedMime),
            _ => MediaContentTypes.OctetStream
        };
    }

    private static string ImageArchiveMimeForPath(string path, string? storedMime) {
        var downloadPath = DownloadPathFor(path);
        var mime = MimeForPath(downloadPath, storedMime);
        return mime.Equals(MediaContentTypes.OctetStream, StringComparison.OrdinalIgnoreCase) && Directory.Exists(downloadPath)
            ? MediaContentTypes.ComicBookZip
            : mime;
    }

    private static string DownloadPathFor(string sourcePath) {
        if (!Directory.Exists(sourcePath)) {
            return sourcePath;
        }

        return EnumerateContainedAcquisitionFiles(sourcePath).FirstOrDefault() ?? sourcePath;
    }

    private static IEnumerable<string> EnumerateContainedAcquisitionFiles(string directory) {
        IEnumerable<string> files;
        try {
            files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories);
        } catch {
            return [];
        }

        return files
            .Where(IsAcquisitionFile)
            .OrderBy(file => Path.GetRelativePath(directory, file), StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsAcquisitionFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".epub" or ".pdf" or ".cbz" or ".zip" or ".cbr";

    private static long? SizeBytesFor(string downloadPath, long? storedSize) {
        if (!File.Exists(downloadPath)) {
            return storedSize;
        }

        try {
            return new FileInfo(downloadPath).Length;
        } catch {
            return storedSize;
        }
    }

    private static string DownloadFileNameFor(string path, string contentType) {
        var fileName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(fileName)) {
            fileName = "book";
        }

        return contentType.Equals(MediaContentTypes.ComicBookZip, StringComparison.OrdinalIgnoreCase) &&
               string.IsNullOrEmpty(Path.GetExtension(fileName))
            ? $"{fileName}.cbz"
            : fileName;
    }

    private static string MimeForPath(string path, string? storedMime) {
        if (!string.IsNullOrWhiteSpace(storedMime) &&
            !storedMime.Equals(MediaContentTypes.OctetStream, StringComparison.OrdinalIgnoreCase)) {
            return storedMime;
        }

        return Path.GetExtension(path).ToLowerInvariant() switch {
            ".epub" => MediaContentTypes.Epub,
            ".pdf" => MediaContentTypes.Pdf,
            ".cbz" => MediaContentTypes.ComicBookZip,
            ".zip" => MediaContentTypes.Zip,
            ".cbr" => MediaContentTypes.ComicBookRar,
            ".jpg" or ".jpeg" => MediaContentTypes.ImageJpeg,
            ".png" => MediaContentTypes.ImagePng,
            ".gif" => MediaContentTypes.ImageGif,
            ".webp" => MediaContentTypes.ImageWebp,
            ".avif" => MediaContentTypes.ImageAvif,
            _ => MediaContentTypes.OctetStream
        };
    }

    private sealed class VisibleBookRow {
        public Guid Id { get; init; }
        public string Title { get; init; } = string.Empty;
        public string SortName { get; init; } = string.Empty;
        public Guid? SeriesId { get; init; }
        public string? SeriesTitle { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
        public BookType BookType { get; init; }
        public BookFormat Format { get; init; }
        public Guid? LibraryRootId { get; init; }
        public string SourcePath { get; init; } = string.Empty;
        public string? SourceMimeType { get; init; }
        public long? SizeBytes { get; init; }
    }

    private sealed record NavigationProjection(
        Guid Id,
        string Title,
        string SortName,
        int VisibleBookCount,
        DateTimeOffset UpdatedAt);

    private sealed record CoverProjection(string? CoverContentType, string? ThumbnailContentType);
}
