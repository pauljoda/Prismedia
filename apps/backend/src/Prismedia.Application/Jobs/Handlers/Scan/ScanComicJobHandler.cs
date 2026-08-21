using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Entities;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Jobs.Scanning;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;

namespace Prismedia.Application.Jobs.Handlers.Scan;

/// <summary>
/// Discovers serialized-comic archives as released installments, groups them beneath comic series
/// and optional volumes, and atomically projects their pages into the generic reader manifest.
/// Individual pages remain resources of the installment rather than catalog Entities.
/// </summary>
[JobDefinition(JobType.ScanComic, SingletonBehavior = JobSingletonBehavior.QueueWideWhenUntargeted, BlocksAutoIdentify = true)]
public sealed class ScanComicJobHandler(
    ILogger<ScanComicJobHandler> logger,
    IFileDiscovery fileDiscovery,
    ILibraryScanRootPersistence roots,
    IComicScanPersistence comics,
    IEntityPageManifestStore pageManifests,
    IDownstreamNeedsPersistence downstreamNeeds,
    IScanSnapshotStore? snapshots = null,
    IComicInfoMetadataReader? comicInfoReader = null,
    IScanMetadataPersistence? scanMetadata = null,
    ILibraryFileChangeIntake? changeIntake = null)
    : ScanJobHandler(logger, fileDiscovery, roots, snapshots, changeIntake: changeIntake) {
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".jpg", ".jpeg", ".png", ".apng", ".gif", ".webp", ".avif", ".bmp", ".tiff", ".tif"
    };

    private static readonly Regex FirstInteger = new(@"\d+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <inheritdoc />
    protected override bool IsEligibleRoot(LibraryRootData root) => root.ScanBooks;

    /// <inheritdoc />
    protected override IReadOnlyList<MediaCategory> ScanCategories => [MediaCategory.ComicArchive];

    /// <inheritdoc />
    protected override async Task<ScanRootOutcome> ScanRootCoreAsync(
        JobContext context,
        LibraryRootData root,
        CancellationToken cancellationToken) {
        var excludedPaths = await Roots.GetExcludedPathsForRootAsync(root.Id, cancellationToken);
        var archivePaths = await FileDiscovery.DiscoverFilesAsync(
            root.Path,
            MediaCategory.ComicArchive,
            root.Recursive,
            excludedPaths,
            cancellationToken);
        logger.LogInformation(
            "ScanComic: found {Count} archives in {Label}",
            archivePaths.Count,
            root.Label);

        var settings = await Roots.GetSettingsAsync(cancellationToken);
        if (!root.AutoIdentify) {
            settings = settings with { AutoIdentifyEnabled = false };
        }

        var items = new List<ComicArchiveItem>();
        foreach (var archivePath in archivePaths.OrderBy(path => path, NaturalPathComparer.Instance)) {
            var members = ListImageMembers(archivePath);
            if (members.Count == 0) {
                logger.LogWarning("ScanComic: skipping archive with no safe readable pages: {Path}", archivePath);
                continue;
            }

            var metadata = comicInfoReader is null
                ? null
                : await comicInfoReader.ReadAsync(archivePath, cancellationToken);
            items.Add(ComicArchiveItem.From(root.Path, archivePath, members, metadata));
        }

        var validArchivePaths = items
            .Select(item => item.ArchivePath)
            .ToHashSet(FileSystemPathComparison.Comparer);
        var processed = 0;

        foreach (var seriesGroup in items
            .GroupBy(item => item.SeriesKey, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, NaturalPathComparer.Instance)) {
            var seriesFirst = seriesGroup.First();
            var seriesIsNsfw = root.IsNsfw || seriesGroup.Any(item => item.MarksNsfw);
            var seriesId = await comics.UpsertComicSeriesAsync(
                seriesFirst.SeriesFolderPath,
                seriesFirst.SeriesTitle,
                root.Id,
                seriesIsNsfw,
                cancellationToken);

            var seriesMetadata = BestSeriesMetadata(seriesGroup);
            if (seriesMetadata is not null && scanMetadata is not null) {
                await scanMetadata.ApplyComicInfoMetadataAsync(
                    seriesId,
                    SeriesFacts(seriesMetadata),
                    seriesIsNsfw,
                    cancellationToken);
            }

            var direct = seriesGroup
                .Where(item => item.VolumeKey is null)
                .OrderBy(item => item.ArchivePath, NaturalPathComparer.Instance)
                .ToArray();
            for (var index = 0; index < direct.Length; index++) {
                await MaterializeInstallmentAsync(
                    direct[index],
                    root,
                    seriesId,
                    index,
                    index + 1,
                    cancellationToken);
                processed++;
                await ReportProgressAsync(context, processed, items.Count, cancellationToken);
            }

            var volumeGroups = seriesGroup
                .Where(item => item.VolumeKey is not null)
                .GroupBy(item => item.VolumeKey!, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, NaturalPathComparer.Instance)
                .ToArray();
            for (var volumeIndex = 0; volumeIndex < volumeGroups.Length; volumeIndex++) {
                var volumeGroup = volumeGroups[volumeIndex];
                var volumeFirst = volumeGroup.First();
                var volumeNumber = volumeFirst.VolumeNumber ?? volumeIndex + 1;
                var volumeTitle = volumeFirst.VolumeTitle ?? $"Volume {volumeNumber}";
                var volumeId = await comics.UpsertComicVolumeAsync(
                    seriesId,
                    volumeTitle,
                    volumeNumber,
                    seriesIsNsfw || volumeGroup.Any(item => item.MarksNsfw),
                    cancellationToken);

                var installments = volumeGroup
                    .OrderBy(item => item.ArchivePath, NaturalPathComparer.Instance)
                    .ToArray();
                for (var index = 0; index < installments.Length; index++) {
                    await MaterializeInstallmentAsync(
                        installments[index],
                        root,
                        volumeId,
                        index,
                        index + 1,
                        cancellationToken);
                    processed++;
                    await ReportProgressAsync(context, processed, items.Count, cancellationToken);
                }
            }

            var identify = AutoIdentifyScanEnqueue.RequestFor(
                settings,
                EntityKind.ComicSeries,
                seriesId.ToString(),
                seriesFirst.SeriesTitle,
                await downstreamNeeds.IsEntityOrganizedAsync(seriesId, cancellationToken));
            if (identify is not null) {
                await context.EnqueueIfNeededAsync(identify, cancellationToken);
            }
        }

        await comics.RemoveStaleComicInstallmentsInRootAsync(
            root.Id,
            validArchivePaths,
            cancellationToken);
        await comics.RemoveEmptyComicContainersAsync(cancellationToken);
        await Roots.RemoveEntitiesInExcludedPathsAsync(root.Id, cancellationToken);
        return ScanRootOutcome.Success;
    }

    private async Task MaterializeInstallmentAsync(
        ComicArchiveItem item,
        LibraryRootData root,
        Guid parentEntityId,
        int sortOrder,
        int fallbackPosition,
        CancellationToken cancellationToken) {
        var position = item.Position ?? fallbackPosition;
        var label = item.PositionLabel ?? position.ToString(CultureInfo.InvariantCulture);
        var isNsfw = root.IsNsfw || item.MarksNsfw;
        var sizeBytes = TryGetFileSize(item.ArchivePath);
        var installmentId = await comics.UpsertComicInstallmentAsync(
            item.ArchivePath,
            item.InstallmentTitle,
            root.Id,
            parentEntityId,
            sortOrder,
            position,
            label,
            item.InstallmentKind,
            sizeBytes,
            isNsfw,
            cancellationToken);

        if (item.Metadata is not null && scanMetadata is not null) {
            await scanMetadata.ApplyComicInfoMetadataAsync(
                installmentId,
                item.Metadata,
                isNsfw,
                cancellationToken);
        }

        await pageManifests.ReplaceAsync(
            BuildManifest(installmentId, item, sizeBytes),
            cancellationToken);
    }

    private static EntityPageManifest BuildManifest(
        Guid installmentId,
        ComicArchiveItem item,
        long? sizeBytes) {
        var direction = ReadingDirection(item.Metadata);
        var defaultMode = direction == PageReadingDirection.TopToBottom
            ? ReaderMode.Webtoon
            : ReaderMode.Paged;
        var metadataByOrdinal = item.Metadata?.Pages.ToDictionary(page => page.ImageOrdinal) ?? [];
        var pages = item.PageMembers.Select((member, ordinal) => {
            metadataByOrdinal.TryGetValue(ordinal, out var pageMetadata);
            return new EntityPageEntry(
                ordinal,
                member,
                MimeTypeFor(member),
                pageMetadata?.Width,
                pageMetadata?.Height,
                pageMetadata?.PageType ?? (ordinal == 0 ? PageType.FrontCover : PageType.Story),
                pageMetadata?.IsDoublePage ?? false,
                checksum: null);
        }).ToArray();
        var coverOrdinal = pages
            .FirstOrDefault(page => page.PageType == PageType.FrontCover)
            ?.Ordinal ?? 0;
        return new EntityPageManifest(
            installmentId,
            direction,
            defaultMode,
            coverOrdinal,
            SourceSignature(item.ArchivePath, item.PageMembers, sizeBytes),
            pages);
    }

    private static PageReadingDirection ReadingDirection(ComicInfoMetadata? metadata) {
        var manga = metadata?.Manga?.Trim();
        var format = metadata?.Format?.Trim();
        // prism-vocab: external ComicInfo.xml Manga and Format values are decoded at this boundary.
        if (ContainsAny(format, "webtoon", "long strip", "long-strip")) {
            return PageReadingDirection.TopToBottom;
        }
        if (ContainsAny(manga, "righttoleft", "right-to-left", "right to left")) {
            return PageReadingDirection.RightToLeft;
        }
        return PageReadingDirection.LeftToRight;
    }

    private static ComicInstallmentKind InstallmentKindFor(ComicInfoMetadata? metadata, string title) {
        var format = metadata?.Format;
        // prism-vocab: external ComicInfo.xml Format and Manga values are decoded at this boundary.
        if (ContainsAny(format, "one-shot", "one shot", "oneshot") ||
            ContainsAny(title, "one-shot", "one shot", "oneshot")) {
            return ComicInstallmentKind.OneShot;
        }
        if (ContainsAny(format, "special", "annual") || ContainsAny(title, "special", "annual")) {
            return ComicInstallmentKind.Special;
        }
        if (!string.IsNullOrWhiteSpace(metadata?.Manga) ||
            ContainsAny(format, "manga", "webtoon", "chapter")) {
            return ComicInstallmentKind.Chapter;
        }
        return ComicInstallmentKind.Issue;
    }

    private static bool ContainsAny(string? value, params string[] candidates) =>
        !string.IsNullOrWhiteSpace(value) && candidates.Any(candidate =>
            value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> ListImageMembers(string archivePath) {
        try {
            using var archive = ZipFile.OpenRead(archivePath);
            return archive.Entries
                .Where(entry =>
                    !string.IsNullOrEmpty(entry.Name) &&
                    ImageExtensions.Contains(Path.GetExtension(entry.Name)) &&
                    IsSafeMember(entry.FullName))
                .Select(entry => entry.FullName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(member => member, NaturalPathComparer.Instance)
                .ToArray();
        } catch (InvalidDataException) {
            return [];
        } catch (IOException) {
            return [];
        } catch (UnauthorizedAccessException) {
            return [];
        }
    }

    private static bool IsSafeMember(string member) {
        if (string.IsNullOrWhiteSpace(member) || member[0] is '/' or '\\' || member[^1] is '/' or '\\') {
            return false;
        }
        var drivePrefix = member.Length >= 2 && char.IsLetter(member[0]) && member[1] == ':';
        return !drivePrefix &&
            !member.Contains('\0') &&
            member.Split(['/', '\\'], StringSplitOptions.None)
                .All(segment => segment.Length > 0 && segment is not "." and not "..");
    }

    private static string MimeTypeFor(string member) =>
        Path.GetExtension(member).ToLowerInvariant() switch {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".apng" => "image/apng",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".avif" => "image/avif",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            _ => throw new InvalidOperationException("The comic page extension is not supported.")
        };

    private static string SourceSignature(
        string archivePath,
        IReadOnlyList<string> members,
        long? sizeBytes) {
        var modified = File.Exists(archivePath)
            ? File.GetLastWriteTimeUtc(archivePath).Ticks
            : 0;
        var input = $"{sizeBytes ?? -1}:{modified}:{string.Join('\n', members)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }

    private static long? TryGetFileSize(string path) {
        try {
            return new FileInfo(path).Length;
        } catch (IOException) {
            return null;
        } catch (UnauthorizedAccessException) {
            return null;
        }
    }

    private static ComicInfoMetadata? BestSeriesMetadata(IEnumerable<ComicArchiveItem> items) =>
        items.Select(item => item.Metadata).FirstOrDefault(metadata => metadata is not null &&
            (!string.IsNullOrWhiteSpace(metadata.Publisher) ||
             metadata.Creators.Count > 0 ||
             metadata.Tags.Count > 0 ||
             metadata.Urls.Count > 0));

    private static ComicInfoMetadata SeriesFacts(ComicInfoMetadata metadata) => metadata with {
        Title = null,
        Number = null,
        Volume = null,
        Summary = null,
        Date = null,
        PageCount = null
    };

    private static Task ReportProgressAsync(
        JobContext context,
        int processed,
        int total,
        CancellationToken cancellationToken) =>
        total == 0 || processed % 10 != 0
            ? Task.CompletedTask
            : context.ReportProgressAsync(
                processed * 80 / total,
                $"Processing {processed}/{total}",
                cancellationToken);

    private sealed record ComicArchiveItem(
        string ArchivePath,
        string SeriesKey,
        string? SeriesFolderPath,
        string SeriesTitle,
        string? VolumeKey,
        int? VolumeNumber,
        string? VolumeTitle,
        string InstallmentTitle,
        int? Position,
        string? PositionLabel,
        ComicInstallmentKind InstallmentKind,
        IReadOnlyList<string> PageMembers,
        ComicInfoMetadata? Metadata,
        bool MarksNsfw) {
        public static ComicArchiveItem From(
            string rootPath,
            string archivePath,
            IReadOnlyList<string> pageMembers,
            ComicInfoMetadata? metadata) {
            var relativePath = Path.GetRelativePath(rootPath, archivePath);
            var segments = relativePath.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
            var fallbackTitle = Path.GetFileNameWithoutExtension(archivePath);
            var installmentTitle = FirstNonEmpty(metadata?.Title, fallbackTitle)!;
            var seriesFolderPath = segments.Length > 1
                ? Path.Combine(rootPath, segments[0])
                : null;
            var seriesTitle = FirstNonEmpty(
                metadata?.Series,
                seriesFolderPath is null ? null : segments[0],
                installmentTitle)!;
            var seriesKey = seriesFolderPath ?? $"title:{seriesTitle}";

            var volumeFolderPath = segments.Length > 2
                ? Path.GetDirectoryName(archivePath)
                : null;
            var volumeFolderTitle = volumeFolderPath is null
                ? null
                : Path.GetFileName(volumeFolderPath);
            var volumeNumber = metadata?.Volume is >= 0
                ? metadata.Volume
                : ParseFirstInteger(volumeFolderTitle);
            var volumeKey = volumeNumber is not null
                ? $"number:{volumeNumber.Value}"
                : volumeFolderPath is not null
                    ? $"path:{volumeFolderPath}"
                    : null;
            var volumeTitle = metadata?.Volume is >= 0
                ? $"Volume {metadata.Volume.Value}"
                : volumeFolderTitle;
            var positionLabel = FirstNonEmpty(metadata?.Number);

            return new ComicArchiveItem(
                archivePath,
                seriesKey,
                seriesFolderPath,
                seriesTitle,
                volumeKey,
                volumeNumber,
                volumeTitle,
                installmentTitle,
                ParseFirstInteger(positionLabel),
                positionLabel,
                InstallmentKindFor(metadata, installmentTitle),
                pageMembers,
                metadata,
                metadata?.MarksNsfw == true);
        }
    }

    private static int? ParseFirstInteger(string? value) {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var match = FirstInteger.Match(value);
        return match.Success && int.TryParse(
            match.Value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.Select(value => value?.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

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
                var characterCompare = char.ToUpperInvariant(x[ix])
                    .CompareTo(char.ToUpperInvariant(y[iy]));
                if (characterCompare != 0) return characterCompare;
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
            var digitsX = x.AsSpan(startX, ix - startX).TrimStart('0');
            var digitsY = y.AsSpan(startY, iy - startY).TrimStart('0');
            if (digitsX.Length != digitsY.Length) return digitsX.Length.CompareTo(digitsY.Length);
            var numeric = digitsX.SequenceCompareTo(digitsY);
            return numeric != 0 ? numeric : (ix - startX).CompareTo(iy - startY);
        }
    }
}
