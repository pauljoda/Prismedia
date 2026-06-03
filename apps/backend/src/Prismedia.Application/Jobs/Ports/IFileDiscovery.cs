using Prismedia.Application.Jobs.Scanning;

namespace Prismedia.Application.Jobs.Ports;

/// <summary>
/// Port for discovering media files on the filesystem.
/// </summary>
public interface IFileDiscovery {
    Task<IReadOnlyList<string>> DiscoverFilesAsync(
        string rootPath,
        MediaCategory category,
        bool recursive,
        IReadOnlySet<string> excludedPaths,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverFilesByDirectoryAsync(
        string rootPath,
        MediaCategory category,
        bool recursive,
        IReadOnlySet<string> excludedPaths,
        CancellationToken cancellationToken);

    /// <summary>
    /// Enumerates the same files as <see cref="DiscoverFilesAsync"/> but returns each with its size
    /// and last-write time, for cheap change detection between scans. The signature is read during
    /// the directory walk, so this costs one stat per file and never opens file contents.
    /// </summary>
    Task<IReadOnlyList<FileSignature>> DiscoverFileSignaturesAsync(
        string rootPath,
        MediaCategory category,
        bool recursive,
        IReadOnlySet<string> excludedPaths,
        CancellationToken cancellationToken);
}

public enum MediaCategory {
    Video,
    Image,
    Audio,
    ComicArchive,

    /// <summary>Single-file books (EPUB, PDF) discovered alongside comic archives in book roots.</summary>
    Book
}
