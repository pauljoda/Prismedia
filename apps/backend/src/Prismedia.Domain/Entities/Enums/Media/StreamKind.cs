namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of probed media stream kinds carried on stream rows and playback
/// source/stream projections. Codes keep the historical PascalCase spelling because it
/// is the persisted value and matches the Jellyfin-compatible stream wire shape.
/// </summary>
public enum StreamKind {
    /// <summary>Video stream.</summary>
    [Code("Video")]
    Video,

    /// <summary>Audio stream.</summary>
    [Code("Audio")]
    Audio
}
