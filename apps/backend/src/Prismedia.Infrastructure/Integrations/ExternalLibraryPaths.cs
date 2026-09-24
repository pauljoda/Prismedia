using Prismedia.Application.Files;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>
/// Strict translation from a connected application's remote file path to the mapped local folder.
/// Remote parsing and containment belong to <see cref="RemoteLibraryPath"/>; this type adds only the local
/// filesystem boundary check. Remote paths never fall through as local paths.
/// </summary>
internal static class ExternalLibraryPaths {
    #region Actions - Translation

    internal static string? Resolve(string remoteRoot, string localRoot, string remoteFile) {
        var segments = RemoteLibraryPath.Parse(remoteRoot).RelativeSegments(RemoteLibraryPath.Parse(remoteFile));
        if (segments is not { Count: > 0 }) {
            return null;
        }

        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment)
            || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || segment.Contains(':'))) {
            throw new ArgumentException("The remote filename cannot be represented safely on this server.");
        }

        var local = Path.GetFullPath(Path.Combine([localRoot, .. segments]));
        var canonicalRoot = CompletedPayloadFileSystem.CanonicalPath(localRoot);
        if (!FileSystemPathComparison.IsSameOrDescendant(canonicalRoot, CompletedPayloadFileSystem.CanonicalPath(local))) {
            throw new ArgumentException("The mapped file escapes its local library boundary.");
        }

        return local;
    }

    #endregion
}
