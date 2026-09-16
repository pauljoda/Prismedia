using Prismedia.Application.Files;
using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Strict cross-platform path translation. Remote paths never fall through as local paths.</summary>
internal static class ExternalLibraryPaths {
    internal static string NormalizeRemote(string path) {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 8192 || path.Any(char.IsControl)) throw new ArgumentException("Choose an absolute remote library path.");
        var windowsSyntax = IsWindows(path.Replace('\\', '/')) || path.StartsWith("\\\\") || path.StartsWith("//");
        if (!windowsSyntax && path.Contains('\\')) throw new ArgumentException("Unix paths containing backslashes cannot be mapped safely.");
        var normalized = windowsSyntax ? path.Replace('\\', '/') : path;
        var windows = IsWindows(normalized);
        if (!normalized.StartsWith('/') && !windows) throw new ArgumentException("Choose an absolute remote library path.");
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or "..")) throw new ArgumentException("Library paths cannot contain traversal segments.");
        if (normalized.StartsWith("//") && segments.Length < 2) throw new ArgumentException("A network path requires a server and share.");
        return normalized.TrimEnd('/') + "/";
    }

    internal static string? Resolve(string remoteRoot, string localRoot, string remoteFile) {
        var root = NormalizeRemote(remoteRoot);
        var file = NormalizeRemote(remoteFile).TrimEnd('/');
        var comparison = IsWindows(root) || root.StartsWith("//") ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!file.StartsWith(root, comparison)) return null;
        var relative = file[root.Length..];
        var segments = relative.Split('/');
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || segment.Contains(':')))
            throw new ArgumentException("The remote filename cannot be represented safely on this server.");
        var local = Path.GetFullPath(Path.Combine([localRoot, .. segments]));
        var canonicalRoot = CompletedPayloadFileSystem.CanonicalPath(localRoot);
        if (!FileSystemPathComparison.IsSameOrDescendant(canonicalRoot, CompletedPayloadFileSystem.CanonicalPath(local)))
            throw new ArgumentException("The mapped file escapes its local library boundary.");
        return local;
    }

    private static bool IsWindows(string path) => path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '/';
}
