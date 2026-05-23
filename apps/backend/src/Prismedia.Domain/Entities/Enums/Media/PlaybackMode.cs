namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of preferred playback startup strategies.
/// </summary>
public enum PlaybackMode {
    /// <summary>Try direct playback first when the browser can play the source.</summary>
    [Code("direct")]
    Direct,

    /// <summary>Use HLS playback when a stream is available or can be generated.</summary>
    [Code("hls")]
    Hls
}
