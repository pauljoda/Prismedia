using Prismedia.Application.Acquisition;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Enumerates the on-disk files an acquisition imported into the library, for the detail view.</summary>
public sealed class ImportedFilesReader : IImportedFilesReader {
    public IReadOnlyList<DownloadItemFile> List(string path) {
        try {
            if (File.Exists(path)) {
                var info = new FileInfo(path);
                return [new DownloadItemFile(info.Name, info.Length, 1)];
            }

            if (!Directory.Exists(path)) {
                return [];
            }

            return Directory
                .EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Select(file => new FileInfo(file))
                .Select(info => new DownloadItemFile(Path.GetRelativePath(path, info.FullName), info.Length, 1))
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return [];
        }
    }
}
