using Prismedia.Application.Files;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Jobs.Scanning;

namespace Prismedia.Infrastructure.Media.Adapters;

/// <summary>
/// Adapts the Infrastructure FileDiscoveryService to the Application port interface.
/// </summary>
public sealed class FileDiscoveryAdapter(FileDiscoveryService inner) : IFileDiscovery {
    public async Task<IReadOnlyList<string>> DiscoverFilesAsync(
        string rootPath, MediaCategory category, bool recursive, IReadOnlySet<string> excludedPaths, CancellationToken cancellationToken) {
        var paths = await inner.DiscoverFilesAsync(
            rootPath,
            ExtensionsFor(category),
            recursive,
            excludedPaths,
            cancellationToken);
        return Filter(category, paths);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverFilesByDirectoryAsync(
        string rootPath, MediaCategory category, bool recursive, IReadOnlySet<string> excludedPaths, CancellationToken cancellationToken) {
        var grouped = await inner.DiscoverFilesByDirectoryAsync(
            rootPath,
            ExtensionsFor(category),
            recursive,
            excludedPaths,
            cancellationToken);
        return category != MediaCategory.ComicMetadataSidecar
            ? grouped
            : grouped
                .Select(pair => new KeyValuePair<string, IReadOnlyList<string>>(
                    pair.Key,
                    Filter(category, pair.Value)))
                .Where(pair => pair.Value.Count > 0)
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    FileSystemPathComparison.Comparer);
    }

    public async Task<IReadOnlyList<FileSignature>> DiscoverFileSignaturesAsync(
        string rootPath, MediaCategory category, bool recursive, IReadOnlySet<string> excludedPaths, CancellationToken cancellationToken) {
        var signatures = await inner.DiscoverFileSignaturesAsync(
            rootPath,
            ExtensionsFor(category),
            recursive,
            excludedPaths,
            skipGeneratedSuffixes: category != MediaCategory.VideoSubtitleSidecar,
            cancellationToken: cancellationToken);
        return category != MediaCategory.ComicMetadataSidecar
            ? signatures
            : signatures.Where(signature =>
                SupportedExtensions.IsComicMetadataSidecar(signature.Path)).ToArray();
    }

    private static IReadOnlyList<string> Filter(MediaCategory category, IReadOnlyList<string> paths) =>
        category != MediaCategory.ComicMetadataSidecar
            ? paths
            : paths.Where(SupportedExtensions.IsComicMetadataSidecar).ToArray();

    private static IReadOnlySet<string> ExtensionsFor(MediaCategory category) => category switch {
        MediaCategory.Video => SupportedExtensions.Video,
        MediaCategory.VideoSubtitleSidecar => SupportedExtensions.VideoSubtitleSidecar,
        MediaCategory.Image => SupportedExtensions.Image,
        MediaCategory.Audio => SupportedExtensions.Audio,
        MediaCategory.ComicArchive => SupportedExtensions.ComicArchive,
        MediaCategory.ComicPage => SupportedExtensions.ComicPage,
        MediaCategory.ComicMetadataSidecar => SupportedExtensions.ComicMetadataSidecar,
        MediaCategory.Book => SupportedExtensions.Book,
        MediaCategory.Audiobook => SupportedExtensions.Audiobook,
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };
}
