using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media;

/// <summary>A source tier's inclusive width-or-height threshold.</summary>
/// <param name="Tier">Canonical tier identity.</param>
/// <param name="MinimumWidth">Minimum source width in pixels.</param>
/// <param name="MinimumHeight">Minimum source height in pixels.</param>
public sealed record MediaResolutionThreshold(MediaResolutionTier Tier, int MinimumWidth, int MinimumHeight);

/// <summary>
/// Classifies source dimensions consistently across Entity metadata and filters. Width preserves
/// the source tier of cropped widescreen masters; height handles narrower aspect ratios.
/// </summary>
public static class MediaResolutionPolicy {
    /// <summary>Immutable thresholds, highest tier first. The first matching threshold wins.</summary>
    public static IReadOnlyList<MediaResolutionThreshold> Tiers { get; } = Array.AsReadOnly<MediaResolutionThreshold>([
        new(MediaResolutionTier.Uhd8K, 7600, 4300),
        new(MediaResolutionTier.Uhd4K, 3800, 2000),
        new(MediaResolutionTier.Qhd, 2540, 1400),
        new(MediaResolutionTier.FullHd, 1800, 1000),
        new(MediaResolutionTier.Hd, 1200, 700),
        new(MediaResolutionTier.Standard480, 640, 480),
        new(MediaResolutionTier.Sd, 1, 1),
    ]);

    /// <summary>Returns the highest matching tier, or null when no positive dimension is known.</summary>
    /// <param name="width">Source width in pixels, when known.</param>
    /// <param name="height">Source height in pixels, when known.</param>
    /// <returns>The source-resolution tier without modifying stored dimensions.</returns>
    public static MediaResolutionTier? Classify(int? width, int? height) =>
        Tiers.FirstOrDefault(tier => width >= tier.MinimumWidth || height >= tier.MinimumHeight)?.Tier;
}
