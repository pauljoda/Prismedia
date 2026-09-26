using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Deterministic folder and member names preserve group order through ordinary filesystem scans.</summary>
public static class IntegrationGalleryNames {
    #region Actions - Naming

    /// <summary>Uses the existing collision-fenced operation naming scheme without a file extension.</summary>
    public static string FolderName(Guid operationId, string title, string groupId) =>
        IntegrationPublicationNames.FileName(operationId, title, groupId, string.Empty);

    /// <summary>Order precedes artifact identity so lexicographic scans retain the sealed manifest sequence.</summary>
    public static string FileName(IntegrationArtifact artifact) =>
        $"{artifact.Ordinal:00000}-{IntegrationPublicationNames.ArtifactKey(artifact.Id)}{Path.GetExtension(artifact.RelativePath).ToLowerInvariant()}";

    #endregion
}
