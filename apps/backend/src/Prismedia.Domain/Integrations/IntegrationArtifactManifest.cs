using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>Immutable byte evidence for one source output. RelativePath is a suggested portable name, never a destination path.</summary>
public sealed record IntegrationArtifact(string Id, string ItemId, string RelativePath, string MediaType,
    long SizeBytes, string Sha256, IntegrationArtifactRole Role, string? GroupId = null, int? Ordinal = null);

/// <summary>A complete immutable output set from one remote job revision, validated before byte transfer or import.</summary>
public sealed class IntegrationArtifactManifest {
    /// <summary>Maximum outputs accepted in one finite acquisition.</summary>
    public const int MaximumArtifacts = 10000;
    /// <summary>Upper bound for the sum of final output lengths.</summary>
    public const long MaximumTotalBytes = 250L * 1024 * 1024 * 1024;
    /// <summary>Remote job identity within its owning connection and installation.</summary>
    public string JobId { get; }
    /// <summary>Immutable upstream manifest revision.</summary>
    public string Revision { get; }
    /// <summary>Validated exact output set, retaining declared group ordering.</summary>
    public IReadOnlyList<IntegrationArtifact> Artifacts { get; }

    /// <summary>Accepts only a complete sealed revision; missing pages, duplicate identities, unsafe paths, and invalid hashes are rejected.</summary>
    public IntegrationArtifactManifest(string jobId, string revision, bool isSealed, int artifactCount, IReadOnlyList<IntegrationArtifact> artifacts) {
        if (string.IsNullOrWhiteSpace(jobId) || jobId.Length > 512 || string.IsNullOrWhiteSpace(revision) || revision.Length > 512)
            throw new ArgumentException("A stable job and manifest revision are required.");
        if (!isSealed || artifacts is null || artifactCount != artifacts.Count || artifactCount is < 1 or > MaximumArtifacts)
            throw new ArgumentException("An artifact manifest must contain every output from a sealed, bounded revision.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var positions = new HashSet<(string, int)>();
        long total = 0;
        foreach (var artifact in artifacts) {
            if (artifact is null || string.IsNullOrWhiteSpace(artifact.Id) || artifact.Id.Length > 512 || !ids.Add(artifact.Id)
                || string.IsNullOrWhiteSpace(artifact.ItemId) || artifact.ItemId.Length > 2048 || !Enum.IsDefined(artifact.Role)
                || string.IsNullOrWhiteSpace(artifact.MediaType) || artifact.MediaType.Length > 256 || artifact.MediaType.Any(char.IsControl)
                || artifact.SizeBytes <= 0 || artifact.SizeBytes > MaximumTotalBytes - total || artifact.Sha256 is not { Length: 64 }
                || !artifact.Sha256.All(Uri.IsHexDigit)) throw new ArgumentException("The artifact manifest contains invalid or duplicate byte evidence.");
            if (!IsPortablePath(artifact.RelativePath) || !paths.Add(artifact.RelativePath))
                throw new ArgumentException("Artifact names must be unique portable relative paths without traversal.");
            if ((artifact.GroupId is null) != (artifact.Ordinal is null) || artifact.GroupId is { } group
                && (string.IsNullOrWhiteSpace(group) || group.Length > 512 || artifact.Ordinal is < 1 || !positions.Add((group, artifact.Ordinal!.Value))))
                throw new ArgumentException("Grouped artifacts require unique positive one-based positions.");
            total += artifact.SizeBytes;
        }
        if (!artifacts.Any(artifact => artifact.Role == IntegrationArtifactRole.Content))
            throw new ArgumentException("A fulfillment manifest must contain requested content.");
        JobId = jobId;
        Revision = revision;
        Artifacts = artifacts.Select(artifact => artifact with { Sha256 = artifact.Sha256.ToLowerInvariant() }).ToArray();
    }

    private static bool IsPortablePath(string? path) => !string.IsNullOrWhiteSpace(path) && path.Length <= 2048
        && !path.StartsWith('/') && !path.Contains('\\') && !path.Any(character => char.IsControl(character) || character is ':' or '*' or '?' or '"' or '<' or '>' or '|')
        && path.Split('/').All(segment => segment.Length is > 0 and <= 255 && segment is not ("." or "..") && !segment.EndsWith('.') && !segment.EndsWith(' '));
}
