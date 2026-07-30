using Prismedia.Domain.Entities;
using Prismedia.Application.Requests;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Maps an acquisition's media kind to the profile kind that governs it. Profiles are user-facing and
/// coarser than acquisition units: one "TV" profile (kind <see cref="EntityKind.VideoSeries"/>) governs
/// both season-pack and single-episode acquisitions, the way one book profile governs every book. The
/// single mapping site — the profile store resolves through it, so no caller translates kinds itself.
/// </summary>
public static class AcquisitionProfileKinds {
    /// <summary>The profile kind governing acquisitions of <paramref name="acquisitionKind"/>.</summary>
    public static EntityKind For(EntityKind acquisitionKind) {
        var profileKinds = RequestKindRegistry.All
            .Where(descriptor => descriptor.AcquisitionKind == acquisitionKind)
            .Select(descriptor => descriptor.ProfileEntityKind)
            .OfType<EntityKind>()
            .Distinct()
            .ToArray();
        return profileKinds.Length switch {
            0 => acquisitionKind,
            1 => profileKinds[0],
            _ => throw new InvalidOperationException(
                $"Acquisition kind '{acquisitionKind}' maps to multiple profile kinds: " +
                string.Join(", ", profileKinds))
        };
    }

    /// <summary>The closed set of kinds a profile may be created for, in display order.</summary>
    public static readonly IReadOnlyList<EntityKind> All = RequestKindRegistry.All
        .Select(descriptor => descriptor.ProfileEntityKind)
        .OfType<EntityKind>()
        .Distinct()
        .ToArray();
}
