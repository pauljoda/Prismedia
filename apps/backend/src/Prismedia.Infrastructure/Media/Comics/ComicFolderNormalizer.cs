using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Infrastructure.Files;
using SkiaSharp;

namespace Prismedia.Infrastructure.Media.Comics;

/// <summary>
/// Converts explicitly bounded loose-page comic folders into root-scoped managed CBZ files. The
/// source tree is read-only: originals remain the recovery source and are never moved or deleted.
/// </summary>
public sealed class ComicFolderNormalizer(
    ManagedGeneratedSourceRoot generatedSources,
    ILogger<ComicFolderNormalizer> logger) : IComicFolderNormalizer {
    private const int ValidationDecodeMaxDimension = 4096;
    private const string SignatureSuffix = ".source.sha256";
    private const string StagingPrefix = ".staging-";
    private static readonly DateTimeOffset StableZipTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly HashSet<string> IgnoredFileNames = new(StringComparer.OrdinalIgnoreCase) {
        ".DS_Store", "Thumbs.db", "desktop.ini"
    };

    private readonly string _managedRoot = generatedSources.AreaPath(ManagedGeneratedSourceRoot.ComicsArea);

    /// <inheritdoc />
    public async Task<ComicFolderNormalizationBatch> NormalizeAsync(
        LibraryRootData root,
        IReadOnlySet<string> excludedPaths,
        CancellationToken cancellationToken) {
        var archives = new List<NormalizedComicArchive>();
        var failedPaths = new HashSet<string>(FileSystemPathComparison.Comparer);
        if (!Directory.Exists(root.Path)) {
            return new ComicFolderNormalizationBatch(archives, failedPaths);
        }

        foreach (var directory in DiscoverDirectories(root, excludedPaths, failedPaths, cancellationToken)) {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = InspectCandidate(root, directory, excludedPaths);
            if (candidate is null) {
                continue;
            }
            if (candidate.RejectionReason is not null) {
                logger.LogWarning(
                    "ScanComic: rejecting loose-page boundary {Path}: {Reason}",
                    directory,
                    candidate.RejectionReason);
                failedPaths.UnionWith(candidate.InputPaths);
                continue;
            }

            try {
                var archive = await NormalizeCandidateAsync(root.Id, candidate, cancellationToken);
                archives.Add(archive);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or XmlException) {
                logger.LogWarning(ex, "ScanComic: failed to normalize loose pages at {Path}", directory);
                failedPaths.UnionWith(candidate.InputPaths);
            }
        }

        return new ComicFolderNormalizationBatch(archives, failedPaths);
    }

    /// <inheritdoc />
    public Task PruneAsync(
        Guid rootId,
        IReadOnlySet<string> retainedArchivePaths,
        CancellationToken cancellationToken) {
        var rootDirectory = ManagedDirectory(rootId);
        if (!Directory.Exists(rootDirectory)) {
            return Task.CompletedTask;
        }

        var retained = retainedArchivePaths
            .Select(Path.GetFullPath)
            .ToHashSet(FileSystemPathComparison.Comparer);
        foreach (var archivePath in Directory.GetFiles(rootDirectory, "*.cbz", SearchOption.TopDirectoryOnly)) {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(archivePath);
            if (retained.Contains(fullPath)) {
                continue;
            }

            File.Delete(fullPath);
            var signaturePath = SignaturePath(fullPath);
            if (File.Exists(signaturePath)) {
                File.Delete(signaturePath);
            }
        }

        foreach (var stagingPath in Directory.GetFiles(
                     rootDirectory,
                     $"{StagingPrefix}*",
                     SearchOption.TopDirectoryOnly)) {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.GetLastWriteTimeUtc(stagingPath) < DateTime.UtcNow.AddDays(-1)) {
                File.Delete(stagingPath);
            }
        }
        foreach (var signaturePath in Directory.GetFiles(
                     rootDirectory,
                     $"*.cbz{SignatureSuffix}",
                     SearchOption.TopDirectoryOnly)) {
            cancellationToken.ThrowIfCancellationRequested();
            var archivePath = signaturePath[..^SignatureSuffix.Length];
            if (!File.Exists(archivePath)) {
                File.Delete(signaturePath);
            }
        }

        return Task.CompletedTask;
    }

    private IEnumerable<string> DiscoverDirectories(
        LibraryRootData root,
        IReadOnlySet<string> excludedPaths,
        ISet<string> failedPaths,
        CancellationToken cancellationToken) {
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(root.Path));
        while (pending.Count > 0) {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            if (IsExcluded(directory, excludedPaths)) {
                continue;
            }

            yield return directory;
            if (!root.Recursive) {
                continue;
            }

            string[] children;
            try {
                children = Directory.GetDirectories(directory);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                logger.LogWarning(ex, "ScanComic: could not enumerate directory {Path}", directory);
                failedPaths.Add(directory);
                continue;
            }

            foreach (var child in children
                         .OrderByDescending(path => path, NaturalFileNameComparer.Instance)) {
                var info = new DirectoryInfo(child);
                if (info.Name.StartsWith('.') ||
                    info.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                    IsExcluded(child, excludedPaths)) {
                    continue;
                }
                pending.Push(child);
            }
        }
    }

    private static ComicFolderCandidate? InspectCandidate(
        LibraryRootData root,
        string directory,
        IReadOnlySet<string> excludedPaths) {
        string[] childDirectories;
        string[] files;
        try {
            childDirectories = Directory.GetDirectories(directory)
                .Where(path => !Path.GetFileName(path).StartsWith('.') && !IsExcluded(path, excludedPaths))
                .ToArray();
            files = Directory.GetFiles(directory)
                .Where(path => !IsExcluded(path, excludedPaths) && !IgnoredFileNames.Contains(Path.GetFileName(path)))
                .ToArray();
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return new ComicFolderCandidate(directory, [], [], [directory], ex.Message);
        }

        var pages = files
            .Where(path => SupportedExtensions.ComicPage.Contains(Path.GetExtension(path)))
            .OrderBy(path => Path.GetFileName(path), NaturalFileNameComparer.Instance)
            .ToArray();
        var sidecars = files
            .Where(SupportedExtensions.IsComicMetadataSidecar)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var hasExplicitBoundary = sidecars.Length > 0;
        var isRoot = FileSystemPathComparison.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.Path)));
        var implicitComicLeaf = !root.ScanImages && !isRoot && childDirectories.Length == 0 && pages.Length > 0;
        if (!hasExplicitBoundary && !implicitComicLeaf) {
            return null;
        }

        var inputs = pages.Concat(sidecars).ToArray();
        if (childDirectories.Length > 0) {
            return new ComicFolderCandidate(
                directory,
                pages,
                sidecars,
                inputs,
                "comic boundaries cannot contain child directories");
        }
        if (pages.Length == 0) {
            return new ComicFolderCandidate(directory, pages, sidecars, inputs, "no supported image pages were found");
        }

        var recognized = inputs.ToHashSet(FileSystemPathComparison.Comparer);
        var mixedFiles = files.Where(path => !recognized.Contains(path)).ToArray();
        if (mixedFiles.Length > 0) {
            return new ComicFolderCandidate(
                directory,
                pages,
                sidecars,
                files,
                $"mixed content includes unsupported file {Path.GetFileName(mixedFiles[0])}");
        }

        var duplicate = inputs
            .GroupBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) {
            return new ComicFolderCandidate(
                directory,
                pages,
                sidecars,
                inputs,
                $"duplicate member name {duplicate.Key}");
        }

        return new ComicFolderCandidate(directory, pages, sidecars, inputs, null);
    }

    private async Task<NormalizedComicArchive> NormalizeCandidateAsync(
        Guid rootId,
        ComicFolderCandidate candidate,
        CancellationToken cancellationToken) {
        foreach (var pagePath in candidate.PagePaths) {
            await ValidateImageAsync(pagePath, cancellationToken);
        }
        foreach (var sidecarPath in candidate.SidecarPaths) {
            await ValidateXmlAsync(sidecarPath, cancellationToken);
        }

        var signature = await ContentSignatureAsync(candidate.InputPaths, cancellationToken);
        var managedDirectory = ManagedDirectory(rootId);
        Directory.CreateDirectory(managedDirectory);
        var archivePath = Path.Combine(managedDirectory, StablePathKey(candidate.DirectoryPath) + ".cbz");
        var signaturePath = SignaturePath(archivePath);
        if (File.Exists(archivePath) && SignatureMatches(signaturePath, signature)) {
            try {
                await VerifyArchiveAsync(archivePath, candidate, cancellationToken);
                return Result(candidate, archivePath, signature);
            } catch (Exception ex) when (ex is InvalidDataException or XmlException) {
                // A managed artifact was truncated or externally modified. Rebuild it from originals.
            }
        }

        var stagingArchive = Path.Combine(managedDirectory, $"{StagingPrefix}{Guid.NewGuid():N}.cbz");
        var stagingSignature = Path.Combine(managedDirectory, $"{StagingPrefix}{Guid.NewGuid():N}.sha256");
        try {
            await CreateArchiveAsync(stagingArchive, candidate, cancellationToken);
            await VerifyArchiveAsync(stagingArchive, candidate, cancellationToken);
            await File.WriteAllTextAsync(stagingSignature, signature + Environment.NewLine, cancellationToken);
            File.Move(stagingArchive, archivePath, overwrite: true);
            File.Move(stagingSignature, signaturePath, overwrite: true);
        } finally {
            if (File.Exists(stagingArchive)) File.Delete(stagingArchive);
            if (File.Exists(stagingSignature)) File.Delete(stagingSignature);
        }

        return Result(candidate, archivePath, signature);
    }

    private static NormalizedComicArchive Result(
        ComicFolderCandidate candidate,
        string archivePath,
        string signature) =>
        new(
            archivePath,
            Path.TrimEndingDirectorySeparator(candidate.DirectoryPath) + ".cbz",
            candidate.DirectoryPath,
            signature);

    private static async Task CreateArchiveAsync(
        string archivePath,
        ComicFolderCandidate candidate,
        CancellationToken cancellationToken) {
        await using var stream = new FileStream(
            archivePath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
        foreach (var sourcePath in candidate.PagePaths.Concat(candidate.SidecarPaths)) {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.CreateEntry(Path.GetFileName(sourcePath), CompressionLevel.Optimal);
            entry.LastWriteTime = StableZipTimestamp;
            await using var input = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true);
            await using var output = entry.Open();
            await input.CopyToAsync(output, cancellationToken);
        }
    }

    private static async Task VerifyArchiveAsync(
        string archivePath,
        ComicFolderCandidate candidate,
        CancellationToken cancellationToken) {
        using var archive = ZipFile.OpenRead(archivePath);
        var expectedNames = candidate.PagePaths
            .Concat(candidate.SidecarPaths)
            .Select(Path.GetFileName)
            .ToArray();
        var actualEntries = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToArray();
        if (!actualEntries.Select(entry => entry.FullName).SequenceEqual(expectedNames, StringComparer.Ordinal)) {
            throw new InvalidDataException("The normalized archive member order did not match its source.");
        }

        foreach (var entry in actualEntries.Take(candidate.PagePaths.Count)) {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = entry.Open();
            using var codec = SKCodec.Create(stream);
            EnsureImageDecodes(codec, entry.FullName);
        }
        foreach (var entry in actualEntries.Skip(candidate.PagePaths.Count)) {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = entry.Open();
            _ = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        }
    }

    private static async Task ValidateImageAsync(string path, CancellationToken cancellationToken) {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        cancellationToken.ThrowIfCancellationRequested();
        using var codec = SKCodec.Create(stream);
        EnsureImageDecodes(codec, path);
    }

    private static void EnsureImageDecodes(SKCodec? codec, string displayPath) {
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0) {
            throw new InvalidDataException($"Comic page {displayPath} could not be decoded.");
        }

        var scale = Math.Min(
            1f,
            ValidationDecodeMaxDimension / (float)Math.Max(codec.Info.Width, codec.Info.Height));
        var dimensions = codec.GetScaledDimensions(scale);
        using var bitmap = SKBitmap.Decode(codec, codec.Info.WithSize(dimensions));
        if (bitmap is null) {
            throw new InvalidDataException($"Comic page {displayPath} could not be decoded.");
        }
    }

    private static async Task ValidateXmlAsync(string path, CancellationToken cancellationToken) {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        _ = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
    }

    private static async Task<string> ContentSignatureAsync(
        IReadOnlyList<string> inputPaths,
        CancellationToken cancellationToken) {
        using var overall = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in inputPaths) {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Encoding.UTF8.GetBytes(Path.GetFileName(path));
            var length = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(length, name.Length);
            overall.AppendData(length);
            overall.AppendData(name);
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true);
            var fileHash = await SHA256.HashDataAsync(stream, cancellationToken);
            overall.AppendData(fileHash);
        }

        return Convert.ToHexString(overall.GetHashAndReset()).ToLowerInvariant();
    }

    private string ManagedDirectory(Guid rootId) => Path.Combine(_managedRoot, rootId.ToString("N"));

    private static string SignaturePath(string archivePath) => archivePath + SignatureSuffix;

    private static bool SignatureMatches(string signaturePath, string expected) {
        try {
            return File.Exists(signaturePath) && string.Equals(
                File.ReadAllText(signaturePath).Trim(),
                expected,
                StringComparison.OrdinalIgnoreCase);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return false;
        }
    }

    private static string StablePathKey(string path) {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (OperatingSystem.IsWindows()) normalized = normalized.ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    private static bool IsExcluded(string path, IReadOnlySet<string> excludedPaths) =>
        excludedPaths.Any(excluded => FileSystemPathComparison.IsSameOrDescendant(excluded, path));

    private sealed record ComicFolderCandidate(
        string DirectoryPath,
        IReadOnlyList<string> PagePaths,
        IReadOnlyList<string> SidecarPaths,
        IReadOnlyList<string> InputPaths,
        string? RejectionReason);

    private sealed class NaturalFileNameComparer : IComparer<string> {
        public static NaturalFileNameComparer Instance { get; } = new();

        public int Compare(string? left, string? right) {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            var leftIndex = 0;
            var rightIndex = 0;
            while (leftIndex < left.Length && rightIndex < right.Length) {
                if (char.IsDigit(left[leftIndex]) && char.IsDigit(right[rightIndex])) {
                    var numberComparison = CompareNumber(left, ref leftIndex, right, ref rightIndex);
                    if (numberComparison != 0) return numberComparison;
                    continue;
                }

                var comparison = char.ToUpperInvariant(left[leftIndex])
                    .CompareTo(char.ToUpperInvariant(right[rightIndex]));
                if (comparison != 0) return comparison;
                leftIndex++;
                rightIndex++;
            }
            return left.Length.CompareTo(right.Length);
        }

        private static int CompareNumber(string left, ref int leftIndex, string right, ref int rightIndex) {
            var leftStart = leftIndex;
            var rightStart = rightIndex;
            while (leftIndex < left.Length && char.IsDigit(left[leftIndex])) leftIndex++;
            while (rightIndex < right.Length && char.IsDigit(right[rightIndex])) rightIndex++;
            var leftDigits = left.AsSpan(leftStart, leftIndex - leftStart).TrimStart('0');
            var rightDigits = right.AsSpan(rightStart, rightIndex - rightStart).TrimStart('0');
            if (leftDigits.Length != rightDigits.Length) return leftDigits.Length.CompareTo(rightDigits.Length);
            var comparison = leftDigits.SequenceCompareTo(rightDigits);
            return comparison != 0
                ? comparison
                : (leftIndex - leftStart).CompareTo(rightIndex - rightStart);
        }
    }
}
