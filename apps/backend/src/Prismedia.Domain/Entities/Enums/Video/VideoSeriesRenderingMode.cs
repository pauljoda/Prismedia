namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of layouts for displaying a video series detail view.
/// </summary>
public enum VideoSeriesRenderingMode {
    /// <summary>Render all videos as one flat list.</summary>
    [Code("flat")]
    Flat,

    /// <summary>Render videos grouped beneath season entities.</summary>
    [Code("seasons")]
    Seasons
}
