namespace Prismedia.Domain.Entities;

/// <summary>Named source-resolution tiers shared by metadata badges and media filters.</summary>
public enum MediaResolutionTier {
    /// <summary>8K source tier.</summary>
    [Code("8K")] Uhd8K,
    /// <summary>4K source tier, including cropped widescreen masters.</summary>
    [Code("4K")] Uhd4K,
    /// <summary>1440-line source tier.</summary>
    [Code("1440p")] Qhd,
    /// <summary>1080-line source tier.</summary>
    [Code("1080p")] FullHd,
    /// <summary>720-line source tier.</summary>
    [Code("720p")] Hd,
    /// <summary>480-line and PAL standard-definition source tier.</summary>
    [Code("480p")] Standard480,
    /// <summary>Known dimensions below the named 480-line tier.</summary>
    [Code("SD")] Sd,
}
