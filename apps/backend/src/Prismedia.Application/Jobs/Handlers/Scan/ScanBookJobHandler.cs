using Prismedia.Application.Jobs.Handlers;
using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Jobs.Scanning;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Scan;

/// <summary>
/// Discovers comic book archives (CBZ/ZIP), creates book/chapter/page entities,
/// and chains downstream thumbnail jobs for pages.
/// </summary>
public sealed class ScanBookJobHandler(
    ILogger<ScanBookJobHandler> logger,
    IFileDiscovery fileDiscovery,
    ILibraryScanRootPersistence roots,
    IBookScanPersistence books,
    IDownstreamNeedsPersistence downstreamNeeds,
    IScanSnapshotStore? snapshots = null,
    IComicInfoMetadataReader? comicInfoReader = null,
    IScanMetadataPersistence? scanMetadata = null,
    IBookFileMetadataReader? bookFileMetadata = null,
    Acquisition.IAcquisitionHintApplier? acquisitionHints = null) : ScanJobHandler(logger, fileDiscovery, roots, snapshots) {
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tiff", ".tif"
    };

    public override JobType Type => JobType.ScanBook;

    protected override bool IsEligibleRoot(LibraryRootData root) => root.ScanBooks;

    protected override IReadOnlyList<MediaCategory> ScanCategories => [MediaCategory.ComicArchive, MediaCategory.Book];

    protected override Task OnNoFileChangesAsync(
        JobContext context, LibraryRootData root, CancellationToken cancellationToken) =>
        AutoIdentifyScanEnqueue.EnqueueExistingRootsForUnchangedScanAsync(
            context, Roots, downstreamNeeds, root, ScanCategories, cancellationToken);

    protected override async Task<ScanRootOutcome> ScanRootCoreAsync(JobContext context, LibraryRootData root, CancellationToken cancellationToken) {
        logger.LogInformation("ScanBook: discovering archives in {Path}", root.Path);
        var excludedPaths = await Roots.GetExcludedPathsForRootAsync(root.Id, cancellationToken);

        var archiveFiles = await FileDiscovery.DiscoverFilesAsync(
            root.Path, MediaCategory.ComicArchive, root.Recursive, excludedPaths, cancellationToken);
        var bookFiles = await FileDiscovery.DiscoverFilesAsync(
            root.Path, MediaCategory.Book, root.Recursive, excludedPaths, cancellationToken);

        logger.LogInformation("ScanBook: found {Count} archive files in {Label}", archiveFiles.Count, root.Label);

        return await MaterializeBookPathsAsync(
            context,
            root,
            archiveFiles,
            bookFiles,
            reconcile: true,
            acquisitionId: null,
            bestEffortHousekeeping: false,
            cancellationToken);
    }

    /// <summary>
    /// Materializes only one import's exact book files through the scanner's canonical upserts and
    /// wanted binding. It deliberately skips stale cleanup so unrelated books in the same root cannot
    /// be removed by a narrow import pass.
    /// </summary>
    public async Task MaterializeImportedPathsAsync(
        JobContext context,
        Guid acquisitionId,
        LibraryRootData root,
        IReadOnlyList<string> placedPaths,
        CancellationToken cancellationToken) {
        if (!root.Enabled || !root.ScanBooks) {
            throw new InvalidOperationException("The imported books no longer belong to an enabled book library root.");
        }

        var archiveFiles = placedPaths.Where(IsArchivePath).ToArray();
        var bookFiles = placedPaths.Where(path => BookFormatFor(path) is not null).ToArray();
        if (archiveFiles.Length + bookFiles.Length != placedPaths.Count) {
            throw new InvalidOperationException("The book import contains a file the book scanner does not support.");
        }

        await MaterializeBookPathsAsync(
            context,
            root,
            archiveFiles,
            bookFiles,
            reconcile: false,
            acquisitionId,
            bestEffortHousekeeping: true,
            cancellationToken);
    }

    private async Task<ScanRootOutcome> MaterializeBookPathsAsync(
        JobContext context,
        LibraryRootData root,
        IReadOnlyList<string> archiveFiles,
        IReadOnlyList<string> bookFiles,
        bool reconcile,
        Guid? acquisitionId,
        bool bestEffortHousekeeping,
        CancellationToken cancellationToken) {

        var settings = await Roots.GetSettingsAsync(cancellationToken);
        if (!root.AutoIdentify) {
            // Honor this root's Auto Identify opt-out without touching other generation settings.
            settings = settings with { AutoIdentifyEnabled = false };
        }
        var archiveItems = new List<BookArchiveItem>();

        foreach (var archivePath in archiveFiles.OrderBy(path => path, NaturalPathComparer.Instance)) {
            var pageMembers = ListImageMembersInZip(archivePath);
            if (pageMembers.Count == 0) {
                logger.LogDebug("ScanBook: skipping empty archive {Path}", archivePath);
                continue;
            }

            var comicInfo = comicInfoReader is null
                ? null
                : await comicInfoReader.ReadAsync(archivePath, cancellationToken);
            archiveItems.Add(BookArchiveItem.From(root.Path, archivePath, pageMembers, comicInfo));
        }

        var validBookPaths = new HashSet<string>(FileSystemPathComparison.Comparer);
        var archiveBookPaths = new HashSet<string>(FileSystemPathComparison.Comparer);
        var processedArchiveCount = 0;

        foreach (var bookGroup in archiveItems
            .GroupBy(item => item.BookPath, FileSystemPathComparison.Comparer)
            .OrderBy(group => group.Key, NaturalPathComparer.Instance)) {
            var first = bookGroup.First();
            var bookMetadata = BestBookMetadata(bookGroup);
            var bookIsNsfw = root.IsNsfw || bookGroup.Any(item => item.MarksNsfw);
            // Bind a request-created wanted entity to this path first, so the path-keyed upsert finds it
            // (attaching the imported file to the wanted entity) instead of creating a duplicate.
            if (acquisitionHints is not null) {
                await acquisitionHints.BindWantedEntityAsync(
                    EntityKind.Book, first.BookPath, cancellationToken, acquisitionId);
            }
            var bookId = await books.UpsertBookAsync(first.BookPath, first.BookTitle, root.Id, bookIsNsfw, cancellationToken);
            if (bookMetadata is not null && scanMetadata is not null) {
                await scanMetadata.ApplyComicInfoMetadataAsync(
                    bookId,
                    bookMetadata,
                    bookIsNsfw,
                    cancellationToken);
            }
            validBookPaths.Add(first.BookPath);
            archiveBookPaths.Add(first.BookPath);

            // Stamp acquisition-supplied identity (plugin/external ids) before auto-identify so it resolves ID-first.
            if (acquisitionHints is not null) {
                await acquisitionHints.ApplyAsync(bookId, first.BookPath, cancellationToken);
            }

            // A book is the top-level root of its volumes/chapters/pages, so identify it directly.
            if (bestEffortHousekeeping) {
                await ImportedMaterializationHousekeeping.TryAsync(
                    logger,
                    "Imported book is ready but its auto-identify job could not be queued.",
                    () => QueueBookAutoIdentifyAsync(
                        context, settings, bookId, first.BookTitle, cancellationToken));
            } else {
                await QueueBookAutoIdentifyAsync(
                    context, settings, bookId, first.BookTitle, cancellationToken);
            }

            var directChapterPaths = new HashSet<string>(FileSystemPathComparison.Comparer);
            var directChapters = bookGroup
                .Where(item => item.VolumePath is null)
                .OrderBy(item => item.ArchivePath, NaturalPathComparer.Instance)
                .ToArray();

            for (var chapterIndex = 0; chapterIndex < directChapters.Length; chapterIndex++) {
                await UpsertChapterPagesAsync(
                    context,
                    settings,
                    bookIsNsfw,
                    bookId,
                    directChapters[chapterIndex],
                    bookId,
                    chapterIndex,
                    directChapterPaths,
                    bestEffortHousekeeping,
                    cancellationToken);
                processedArchiveCount++;
                await ReportArchiveProgressAsync(
                    context,
                    processedArchiveCount,
                    archiveItems.Count,
                    bestEffortHousekeeping,
                    cancellationToken);
            }

            var validVolumePaths = new HashSet<string>(FileSystemPathComparison.Comparer);
            var volumeGroups = bookGroup
                .Where(item => item.VolumePath is not null)
                .GroupBy(item => item.VolumePath!, FileSystemPathComparison.Comparer)
                .OrderBy(group => group.Key, NaturalPathComparer.Instance)
                .ToArray();

            for (var volumeIndex = 0; volumeIndex < volumeGroups.Length; volumeIndex++) {
                var volumeGroup = volumeGroups[volumeIndex];
                var volumeFirst = volumeGroup.First();
                var volumePath = volumeFirst.VolumePath!;
                var volumeId = await books.UpsertBookVolumeAsync(
                    volumePath,
                    volumeFirst.VolumeTitle!,
                    bookId,
                    volumeIndex,
                    bookIsNsfw,
                    cancellationToken);
                validVolumePaths.Add(volumePath);

                var validChapterPaths = new HashSet<string>(FileSystemPathComparison.Comparer);
                var chapters = volumeGroup
                    .OrderBy(item => item.ArchivePath, NaturalPathComparer.Instance)
                    .ToArray();
                for (var chapterIndex = 0; chapterIndex < chapters.Length; chapterIndex++) {
                    await UpsertChapterPagesAsync(
                        context,
                        settings,
                        bookIsNsfw,
                        bookId,
                        chapters[chapterIndex],
                        volumeId,
                        chapterIndex,
                        validChapterPaths,
                        bestEffortHousekeeping,
                        cancellationToken);
                    processedArchiveCount++;
                    await ReportArchiveProgressAsync(
                        context,
                        processedArchiveCount,
                        archiveItems.Count,
                        bestEffortHousekeeping,
                        cancellationToken);
                }

                if (reconcile) {
                    await books.RemoveStaleBookChaptersAsync(volumeId, validChapterPaths, cancellationToken);
                }
            }

            if (reconcile) {
                await books.RemoveStaleBookChaptersAsync(bookId, directChapterPaths, cancellationToken);
                await books.RemoveStaleBookVolumesAsync(bookId, validVolumePaths, cancellationToken);
            }
        }

        await ScanSingleFileBooksAsync(
            context,
            root,
            settings,
            bookFiles,
            validBookPaths,
            archiveBookPaths,
            acquisitionId,
            bestEffortHousekeeping,
            cancellationToken);

        if (reconcile) {
            await books.RemoveStaleBooksInRootAsync(root.Id, validBookPaths, cancellationToken);
            // Author groupings whose books were all removed (or that used to be the old "series" parents) are pruned.
            await books.RemoveEmptyBookAuthorsAsync(cancellationToken);
            await Roots.RemoveEntitiesInExcludedPathsAsync(root.Id, cancellationToken);
        }

        return ScanRootOutcome.Success;
    }

    /// <summary>
    /// Discovers single-file books (EPUB/PDF) and upserts either standalone book entities
    /// for root-level files or a folder-backed book parent with child book entities.
    /// Records every source path so stale cleanup keeps the current hierarchy.
    /// </summary>
    private async Task ScanSingleFileBooksAsync(
        JobContext context,
        LibraryRootData root,
        LibrarySettingsData settings,
        IReadOnlyList<string> bookFiles,
        ISet<string> validBookPaths,
        IReadOnlySet<string> archiveBookPaths,
        Guid? acquisitionId,
        bool bestEffortHousekeeping,
        CancellationToken cancellationToken) {
        if (bookFiles.Count == 0) {
            return;
        }

        logger.LogInformation("ScanBook: found {Count} single-file books in {Label}", bookFiles.Count, root.Label);

        var items = new List<SingleFileBookItem>();
        foreach (var sourcePath in bookFiles.OrderBy(path => path, NaturalPathComparer.Instance)) {
            var format = BookFormatFor(sourcePath);
            if (format is null) {
                continue;
            }

            var metadata = bookFileMetadata is null
                ? null
                : await bookFileMetadata.ReadAsync(sourcePath, format.Value, cancellationToken);
            var fallbackTitle = Path.GetFileNameWithoutExtension(sourcePath);
            var title = FirstNonEmpty(metadata?.Title, metadata?.Series, fallbackTitle)!;
            var isNsfw = root.IsNsfw || metadata?.MarksNsfw == true;
            items.Add(SingleFileBookItem.From(root.Path, sourcePath, title, isNsfw, format.Value, metadata));
        }

        foreach (var looseItem in items
            .Where(item => item.AuthorPath is null)
            .OrderBy(item => item.SourcePath, NaturalPathComparer.Instance)) {
            await UpsertSingleFileBookAsync(
                context,
                settings,
                root,
                looseItem,
                validBookPaths,
                parentBookEntityId: null,
                sortOrder: null,
                acquisitionId,
                bestEffortHousekeeping,
                cancellationToken);
        }

        // Books under an `Author/` folder are grouped under a folder-backed author entity (like
        // Artist/Album for music). Each book is parented to its author; empty authors are pruned later.
        foreach (var authorGroup in items
            .Where(item => item.AuthorPath is not null)
            .GroupBy(item => item.AuthorPath!, FileSystemPathComparison.Comparer)
            .OrderBy(group => group.Key, NaturalPathComparer.Instance)) {
            var first = authorGroup.First();
            var authorIsNsfw = root.IsNsfw || authorGroup.Any(item => item.IsNsfw);
            // Name the author from the first book that carried embedded creator metadata (i.e. whose title
            // differs from the folder name); fall back to the folder name when none of them did.
            var folderName = Path.GetFileName(first.AuthorPath!);
            var authorTitle = authorGroup
                .Select(item => item.AuthorTitle!)
                .FirstOrDefault(name => !string.Equals(name, folderName, StringComparison.OrdinalIgnoreCase))
                ?? folderName;
            // Bind a request-created wanted author to this folder first, so the upsert reuses that entity.
            if (acquisitionHints is not null) {
                await acquisitionHints.BindWantedParentAsync(
                    EntityKind.BookAuthor, first.AuthorPath!, cancellationToken, acquisitionId);
            }
            var authorId = await books.UpsertBookAuthorAsync(
                first.AuthorPath!,
                authorTitle,
                sortOrder: null,
                authorIsNsfw,
                cancellationToken);

            var booksByAuthor = authorGroup
                .OrderBy(item => item.SourcePath, NaturalPathComparer.Instance)
                .ToArray();
            for (var index = 0; index < booksByAuthor.Length; index++) {
                await UpsertSingleFileBookAsync(
                    context,
                    settings,
                    root,
                    booksByAuthor[index],
                    validBookPaths,
                    authorId,
                    index,
                    acquisitionId,
                    bestEffortHousekeeping,
                    cancellationToken);
            }
        }
    }

    private async Task UpsertSingleFileBookAsync(
        JobContext context,
        LibrarySettingsData settings,
        LibraryRootData root,
        SingleFileBookItem item,
        ISet<string> validBookPaths,
        Guid? parentBookEntityId,
        int? sortOrder,
        Guid? acquisitionId,
        bool bestEffortHousekeeping,
        CancellationToken cancellationToken) {
        // Bind a request-created wanted entity to this path first, so the path-keyed upsert finds it
        // (attaching the imported file to the wanted entity) instead of creating a duplicate.
        if (acquisitionHints is not null) {
            await acquisitionHints.BindWantedEntityAsync(
                EntityKind.Book, item.SourcePath, cancellationToken, acquisitionId);
        }
        var bookId = await books.UpsertSingleFileBookAsync(
            item.SourcePath,
            item.Title,
            root.Id,
            item.IsNsfw,
            DefaultBookTypeFor(item.Format),
            item.Format,
            ContentTypeFor(item.Format),
            parentBookEntityId,
            sortOrder,
            cancellationToken);
        validBookPaths.Add(item.SourcePath);

        // Stamp acquisition-supplied identity before auto-identify so it resolves ID-first.
        if (acquisitionHints is not null) {
            await acquisitionHints.ApplyAsync(bookId, item.SourcePath, cancellationToken);
        }

        if (item.Metadata is not null && scanMetadata is not null) {
            await scanMetadata.ApplyComicInfoMetadataAsync(bookId, item.Metadata, item.IsNsfw, cancellationToken);
        }

        if (bestEffortHousekeeping) {
            await ImportedMaterializationHousekeeping.TryAsync(
                logger,
                "Imported book is ready but its downstream jobs could not be queued.",
                () => QueueSingleFileBookJobsAsync(
                    context, settings, bookId, item.Title, cancellationToken));
        } else {
            await QueueSingleFileBookJobsAsync(
                context, settings, bookId, item.Title, cancellationToken);
        }
    }

    private async Task QueueBookAutoIdentifyAsync(
        JobContext context,
        LibrarySettingsData settings,
        Guid bookId,
        string title,
        CancellationToken cancellationToken) {
        var request = AutoIdentifyScanEnqueue.RequestFor(
            settings,
            EntityKind.Book,
            bookId.ToString(),
            title,
            await downstreamNeeds.IsEntityOrganizedAsync(bookId, cancellationToken));
        if (request is not null) {
            await context.EnqueueIfNeededAsync(request, cancellationToken);
        }
    }

    private async Task QueueSingleFileBookJobsAsync(
        JobContext context,
        LibrarySettingsData settings,
        Guid bookId,
        string title,
        CancellationToken cancellationToken) {
        if (settings.AutoGeneratePreview &&
            !await downstreamNeeds.HasEntityFileAsync(bookId, EntityFileRole.Thumbnail, cancellationToken)) {
            await context.EnqueueIfNeededAsync(
                EnqueueJobRequest.ForEntity(
                    JobType.GenerateBookCoverThumbnail,
                    EntityKind.Book,
                    bookId.ToString(),
                    title,
                    JobPriorities.Thumbnail),
                cancellationToken);
        }

        await QueueBookAutoIdentifyAsync(context, settings, bookId, title, cancellationToken);
    }

    private async Task QueueBookPageJobsAsync(
        JobContext context,
        LibrarySettingsData settings,
        IReadOnlyList<BookPageUpsertItem> pageItems,
        IReadOnlyList<Guid> pageIds,
        CancellationToken cancellationToken) {
        for (var index = 0; index < pageItems.Count && index < pageIds.Count; index++) {
            if (!settings.AutoGeneratePreview ||
                await downstreamNeeds.HasEntityFileAsync(
                    pageIds[index], EntityFileRole.Thumbnail, cancellationToken)) {
                continue;
            }

            await context.EnqueueIfNeededAsync(
                EnqueueJobRequest.ForEntity(
                    JobType.GenerateBookPageThumbnail,
                    EntityKind.BookPage,
                    pageIds[index].ToString(),
                    pageItems[index].Title,
                    JobPriorities.Thumbnail),
                cancellationToken);
        }
    }

    private static BookFormat? BookFormatFor(string sourcePath) =>
        Path.GetExtension(sourcePath).ToLowerInvariant() switch {
            ".epub" => BookFormat.Epub,
            ".pdf" => BookFormat.Pdf,
            _ => null
        };

    private static bool IsArchivePath(string sourcePath) =>
        Path.GetExtension(sourcePath).Equals(".cbz", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(sourcePath).Equals(".zip", StringComparison.OrdinalIgnoreCase);

    private static BookType DefaultBookTypeFor(BookFormat format) =>
        format == BookFormat.Epub ? BookType.Novel : BookType.Book;

    private static string ContentTypeFor(BookFormat format) =>
        format == BookFormat.Pdf
            ? Prismedia.Contracts.Media.MediaContentTypes.Pdf
            : Prismedia.Contracts.Media.MediaContentTypes.Epub;

    private async Task UpsertChapterPagesAsync(
        JobContext context,
        LibrarySettingsData settings,
        bool isNsfw,
        Guid bookId,
        BookArchiveItem item,
        Guid parentEntityId,
        int chapterIndex,
        ISet<string> validChapterPaths,
        bool bestEffortHousekeeping,
        CancellationToken cancellationToken) {
        validChapterPaths.Add(item.ArchivePath);
        var chapterId = await books.UpsertBookChapterAsync(
            item.ArchivePath,
            item.ChapterTitle,
            parentEntityId,
            chapterIndex,
            item.PageMembers.Count,
            isNsfw || item.MarksNsfw,
            cancellationToken);

        var pageItems = new List<BookPageUpsertItem>(item.PageMembers.Count);
        for (var i = 0; i < item.PageMembers.Count; i++) {
            var memberPath = item.PageMembers[i];
            var pagePath = EntitySourcePath.ArchiveMember(item.ArchivePath, memberPath);
            var pageTitle = Path.GetFileNameWithoutExtension(memberPath);

            pageItems.Add(new BookPageUpsertItem(
                pagePath,
                pageTitle,
                bookId,
                chapterId,
                i,
                isNsfw || item.MarksNsfw));
        }

        var pageIds = await books.UpsertBookPagesBatchAsync(pageItems, cancellationToken);
        if (bestEffortHousekeeping) {
            await ImportedMaterializationHousekeeping.TryAsync(
                logger,
                "Imported book pages are ready but their thumbnail jobs could not be queued.",
                () => QueueBookPageJobsAsync(
                    context, settings, pageItems, pageIds, cancellationToken));
        } else {
            await QueueBookPageJobsAsync(
                context, settings, pageItems, pageIds, cancellationToken);
        }
    }

    private Task ReportArchiveProgressAsync(
        JobContext context,
        int processedArchiveCount,
        int archiveCount,
        bool bestEffortHousekeeping,
        CancellationToken cancellationToken) {
        if (archiveCount == 0 || processedArchiveCount % 10 != 0) {
            return Task.CompletedTask;
        }

        Task ReportAsync() => context.ReportProgressAsync(
            processedArchiveCount * 80 / archiveCount,
            $"Processing {processedArchiveCount}/{archiveCount}",
            cancellationToken);
        return bestEffortHousekeeping
            ? ImportedMaterializationHousekeeping.TryAsync(
                logger,
                "Imported book progress could not be reported.",
                ReportAsync)
            : ReportAsync();
    }

    private static List<string> ListImageMembersInZip(string archivePath) {
        try {
            using var archive = ZipFile.OpenRead(archivePath);
            return archive.Entries
                .Where(entry => !string.IsNullOrEmpty(entry.Name)
                    && ImageExtensions.Contains(Path.GetExtension(entry.Name)))
                .OrderBy(entry => entry.FullName, NaturalPathComparer.Instance)
                .Select(entry => entry.FullName)
                .ToList();
        } catch {
            return [];
        }
    }

    private static ComicInfoMetadata? BestBookMetadata(IEnumerable<BookArchiveItem> items) =>
        items.Select(item => item.Metadata)
            .FirstOrDefault(metadata => metadata is not null &&
                (!string.IsNullOrWhiteSpace(metadata.Series) ||
                    !string.IsNullOrWhiteSpace(metadata.Summary) ||
                    metadata.Tags.Count > 0 ||
                    metadata.Creators.Count > 0 ||
                    !string.IsNullOrWhiteSpace(metadata.Publisher)));

    private sealed record BookArchiveItem(
        string ArchivePath,
        string BookPath,
        string BookTitle,
        string? VolumePath,
        string? VolumeTitle,
        string ChapterTitle,
        IReadOnlyList<string> PageMembers,
        ComicInfoMetadata? Metadata,
        bool MarksNsfw) {
        public static BookArchiveItem From(
            string rootPath,
            string archivePath,
            IReadOnlyList<string> pageMembers,
            ComicInfoMetadata? metadata) {
            var relativePath = Path.GetRelativePath(rootPath, archivePath);
            var segments = relativePath
                .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            var fallbackTitle = Path.GetFileNameWithoutExtension(archivePath);
            var chapterTitle = string.IsNullOrWhiteSpace(metadata?.Title) ? fallbackTitle : metadata.Title.Trim();

            if (segments.Length <= 1) {
                var rootBookTitle = FirstNonEmpty(metadata?.Series, metadata?.Title, chapterTitle)!;
                return new BookArchiveItem(archivePath, archivePath, rootBookTitle, null, null, chapterTitle, pageMembers, metadata, metadata?.MarksNsfw == true);
            }

            var bookPath = Path.Combine(rootPath, segments[0]);
            var bookTitle = FirstNonEmpty(metadata?.Series, segments[0])!;
            if (segments.Length <= 2) {
                return new BookArchiveItem(archivePath, bookPath, bookTitle, null, null, chapterTitle, pageMembers, metadata, metadata?.MarksNsfw == true);
            }

            var volumePath = Path.GetDirectoryName(archivePath) ?? bookPath;
            var volumeTitle = Path.GetFileName(volumePath);
            return new BookArchiveItem(archivePath, bookPath, bookTitle, volumePath, volumeTitle, chapterTitle, pageMembers, metadata, metadata?.MarksNsfw == true);
        }
    }

    private sealed record SingleFileBookItem(
        string SourcePath,
        string Title,
        bool IsNsfw,
        BookFormat Format,
        ComicInfoMetadata? Metadata,
        string? AuthorPath,
        string? AuthorTitle) {
        public static SingleFileBookItem From(
            string rootPath,
            string sourcePath,
            string title,
            bool isNsfw,
            BookFormat format,
            ComicInfoMetadata? metadata) {
            var relativePath = Path.GetRelativePath(rootPath, sourcePath);
            var segments = relativePath
                .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length <= 1) {
                return new SingleFileBookItem(sourcePath, title, isNsfw, format, metadata, null, null);
            }

            // The top-level folder under the root groups a single-file book's author (e.g. Author/Title/book.epub),
            // mirroring Artist/Album for music. The display name prefers the embedded author (EPUB dc:creator /
            // PDF Author) so a series- or title-named folder (e.g. "Game of Thrones") still shows the real
            // author ("George R.R. Martin"); the folder name is the fallback when no creator metadata exists.
            var authorPath = Path.Combine(rootPath, segments[0]);
            var authorTitle = FirstNonEmpty(metadata?.Creators.Count > 0 ? metadata.Creators[0] : null) ?? segments[0];
            return new SingleFileBookItem(sourcePath, title, isNsfw, format, metadata, authorPath, authorTitle);
        }
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.Select(value => value?.Trim()).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private sealed class NaturalPathComparer : IComparer<string> {
        public static readonly NaturalPathComparer Instance = new();

        public int Compare(string? x, string? y) {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var ix = 0;
            var iy = 0;
            while (ix < x.Length && iy < y.Length) {
                if (char.IsDigit(x[ix]) && char.IsDigit(y[iy])) {
                    var numberCompare = CompareNumber(x, ref ix, y, ref iy);
                    if (numberCompare != 0) return numberCompare;
                    continue;
                }

                var charCompare = char.ToUpperInvariant(x[ix]).CompareTo(char.ToUpperInvariant(y[iy]));
                if (charCompare != 0) return charCompare;
                ix++;
                iy++;
            }

            return x.Length.CompareTo(y.Length);
        }

        private static int CompareNumber(string x, ref int ix, string y, ref int iy) {
            var startX = ix;
            var startY = iy;
            while (ix < x.Length && char.IsDigit(x[ix])) ix++;
            while (iy < y.Length && char.IsDigit(y[iy])) iy++;

            var spanX = x.AsSpan(startX, ix - startX).TrimStart('0');
            var spanY = y.AsSpan(startY, iy - startY).TrimStart('0');
            if (spanX.Length != spanY.Length) return spanX.Length.CompareTo(spanY.Length);

            var digitCompare = spanX.CompareTo(spanY, StringComparison.Ordinal);
            return digitCompare != 0 ? digitCompare : (ix - startX).CompareTo(iy - startY);
        }
    }
}
