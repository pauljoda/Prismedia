namespace Prismedia.Domain.Integrations;

/// <summary>
/// An absolute folder or file path as a connected application reports it on its own host. Windows drive
/// and network-share paths compare case-insensitively; POSIX paths compare exactly. Traversal segments,
/// relative paths, and ambiguous separators are never accepted, so containment is decided by whole
/// path segments rather than by string prefixes.
/// </summary>
public sealed class RemoteLibraryPath {
    #region Static Variables

    private const int MaximumLength = 8192;

    #endregion

    #region Variables

    private readonly string[] _segments;
    private readonly RemotePathSyntax _syntax;

    /// <summary>Normalized path with forward slashes and no trailing separator (a drive root keeps one).</summary>
    public string Value { get; }

    /// <summary>Whether this path uses Windows drive or network-share syntax.</summary>
    public bool IsWindows => _syntax != RemotePathSyntax.Posix;

    private StringComparison Comparison => IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    #endregion

    #region Constructors

    private RemoteLibraryPath(RemotePathSyntax syntax, string[] segments) {
        _syntax = syntax;
        _segments = segments;
        Value = syntax switch {
            RemotePathSyntax.Share => "//" + string.Join('/', segments),
            RemotePathSyntax.Drive when segments.Length == 1 => segments[0] + "/",
            RemotePathSyntax.Drive => string.Join('/', segments),
            _ => "/" + string.Join('/', segments)
        };
    }

    #endregion

    #region Actions - Parsing

    /// <summary>Parses a remote path, explaining exactly why an unusable path is refused.</summary>
    /// <exception cref="ArgumentException">The path is empty, relative, traversing, or ambiguous.</exception>
    public static RemoteLibraryPath Parse(string? path) {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaximumLength || path.Any(char.IsControl)) {
            throw new ArgumentException("Choose an absolute remote library path.", nameof(path));
        }

        var forward = path.Replace('\\', '/');
        var syntax = forward.StartsWith("//", StringComparison.Ordinal) ? RemotePathSyntax.Share
            : IsDrivePath(forward) ? RemotePathSyntax.Drive
            : RemotePathSyntax.Posix;
        if (syntax == RemotePathSyntax.Posix && path.Contains('\\')) {
            throw new ArgumentException("Unix paths containing backslashes cannot be mapped safely.", nameof(path));
        }

        if (syntax == RemotePathSyntax.Posix && !forward.StartsWith('/')) {
            throw new ArgumentException("Choose an absolute remote library path.", nameof(path));
        }

        var segments = forward.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or "..")) {
            throw new ArgumentException("Library paths cannot contain traversal segments.", nameof(path));
        }

        if (syntax == RemotePathSyntax.Share && segments.Length < 2) {
            throw new ArgumentException("A network path requires a server and share.", nameof(path));
        }

        return new(syntax, segments);
    }

    /// <summary>Parses a remote path without throwing; untrusted provider input that is unusable returns false.</summary>
    public static bool TryParse(string? path, out RemoteLibraryPath? result) {
        try {
            result = Parse(path);
            return true;
        } catch (ArgumentException) {
            result = null;
            return false;
        }
    }

    private static bool IsDrivePath(string forwardPath) =>
        forwardPath.Length >= 3 && char.IsAsciiLetter(forwardPath[0]) && forwardPath[1] == ':' && forwardPath[2] == '/';

    #endregion

    #region Actions - Containment

    /// <summary>
    /// Whether <paramref name="path"/> lies strictly beneath this root. A holding or file is never the
    /// mapped root itself, so equality does not count.
    /// </summary>
    public bool IsAncestorOf(RemoteLibraryPath path) => RelativeSegments(path) is { Count: > 0 };

    /// <summary>Whether an untrusted provider path lies strictly beneath this root; unusable paths do not.</summary>
    public bool IsAncestorOf(string? path) => TryParse(path, out var parsed) && IsAncestorOf(parsed!);

    /// <summary>
    /// The whole segments of <paramref name="path"/> below this root, an empty list for the root itself, or
    /// null when the path lies outside it.
    /// </summary>
    public IReadOnlyList<string>? RelativeSegments(RemoteLibraryPath path) {
        if (path._syntax != _syntax || path._segments.Length < _segments.Length) {
            return null;
        }

        for (var index = 0; index < _segments.Length; index++) {
            if (!string.Equals(_segments[index], path._segments[index], Comparison)) {
                return null;
            }
        }

        return path._segments[_segments.Length..];
    }

    /// <summary>
    /// Whether two mapped roots share any folder. This comparison ignores case even for POSIX paths so an
    /// ambiguous mapping is refused rather than guessed.
    /// </summary>
    public bool Overlaps(RemoteLibraryPath other) {
        var shorter = Math.Min(_segments.Length, other._segments.Length);
        return _syntax == other._syntax && Enumerable.Range(0, shorter)
            .All(index => string.Equals(_segments[index], other._segments[index], StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    /// <summary>How a remote path is rooted; it decides case sensitivity and display form.</summary>
    private enum RemotePathSyntax {
        Posix,
        Drive,
        Share
    }
}
