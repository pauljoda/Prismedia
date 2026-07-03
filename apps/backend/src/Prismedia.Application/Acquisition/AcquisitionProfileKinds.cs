using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Maps an acquisition's media kind to the profile kind that governs it. Profiles are user-facing and
/// coarser than acquisition units: one "TV" profile (kind <see cref="EntityKind.VideoSeries"/>) governs
/// both season-pack and single-episode acquisitions, the way one book profile governs every book. The
/// single mapping site — the profile store resolves through it, so no caller translates kinds itself.
/// </summary>
public static class AcquisitionProfileKinds {
    /// <summary>The profile kind governing acquisitions of <paramref name="acquisitionKind"/>.</summary>
    public static EntityKind For(EntityKind acquisitionKind) => acquisitionKind switch {
        EntityKind.VideoSeason or EntityKind.Video or EntityKind.VideoSeries => EntityKind.VideoSeries,
        _ => acquisitionKind,
    };

    /// <summary>The closed set of kinds a profile may be created for, in display order.</summary>
    public static readonly IReadOnlyList<EntityKind> All = [
        EntityKind.Book,
        EntityKind.Movie,
        EntityKind.VideoSeries,
        EntityKind.AudioLibrary,
    ];
}
