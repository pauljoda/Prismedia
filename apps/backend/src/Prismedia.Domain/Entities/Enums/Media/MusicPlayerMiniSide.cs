namespace Prismedia.Domain.Entities;

/// <summary>
/// Horizontal docking side for the persisted mini music player.
/// </summary>
public enum MusicPlayerMiniSide {
    /// <summary>Dock the mini player on the left edge.</summary>
    [Code("left")]
    Left,

    /// <summary>Dock the mini player on the right edge.</summary>
    [Code("right")]
    Right
}
