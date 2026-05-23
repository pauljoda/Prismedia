namespace Prismedia.Application.Jobs.Ports;

/// <summary>
/// Port for discovering media files on the filesystem.
/// </summary>
public interface IFileDiscovery {
    Task<IReadOnlyList<string>> DiscoverFilesAsync(
        string rootPath,
        MediaCategory category,
        bool recursive,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverFilesByDirectoryAsync(
        string rootPath,
        MediaCategory category,
        bool recursive,
        CancellationToken cancellationToken);
}

public enum MediaCategory {
    Video,
    Image,
    Audio,
    ComicArchive
}
