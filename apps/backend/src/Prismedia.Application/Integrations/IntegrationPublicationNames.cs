using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Prismedia.Application.Integrations;

/// <summary>Deterministic publication filenames isolate operations without making their storage identity a display title.</summary>
public static partial class IntegrationPublicationNames {
    /// <summary>Produces the stable collision-fenced filename used for an accepted artifact.</summary>
    public static string FileName(Guid operationId, string title, string artifactId, string sourceFileName) {
        var stem = new string(title.Where(character => !char.IsControl(character) && character is not ('/' or '\\' or ':' or '"' or '<' or '>' or '|' or '?' or '*')).Take(80).ToArray()).Trim().TrimEnd('.');
        if (stem.Length == 0) stem = "Publication";
        return $"{stem} [{operationId:N}-{ArtifactKey(artifactId)}]{Path.GetExtension(sourceFileName).ToLowerInvariant()}";
    }
    /// <summary>Stable artifact suffix also used for unpublished partial files.</summary>
    public static string ArtifactKey(string artifactId) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(artifactId)))[..16];
    /// <summary>Finds only a candidate journal ID. Callers must verify the accepted path and bytes before trusting any title.</summary>
    public static Guid? CandidateOperation(string path) {
        return ParseOperation(Path.GetFileNameWithoutExtension(path));
    }
    /// <summary>Finds a candidate on a folder without treating dots in its display title as an extension.</summary>
    public static Guid? CandidateFolderOperation(string path) => ParseOperation(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)));
    private static Guid? ParseOperation(string name) {
        var match = OperationSuffix().Match(name);
        return match.Success && Guid.TryParseExact(match.Groups[1].Value, "N", out var id) ? id : null;
    }
    [GeneratedRegex(@" \[([a-f0-9]{32})-[a-f0-9]{16}\]$", RegexOptions.CultureInvariant)]
    private static partial Regex OperationSuffix();
}
