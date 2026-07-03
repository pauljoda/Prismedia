namespace Prismedia.Application.Acquisition;

/// <summary>
/// Detects executable / dangerous files in a completed download's payload. A payload carrying one is
/// held for manual review instead of imported — a release whose "video" is a <c>.scr</c> is malware,
/// and silently importing (or silently skipping) it would either endanger the library host or leave
/// the acquisition stuck with no explanation. Extension lists mirror Sonarr's, which this gate was
/// modeled on.
/// </summary>
public static class DangerousFileDetection {
    // prism-vocab: external — the extension vocabulary matches Sonarr's DangerousExtensions +
    // ExecutableExtensions sets so behavior is drop-in familiar.
    private static readonly IReadOnlySet<string> DangerousExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        ".arj", ".lnk", ".lzh", ".ps1", ".scr", ".vbs", ".zipx",
        ".bat", ".cmd", ".exe", ".sh", ".msi", ".com", ".pif"
    };

    /// <summary>The first dangerous file among the payload paths, or null when the payload is clean.</summary>
    public static string? FindDangerousFile(IEnumerable<string> filePaths) =>
        filePaths.FirstOrDefault(path => DangerousExtensions.Contains(Path.GetExtension(path)));
}
