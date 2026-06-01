using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Application.Jobs.Ports;

namespace Prismedia.Infrastructure.Media.Adapters;

/// <summary>
/// Adapts the Infrastructure FileDiscoveryService to the Application port interface.
/// </summary>
public sealed class FileDiscoveryAdapter(FileDiscoveryService inner) : IFileDiscovery {
    public Task<IReadOnlyList<string>> DiscoverFilesAsync(
        string rootPath, MediaCategory category, bool recursive, IReadOnlySet<string> excludedPaths, CancellationToken cancellationToken) {
        return inner.DiscoverFilesAsync(rootPath, ExtensionsFor(category), recursive, excludedPaths, cancellationToken);
    }

    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverFilesByDirectoryAsync(
        string rootPath, MediaCategory category, bool recursive, IReadOnlySet<string> excludedPaths, CancellationToken cancellationToken) {
        return inner.DiscoverFilesByDirectoryAsync(rootPath, ExtensionsFor(category), recursive, excludedPaths, cancellationToken);
    }

    private static IReadOnlySet<string> ExtensionsFor(MediaCategory category) => category switch {
        MediaCategory.Video => SupportedExtensions.Video,
        MediaCategory.Image => SupportedExtensions.Image,
        MediaCategory.Audio => SupportedExtensions.Audio,
        MediaCategory.ComicArchive => SupportedExtensions.ComicArchive,
        MediaCategory.Book => SupportedExtensions.Book,
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };
}
