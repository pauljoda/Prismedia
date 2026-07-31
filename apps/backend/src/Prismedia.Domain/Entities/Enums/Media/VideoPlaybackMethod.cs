namespace Prismedia.Domain.Entities;

/// <summary>How Prismedia will deliver a video source for one playback session.</summary>
public enum VideoPlaybackMethod {
    /// <summary>The client reads the original source file unchanged.</summary>
    [Code("direct")]
    Direct,

    /// <summary>The video stream is copied while its container or audio stream is adapted.</summary>
    [Code("remux")]
    Remux,

    /// <summary>The video stream is re-encoded for the requesting client.</summary>
    [Code("transcode")]
    Transcode,
}
