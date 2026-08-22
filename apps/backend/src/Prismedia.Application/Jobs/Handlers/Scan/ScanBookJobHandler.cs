using Prismedia.Application.Jobs.Handlers;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Books;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Jobs.Scanning;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Scan;

/// <summary>
/// Discovers prose books and audiobook sources. Serialized comics use the independent
/// <see cref="ScanComicJobHandler"/> so released installments never become prose chapters.
/// </summary>
[JobDefinition(JobType.ScanBook, SingletonBehavior = JobSingletonBehavior.QueueWideWhenUntargeted, BlocksAutoIdentify = true)]
public sealed class ScanBookJobHandler(
    ILogger<ScanBookJobHandler> logger,
    IFileDiscovery fileDiscovery,
    ILibraryScanRootPersistence roots,
    IBookScanPersistence books,
    IDownstreamNeedsPersistence downstreamNeeds,
    IScanSnapshotStore? snapshots = null,
    IScanMetadataPersistence? scanMetadata = null,
    IBookFileMetadataReader? bookFileMetadata = null,
    Acquisition.IAcquisitionHintApplier? acquisitionHints = null,
    IAudioScanPersistence? audio = null,
    ILibraryFileChangeIntake? changeIntake = null,
    IBookChapterMapService? chapterMap = null) : ScanJobHandler(logger, fileDiscovery, roots, snapshots, changeIntake: changeIntake) {
    protected override bool IsEligibleRoot(LibraryRootData root) => root.ScanBooks;

    protected override IReadOnlyList<MediaCategory> ScanCategories =>
        [MediaCategory.Book, MediaCategory.Audiobook];

    protected override async Task<ScanRootOutcome> ScanRootCoreAsync(JobContext context, LibraryRootData root, CancellationToken cancellationToken) {
        logger.LogInformation("ScanBook: discovering prose and audiobook sources in {Path}", root.Path);
        var excludedPaths = await Roots.GetExcludedPathsForRootAsync(root.Id, cancellationToken);

        var bookFiles = await FileDiscovery.DiscoverFilesAsync(
            root.Path, MediaCategory.Book, root.Recursive, excludedPaths, cancellationToken);
        var audiobookFiles = await FileDiscovery.DiscoverFilesAsync(
            root.Path, MediaCategory.Audiobook, root.Recursive, excludedPaths, cancellationToken);

        return await MaterializeBookPathsAsync(
            context,
            root,
            bookFiles,
            audiobookFiles,
            reconcile: true,
            reconcileAudiobookTracks: false,
            acquisitionId: null,
            acquisitionTargetBookId: null,
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
        CancellationToken cancellationToken,
        IReadOnlyList<string>? removedSourcePaths = null) {
        if (!root.Enabled || !root.ScanBooks) {
            throw new InvalidOperationException("The imported books no longer belong to an enabled book library root.");
        }

        var bookFiles = placedPaths.Where(path => BookFormatFor(path) is not null).ToArray();
        var audiobookFiles = placedPaths.Where(IsAudiobookPath).ToArray();
        if (bookFiles.Length + audiobookFiles.Length != placedPaths.Count) {
            throw new InvalidOperationException("The book import contains a file the book scanner does not support.");
        }
        if (removedSourcePaths?.Any(path => !IsAudiobookPath(path)) == true) {
            throw new InvalidOperationException("A narrow book import can retire only audiobook source files.");
        }

        var acquisitionTargetBookId = acquisitionHints is null
            ? null
            : await acquisitionHints.ResolveTargetEntityIdAsync(
                EntityKind.Book, acquisitionId, cancellationToken);
        await MaterializeBookPathsAsync(
            context,
            root,
            bookFiles,
            audiobookFiles,
            reconcile: false,
            reconcileAudiobookTracks: removedSourcePaths is { Count: > 0 },
            acquisitionId,
            acquisitionTargetBookId,
            bestEffortHousekeeping: true,
            cancellationToken);
    }

    private async Task<ScanRootOutcome> MaterializeBookPathsAsync(
        JobContext context,
        LibraryRootData root,
        IReadOnlyList<string> bookFiles,
        IReadOnlyList<string> audiobookFiles,
        bool reconcile,
        bool reconcileAudiobookTracks,
        Guid? acquisitionId,
        Guid? acquisitionTargetBookId,
        bool bestEffortHousekeeping,
        CancellationToken cancellationToken) {

        var settings = await Roots.GetSettingsAsync(cancellationToken);
        if (!root.AutoIdentify) {
            // Honor this root's Auto Identify opt-out without touching other generation settings.
            settings = settings with { AutoIdentifyEnabled = false };
        }
        var validBookPaths = new HashSet<string>(FileSystemPathComparison.Comparer);

        var readableBooksByDirectory = await ScanSingleFileBooksAsync(
            context,
            root,
            settings,
            bookFiles,
            validBookPaths,
            acquisitionId,
            bestEffortHousekeeping,
            cancellationToken);

        await ScanAudiobooksAsync(
            context,
            root,
            settings,
            audiobookFiles,
            readableBooksByDirectory,
            validBookPaths,
            reconcile || reconcileAudiobookTracks,
            acquisitionId,
            acquisitionTargetBookId,
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
    private async Task<IReadOnlyList<ReadableBookSource>> ScanSingleFileBooksAsync(
        JobContext context,
        LibraryRootData root,
        LibrarySettingsData settings,
        IReadOnlyList<string> bookFiles,
        ISet<string> validBookPaths,
        Guid? acquisitionId,
        bool bestEffortHousekeeping,
        CancellationToken cancellationToken) {
        if (bookFiles.Count == 0) {
            return [];
        }

        var readableBooks = new List<ReadableBookSource>();

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
            var bookId = await UpsertSingleFileBookAsync(
                context,
                settings,
                root,
                looseItem,
                validBookPaths,
                parentEntityId: null,
                sortOrder: null,
                acquisitionId,
                bestEffortHousekeeping,
                cancellationToken);
            readableBooks.Add(new ReadableBookSource(looseItem.SourcePath, bookId));
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
                await acquisitionHints.BindWantedParentFolderAsync(
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
                var bookId = await UpsertSingleFileBookAsync(
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
                readableBooks.Add(new ReadableBookSource(booksByAuthor[index].SourcePath, bookId));
            }
        }

        return readableBooks;
    }

    private async Task<Guid> UpsertSingleFileBookAsync(
        JobContext context,
        LibrarySettingsData settings,
        LibraryRootData root,
        SingleFileBookItem item,
        ISet<string> validBookPaths,
        Guid? parentEntityId,
        int? sortOrder,
        Guid? acquisitionId,
        bool bestEffortHousekeeping,
        CancellationToken cancellationToken) {
        // Bind a request-created wanted entity to this path first, so the path-keyed upsert finds it
        // (attaching the imported file to the wanted entity) instead of creating a duplicate.
        if (acquisitionHints is not null) {
            await acquisitionHints.BindWantedFileAsync(
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
            parentEntityId,
            sortOrder,
            cancellationToken);
        validBookPaths.Add(item.SourcePath);

        // Stamp acquisition-supplied identity before auto-identify so it resolves ID-first.
        if (acquisitionHints is not null) {
            await acquisitionHints.ApplyAsync(bookId, item.SourcePath, cancellationToken);
        }

        if (item.Metadata is not null && scanMetadata is not null) {
            await scanMetadata.ApplyBookFileMetadataAsync(bookId, item.Metadata, item.IsNsfw, cancellationToken);
        }

        if (!bestEffortHousekeeping) {
            await QueueSingleFileBookJobsAsync(
                context, settings, bookId, item.Title, cancellationToken);
        }

        return bookId;
    }

    private async Task ScanAudiobooksAsync(
        JobContext context,
        LibraryRootData root,
        LibrarySettingsData settings,
        IReadOnlyList<string> audiobookFiles,
        IReadOnlyList<ReadableBookSource> readableBooks,
        ISet<string> validBookPaths,
        bool reconcile,
        Guid? acquisitionId,
        Guid? acquisitionTargetBookId,
        bool bestEffortHousekeeping,
        CancellationToken cancellationToken) {
        if (audio is null) {
            return;
        }

        var groupPaths = audiobookFiles
            .Where(IsAudiobookPath)
            .Select(path => AudiobookGroupKey(root.Path, path))
            .Distinct(FileSystemPathComparison.Comparer)
            .ToArray();
        var importedOwners = acquisitionHints is null
            ? []
            : await acquisitionHints.ResolveImportedBookOwnersAsync(groupPaths, cancellationToken);
        var importedOwnerByPath = importedOwners.ToDictionary(
            owner => owner.SourcePath,
            owner => owner.BookEntityId,
            FileSystemPathComparison.Comparer);
        var readableBookIds = readableBooks.Select(book => book.EntityId).ToHashSet();
        var validAudioPathsByBook = new Dictionary<Guid, HashSet<string>>();
        var groups = audiobookFiles
            .Where(IsAudiobookPath)
            .OrderBy(path => path, NaturalPathComparer.Instance)
            .Select(path => new {
                Path = path,
                BookId = acquisitionTargetBookId
                    ?? (importedOwnerByPath.TryGetValue(
                        AudiobookGroupKey(root.Path, path), out var importedBookId)
                            ? importedBookId
                            : (Guid?)null)
                    ?? ResolveReadableBookId(path, readableBooks)
            })
            .GroupBy(
                item => item.BookId is { } bookId
                    ? $"book:{bookId}"
                    : $"path:{AudiobookGroupKey(root.Path, item.Path)}",
                StringComparer.Ordinal);
        foreach (var group in groups.OrderBy(group => group.Key, NaturalPathComparer.Instance)) {
            var first = group.First();
            var hasExistingBook = first.BookId is { };
            var bookId = first.BookId ?? Guid.Empty;
            var hasReadableBook = first.BookId is { } existingBookId && readableBookIds.Contains(existingBookId);
            var sourcePath = AudiobookGroupKey(root.Path, first.Path);
            var title = hasExistingBook
                ? string.Empty
                : AudiobookTitle(root.Path, sourcePath, first.Path);

            if (!hasExistingBook) {
                if (acquisitionHints is not null) {
                    await acquisitionHints.BindWantedFolderAsync(
                        EntityKind.Book, sourcePath, cancellationToken, acquisitionId);
                }
                bookId = await books.UpsertAudiobookBookAsync(
                    sourcePath,
                    title,
                    root.Id,
                    root.IsNsfw,
                    BookType.Novel,
                    BookFormat.Audio,
                    cancellationToken);
                validBookPaths.Add(sourcePath);
                if (!bestEffortHousekeeping) {
                    await QueueBookAutoIdentifyAsync(context, settings, bookId, title, cancellationToken);
                }
            }

            var tracks = group
                .Select(item => item.Path)
                .OrderBy(path => path, NaturalPathComparer.Instance)
                .Select((path, index) => new AudioTrackUpsertItem(
                    path,
                    Path.GetFileNameWithoutExtension(path),
                    root.Id,
                    bookId,
                    index,
                    SectionLabel: null,
                    SectionOrder: 0,
                    root.IsNsfw))
                .ToArray();
            var trackIds = await audio.UpsertAudioTracksBatchAsync(tracks, cancellationToken);
            if (acquisitionId is not null && acquisitionHints is not null) {
                await acquisitionHints.ApplyAsync(bookId, sourcePath, cancellationToken);
            }
            validAudioPathsByBook[bookId] = tracks
                .Select(track => track.FilePath)
                .ToHashSet(FileSystemPathComparison.Comparer);
            for (var index = 0; index < trackIds.Count && index < tracks.Length; index++) {
                if (!bestEffortHousekeeping) {
                    await QueueAudiobookTrackJobsAsync(
                        context, settings, trackIds[index], tracks[index].Title, cancellationToken);
                }
            }

            if (!bestEffortHousekeeping) {
                await QueueChapterMapAsync(
                    context, bookId, hasExistingBook ? null : title, cancellationToken);
            }

            if (reconcile && !hasReadableBook) {
                await audio.RemoveStaleAudioTracksInLibraryAsync(
                    bookId,
                    validAudioPathsByBook[bookId],
                    cancellationToken);
            }
        }

        if (reconcile) {
            foreach (var readableBook in readableBooks) {
                await audio.RemoveStaleAudioTracksInLibraryAsync(
                    readableBook.EntityId,
                    validAudioPathsByBook.GetValueOrDefault(readableBook.EntityId) ??
                        new HashSet<string>(FileSystemPathComparison.Comparer),
                    cancellationToken);
                // A removed track frees its chapter, and a replaced EPUB rewrites chapter keys —
                // both invalidate the signature the map service checks here.
                if (!bestEffortHousekeeping) {
                    await QueueChapterMapAsync(context, readableBook.EntityId, null, cancellationToken);
                }
            }
        }
    }

    private async Task QueueAudiobookTrackJobsAsync(
        JobContext context,
        LibrarySettingsData settings,
        Guid trackId,
        string title,
        CancellationToken cancellationToken) {
        var needs = await downstreamNeeds.CheckDownstreamNeedsBatchAsync([trackId], cancellationToken);
        if (!needs.TryGetValue(trackId, out var trackNeeds)) {
            return;
        }

        foreach (var request in EntityProcessingPlanRequests.ForEntity(
                     EntityKind.AudioTrack,
                     trackId,
                     title,
                     settings,
                     trackNeeds,
                     deferPreviewUntilProbeCompletes: true)) {
            await context.EnqueueIfNeededAsync(request, cancellationToken);
        }
    }

    private static string AudiobookGroupKey(string rootPath, string sourcePath) {
        var directory = Path.GetDirectoryName(sourcePath) ?? rootPath;
        return FileSystemPathComparison.Equals(directory, rootPath) ? sourcePath : directory;
    }

    private static Guid? ResolveReadableBookId(
        string audiobookPath,
        IReadOnlyList<ReadableBookSource> readableBooks) {
        var directory = Path.GetDirectoryName(audiobookPath) ?? string.Empty;
        var candidates = readableBooks
            .Where(book => FileSystemPathComparison.Equals(
                Path.GetDirectoryName(book.SourcePath) ?? string.Empty,
                directory))
            .ToArray();
        var audiobookStem = Path.GetFileNameWithoutExtension(audiobookPath);
        var exact = candidates.FirstOrDefault(book => string.Equals(
            Path.GetFileNameWithoutExtension(book.SourcePath),
            audiobookStem,
            StringComparison.OrdinalIgnoreCase));
        return exact is not null ? exact.EntityId : candidates.Length == 1 ? candidates[0].EntityId : null;
    }

    private static string AudiobookTitle(string rootPath, string groupKey, string firstSourcePath) =>
        FileSystemPathComparison.Equals(Path.GetDirectoryName(firstSourcePath) ?? rootPath, rootPath)
            ? Path.GetFileNameWithoutExtension(firstSourcePath)
            : Path.GetFileName(groupKey);

    /// <summary>
    /// Catalog-only chapter-map drift repair for unchanged roots: retitled tracks, first-deploy
    /// backfill, and manual data edits change no files, so the snapshot fast path skips the
    /// detailed scan and the per-book enqueue hooks never run. Stale signatures are proven drift,
    /// so only those books get a refresh job — an up-to-date root enqueues nothing.
    /// </summary>
    protected override async Task OnUnchangedIntegrityScanAsync(
        JobContext context,
        LibraryRootData root,
        CancellationToken cancellationToken) {
        if (chapterMap is null) {
            return;
        }

        foreach (var stale in await chapterMap.ListStaleForRootAsync(root.Path, cancellationToken)) {
            await context.EnqueueIfNeededAsync(
                EnqueueJobRequest.ForEntity(
                    JobType.MapBookChapters,
                    EntityKind.Book,
                    stale.BookId.ToString(),
                    stale.Title),
                cancellationToken);
        }
    }

    /// <summary>
    /// Enqueues the chapter-map refresh only when its persisted signatures are stale, so routine
    /// verification scans of unchanged books enqueue nothing. The job re-projects the EPUB table
    /// of contents and recomputes the automatic audiobook chapter map.
    /// </summary>
    private async Task QueueChapterMapAsync(
        JobContext context,
        Guid bookId,
        string? title,
        CancellationToken cancellationToken) {
        if (chapterMap is null || !await chapterMap.IsRefreshNeededAsync(bookId, cancellationToken)) {
            return;
        }

        await context.EnqueueIfNeededAsync(
            EnqueueJobRequest.ForEntity(
                JobType.MapBookChapters,
                EntityKind.Book,
                bookId.ToString(),
                title),
            cancellationToken);
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
                    title),
                cancellationToken);
        }

        await QueueBookAutoIdentifyAsync(context, settings, bookId, title, cancellationToken);
        await QueueChapterMapAsync(context, bookId, title, cancellationToken);
    }

    private static BookFormat? BookFormatFor(string sourcePath) =>
        Path.GetExtension(sourcePath).ToLowerInvariant() switch {
            ".epub" => BookFormat.Epub,
            ".pdf" => BookFormat.Pdf,
            _ => null
        };

    private static bool IsAudiobookPath(string sourcePath) =>
        Path.GetExtension(sourcePath) is var extension &&
        (extension.Equals(".m4b", StringComparison.OrdinalIgnoreCase) ||
         extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase) ||
         extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase));

    private static BookType DefaultBookTypeFor(BookFormat format) =>
        format == BookFormat.Epub ? BookType.Novel : BookType.Book;

    private static string ContentTypeFor(BookFormat format) =>
        format == BookFormat.Pdf
            ? Prismedia.Contracts.Media.MediaContentTypes.Pdf
            : Prismedia.Contracts.Media.MediaContentTypes.Epub;

    private sealed record SingleFileBookItem(
        string SourcePath,
        string Title,
        bool IsNsfw,
        BookFormat Format,
        BookFileMetadata? Metadata,
        string? AuthorPath,
        string? AuthorTitle) {
        public static SingleFileBookItem From(
            string rootPath,
            string sourcePath,
            string title,
            bool isNsfw,
            BookFormat format,
            BookFileMetadata? metadata) {
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

    private sealed record ReadableBookSource(string SourcePath, Guid EntityId);

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
