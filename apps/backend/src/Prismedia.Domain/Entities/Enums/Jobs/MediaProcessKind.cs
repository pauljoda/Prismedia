namespace Prismedia.Domain.Entities;

/// <summary>Identifies the resource behavior of an ffmpeg process.</summary>
public enum MediaProcessKind {
    /// <summary>User-requested playback, which is observed but never capacity limited.</summary>
    [Code("playback")]
    Playback,

    /// <summary>Derived-media generation, which may wait for playback and host headroom.</summary>
    [Code("background")]
    Background
}
