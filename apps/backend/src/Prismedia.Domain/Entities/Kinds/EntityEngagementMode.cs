namespace Prismedia.Domain.Entities;

/// <summary>Shared vocabulary used when presenting completion and in-progress state.</summary>
public enum EntityEngagementMode {
    /// <summary>The kind does not expose position, completion, or engagement filters.</summary>
    [Code("none")]
    None,

    /// <summary>The kind uses watched, unwatched, and in-progress language.</summary>
    [Code("playback")]
    Playback,

    /// <summary>The kind uses read, unread, and reading language.</summary>
    [Code("reading")]
    Reading
}
