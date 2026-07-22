using System.Text.RegularExpressions;

namespace Prismedia.Application.Acquisition;

public static partial class AcquisitionImportErrorSanitizer {
    public static string Sanitize(string error, string? payloadRootPath, string? libraryRootPath) {
        var sanitized = ReplaceRoot(error, payloadRootPath, "source");
        sanitized = ReplaceRoot(sanitized, libraryRootPath, "library");
        sanitized = WindowsAbsolutePathPattern().Replace(sanitized, "[path]");
        sanitized = UnixAbsolutePathPattern().Replace(sanitized, "[path]");
        sanitized = SecretPattern().Replace(sanitized, "$1=[redacted]");
        return sanitized.Length <= 2_000 ? sanitized : sanitized[..2_000];
    }

    private static string ReplaceRoot(string value, string? root, string label) {
        if (string.IsNullOrWhiteSpace(root)) { return value; }
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return value.Replace(normalizedRoot + Path.DirectorySeparatorChar, string.Empty, StringComparison.Ordinal)
            .Replace(normalizedRoot + Path.AltDirectorySeparatorChar, string.Empty, StringComparison.Ordinal)
            .Replace(normalizedRoot, label, StringComparison.Ordinal);
    }

    [GeneratedRegex("(?i)(api[-_ ]?key|token|password|secret)\\s*=\\s*[^;\\s]+")]
    private static partial Regex SecretPattern();

    [GeneratedRegex("(?i)(?<![a-z0-9])(?:[a-z]:[\\\\/])[^\\s;,)]+")]
    private static partial Regex WindowsAbsolutePathPattern();

    [GeneratedRegex("(?<![a-zA-Z0-9:])/[^\\s;,)]+")]
    private static partial Regex UnixAbsolutePathPattern();
}
