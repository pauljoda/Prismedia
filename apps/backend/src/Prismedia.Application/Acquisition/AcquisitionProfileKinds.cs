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

    /// <summary>
    /// Whether the acquisition kind is governed by the specified profile naming family. Import and
    /// recovery flows use this rather than maintaining their own copies of the TV/film kind sets.
    /// </summary>
    public static bool UsesNamingFamily(EntityKind acquisitionKind, AcquisitionNamingFamily namingFamily) =>
        EntityKindRegistry.Describe(For(acquisitionKind)).AcquisitionProfile?.NamingFamily == namingFamily;

    /// <summary>
    /// Returns the durable import checkpoint protocol selected by the acquisition's governing profile
    /// definition. This is deliberately independent from naming-template behavior.
    /// </summary>
    public static AcquisitionCheckpointProtocol CheckpointProtocolFor(EntityKind acquisitionKind) =>
        EntityKindRegistry.Describe(For(acquisitionKind)).AcquisitionProfile?.CheckpointProtocol
        ?? throw new InvalidOperationException(
            $"Acquisition kind '{acquisitionKind}' does not have a governing acquisition profile.");

    /// <summary>The closed set of kinds a profile may be created for, in display order.</summary>
    public static readonly IReadOnlyList<EntityKind> All = EntityKindRegistry.All
        .Where(definition => definition.AcquisitionProfile is not null)
        .OrderBy(definition => definition.AcquisitionProfile!.DisplayOrder)
        .Select(definition => definition.Kind)
        .ToArray();
}
