using Prismedia.Application.Acquisition;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// Reads a completed download's payload for the per-kind import engines: enumerates files with sizes
/// relative to the content root. A single-file content path yields one entry rooted at its parent
/// directory, mirroring the book planner's semantics.
/// </summary>
public sealed class DownloadPayloadReader : IDownloadPayloadReader {
    public DownloadPayload? Read(string contentPath) {
        try {
            if (File.Exists(contentPath)) {
                var info = new FileInfo(contentPath);
                return new DownloadPayload(
                    info.DirectoryName ?? contentPath,
                    [new ImportCandidateFile(info.Name, info.Length)]);
            }

            if (!Directory.Exists(contentPath)) {
                return null;
            }

            var files = Directory
                .EnumerateFiles(contentPath, "*", SearchOption.AllDirectories)
                .Select(file => new FileInfo(file))
                .Select(info => new ImportCandidateFile(Path.GetRelativePath(contentPath, info.FullName), info.Length))
                .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return new DownloadPayload(contentPath, files);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return null;
        }
    }
}
