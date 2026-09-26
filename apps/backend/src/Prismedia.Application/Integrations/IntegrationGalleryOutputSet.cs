using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>One complete, bounded gallery with a single explicit group and contiguous source-declared reading order.</summary>
public sealed class IntegrationGalleryOutputSet {
    #region Static Variables

    /// <summary>Maximum images imported as one finite gallery selection.</summary>
    public const int MaximumImages = 1000;

    /// <summary>Import rules for each still image in a gallery.</summary>
    private static IntegrationImportPolicy Member => IntegrationImportPolicy.For(EntityKind.Gallery).Content;

    #endregion

    #region Variables

    /// <summary>Opaque group identity from the sealed executor manifest.</summary>
    public string GroupId { get; }

    /// <summary>Exact image outputs in their declared one-based order.</summary>
    public IReadOnlyList<IntegrationArtifact> Artifacts { get; }

    #endregion

    #region Constructors

    /// <summary>Rejects gaps, mixed groups/items, sidecars, unsupported formats, and byte budgets before placement.</summary>
    public IntegrationGalleryOutputSet(IReadOnlyList<IntegrationArtifact> artifacts, string selectedItemId, long maximumBytes) {
        if (artifacts is not { Count: > 0 and <= MaximumImages } || maximumBytes <= 0 || string.IsNullOrWhiteSpace(selectedItemId)
            || artifacts.Any(artifact => artifact is null) || string.IsNullOrWhiteSpace(artifacts[0].GroupId)) {
            throw new ArgumentException("A gallery requires a bounded, explicitly ordered image group.");
        }

        GroupId = artifacts[0].GroupId!;
        var ordered = artifacts.OrderBy(artifact => artifact.Ordinal).ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        for (var index = 0; index < ordered.Length; index++) {
            var artifact = ordered[index];
            if (artifact.ItemId != selectedItemId || artifact.GroupId != GroupId || artifact.Ordinal != index + 1
                || artifact.Role != IntegrationArtifactRole.Content || !ids.Add(artifact.Id)
                || !Member.AcceptsFileName(artifact.RelativePath)
                || artifact.SizeBytes <= 0 || artifact.SizeBytes > Member.MaximumBytes
                || artifact.SizeBytes > maximumBytes - total) {
                throw new ArgumentException(
                    "The gallery must contain only the selected item's complete ordered images within the accepted limits.");
            }

            total += artifact.SizeBytes;
        }

        Artifacts = ordered;
    }

    #endregion
}
