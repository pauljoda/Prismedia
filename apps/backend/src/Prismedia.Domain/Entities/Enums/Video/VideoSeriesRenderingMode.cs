namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of layouts for displaying a video series detail view.
/// </summary>
public enum VideoSeriesRenderingMode {
    /// <summary>Render all direct episodes as one flat list.</summary>
    [Code("flat")]
    Flat,

    /// <summary>Render episodes grouped beneath season entities.</summary>
    [Code("seasons")]
    Seasons,

    /// <summary>Render both direct episodes and season groups without hiding either branch.</summary>
    [Code("mixed")]
    Mixed
}
