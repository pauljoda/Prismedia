using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Default field/image selection for applying a plugin proposal without a manual review — the shared
/// "accept everything the provider sent" rule used by auto-identify and by request commits (which
/// populate wanted entities from a proposal the user already reviewed on the request surface).
/// </summary>
public static class ProposalApplySelection {
    /// <summary>
    /// Builds the field keys present in the proposal so an unattended apply imports provider metadata
    /// while leaving user-owned fields such as rating untouched. Structural children are always applied
    /// by the apply service regardless of the selected field set.
    /// </summary>
    public static IReadOnlyCollection<string> SelectAllPresentFields(EntityMetadataProposal proposal) {
        var patch = proposal.Patch;
        var fields = new List<string>();

        if (patch is not null) {
            if (!string.IsNullOrWhiteSpace(patch.Title)) fields.Add(MetadataPatchField.Title.ToCode());
            if (!string.IsNullOrWhiteSpace(patch.Description)) fields.Add(MetadataPatchField.Description.ToCode());
            if (patch.ExternalIds is { Count: > 0 }) fields.Add(MetadataPatchField.ExternalIds.ToCode());
            if (patch.Urls is { Count: > 0 }) fields.Add(MetadataPatchField.Urls.ToCode());
            if (patch.Dates is { Count: > 0 }) fields.Add(MetadataPatchField.Dates.ToCode());
            if (patch.Stats is { Count: > 0 }) fields.Add(MetadataPatchField.Stats.ToCode());
            if (patch.Positions is { Count: > 0 }) fields.Add(MetadataPatchField.Positions.ToCode());
            if (!string.IsNullOrWhiteSpace(patch.Classification)) fields.Add(MetadataPatchField.Classification.ToCode());
            if (patch.Flags is not null) fields.Add(MetadataPatchField.Flags.ToCode());
            if (patch.Tags is { Count: > 0 }) fields.Add(MetadataPatchField.Tags.ToCode());
            if (!string.IsNullOrWhiteSpace(patch.Studio)) fields.Add(MetadataPatchField.Studio.ToCode());
            if (patch.Credits is { Count: > 0 }) fields.Add(MetadataPatchField.Credits.ToCode());
        }

        if (proposal.Images is { Count: > 0 }) fields.Add(MetadataPatchField.Images.ToCode());

        return fields;
    }

    /// <summary>
    /// Picks the first candidate per artwork role, mirroring the manual review's default image
    /// selection. Logo art is skipped because it is only meaningful for studios, which are applied
    /// as relationship proposals with their own artwork handling.
    /// </summary>
    public static IReadOnlyDictionary<string, string?>? SelectDefaultImages(EntityMetadataProposal proposal) {
        if (proposal.Images is not { Count: > 0 }) {
            return null;
        }

        var images = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var image in proposal.Images) {
            if (string.IsNullOrWhiteSpace(image.Url) || ImageKindRoleResolver.Is(image.Kind, MediaImageKind.Logo)) {
                continue;
            }

            images.TryAdd(image.Kind, image.Url);
        }

        return images.Count > 0 ? images : null;
    }
}
