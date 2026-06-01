using Prismedia.Application.Jobs.Handlers;
using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
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
    IComicInfoMetadataReader? comicInfoReader = null,
    IScanMetadataPersistence? scanMetadata = null,
    IBookFileMetadataReader? bookFileMetadata = null) : ScanJobHandler(logger, fileDiscovery, roots) {
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tiff", ".tif"
    };

    public override JobType Type => JobType.ScanBook;

    protected override bool IsEligibleRoot(LibraryRootData root) => root.ScanBooks;

    protected override async Task ScanRootAsync(JobContext context, LibraryRootData root, CancellationToken cancellationToken) {
        logger.LogInformation("ScanBook: discovering archives in {Path}", root.Path);
        var excludedPaths = await Roots.GetExcludedPathsForRootAsync(root.Id, cancellationToken);

        var archiveFiles = await FileDiscovery.DiscoverFilesAsync(
            root.Path, MediaCategory.ComicArchive, root.Recursive, excludedPaths, cancellationToken);

        logger.LogInformation("ScanBook: found {Count} archive files in {Label}", archiveFiles.Count, root.Label);

        var settings = await Roots.GetSettingsAsync(cancellationToken);
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

        var validBookPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processedArchiveCount = 0;

        foreach (var bookGroup in archiveItems
            .GroupBy(item => item.BookPath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, NaturalPathComparer.Instance)) {
            var first = bookGroup.First();
            var bookMetadata = BestBookMetadata(bookGroup);
            var bookIsNsfw = root.IsNsfw || bookGroup.Any(item => item.MarksNsfw);
            var bookId = await books.UpsertBookAsync(first.BookPath, first.BookTitle, root.Id, bookIsNsfw, cancellationToken);
            if (bookMetadata is not null && scanMetadata is not null) {
                await scanMetadata.ApplyComicInfoMetadataAsync(
                    bookId,
                    bookMetadata,
                    bookIsNsfw,
                    cancellationToken);
            }
            validBookPaths.Add(first.BookPath);

            // A book is the top-level root of its volumes/chapters/pages, so identify it directly.
            var bookAutoIdentify = AutoIdentifyScanEnqueue.RequestFor(
                settings, "book", bookId.ToString(), first.BookTitle);
            if (bookAutoIdentify is not null)
                await context.EnqueueIfNeededAsync(bookAutoIdentify, cancellationToken);

            var directChapterPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                    cancellationToken);
                processedArchiveCount++;
                await ReportArchiveProgressAsync(context, processedArchiveCount, archiveItems.Count, cancellationToken);
            }

            var validVolumePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var volumeGroups = bookGroup
                .Where(item => item.VolumePath is not null)
                .GroupBy(item => item.VolumePath!, StringComparer.OrdinalIgnoreCase)
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

                var validChapterPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                        cancellationToken);
                    processedArchiveCount++;
                    await ReportArchiveProgressAsync(context, processedArchiveCount, archiveItems.Count, cancellationToken);
                }

                await books.RemoveStaleBookChaptersAsync(volumeId, validChapterPaths, cancellationToken);
            }

            await books.RemoveStaleBookChaptersAsync(bookId, directChapterPaths, cancellationToken);
            await books.RemoveStaleBookVolumesAsync(bookId, validVolumePaths, cancellationToken);
        }

        await ScanSingleFileBooksAsync(context, root, settings, excludedPaths, validBookPaths, cancellationToken);

        await books.RemoveStaleBooksInRootAsync(root.Id, validBookPaths, cancellationToken);
        await Roots.RemoveEntitiesInExcludedPathsAsync(root.Id, cancellationToken);
    }

    /// <summary>
    /// Discovers single-file books (EPUB/PDF) in the root and upserts one book entity per file
    /// with no chapter/page entities. Records their source paths so stale cleanup keeps them.
    /// </summary>
    private async Task ScanSingleFileBooksAsync(
        JobContext context,
        LibraryRootData root,
        LibrarySettingsData settings,
        IReadOnlySet<string> excludedPaths,
        ISet<string> validBookPaths,
        CancellationToken cancellationToken) {
        var bookFiles = await FileDiscovery.DiscoverFilesAsync(
            root.Path, MediaCategory.Book, root.Recursive, excludedPaths, cancellationToken);
        if (bookFiles.Count == 0) {
            return;
        }

        logger.LogInformation("ScanBook: found {Count} single-file books in {Label}", bookFiles.Count, root.Label);

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

            var bookId = await books.UpsertSingleFileBookAsync(
                sourcePath,
                title,
                root.Id,
                isNsfw,
                DefaultBookTypeFor(format.Value),
                format.Value,
                ContentTypeFor(format.Value),
                cancellationToken);
            validBookPaths.Add(sourcePath);

            if (metadata is not null && scanMetadata is not null) {
                await scanMetadata.ApplyComicInfoMetadataAsync(bookId, metadata, isNsfw, cancellationToken);
            }

            if (settings.AutoGeneratePreview &&
                !await downstreamNeeds.HasEntityFileAsync(bookId, EntityFileRole.Thumbnail, cancellationToken)) {
                await context.EnqueueIfNeededAsync(new EnqueueJobRequest(
                    JobType.GenerateBookCoverThumbnail, TargetEntityKind: "book",
                    TargetEntityId: bookId.ToString(), TargetLabel: title), cancellationToken);
            }

            var autoIdentify = AutoIdentifyScanEnqueue.RequestFor(settings, "book", bookId.ToString(), title);
            if (autoIdentify is not null) {
                await context.EnqueueIfNeededAsync(autoIdentify, cancellationToken);
            }
        }
    }

    private static BookFormat? BookFormatFor(string sourcePath) =>
        Path.GetExtension(sourcePath).ToLowerInvariant() switch {
            ".epub" => BookFormat.Epub,
            ".pdf" => BookFormat.Pdf,
            _ => null
        };

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

        for (var i = 0; i < item.PageMembers.Count; i++) {
            var memberPath = item.PageMembers[i];
            var pagePath = $"{item.ArchivePath}::{memberPath}";
            var pageTitle = Path.GetFileNameWithoutExtension(memberPath);

            var pageId = await books.UpsertBookPageAsync(pagePath, pageTitle, bookId, chapterId, i, isNsfw || item.MarksNsfw, cancellationToken);

            if (settings.AutoGeneratePreview && !await downstreamNeeds.HasEntityFileAsync(pageId, EntityFileRole.Thumbnail, cancellationToken)) {
                await context.EnqueueIfNeededAsync(new EnqueueJobRequest(
                    JobType.GenerateBookPageThumbnail, TargetEntityKind: "book-page",
                    TargetEntityId: pageId.ToString(), TargetLabel: pageTitle), cancellationToken);
            }
        }
    }

    private static Task ReportArchiveProgressAsync(
        JobContext context,
        int processedArchiveCount,
        int archiveCount,
        CancellationToken cancellationToken) {
        if (archiveCount == 0 || processedArchiveCount % 10 != 0) {
            return Task.CompletedTask;
        }

        return context.ReportProgressAsync(processedArchiveCount * 80 / archiveCount,
            $"Processing {processedArchiveCount}/{archiveCount}", cancellationToken);
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
