namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of repeat behaviors for the persisted in-app music player queue.
/// </summary>
public enum MusicPlayerRepeatMode {
    /// <summary>Stop at the end of the queue.</summary>
    [Code("off")]
    Off,

    /// <summary>Loop the whole queue.</summary>
    [Code("all")]
    All,

    /// <summary>Loop the current track.</summary>
    [Code("one")]
    One
}
