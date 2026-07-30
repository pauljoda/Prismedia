using Prismedia.Contracts.Entities;

namespace Prismedia.Contracts.Media;

/// <summary>
/// Playback queue snapshot for an audio track. Entity detail routes return
/// <see cref="EntityCard"/>; this derived shape remains only in the music-player payload.
/// </summary>
public sealed record AudioTrackDetail : EntityCard {
    /// <summary>Artist value read from embedded audio tags, when known.</summary>
    public required string? EmbeddedArtist { get; init; }

    /// <summary>Album value read from embedded audio tags, when known.</summary>
    public required string? EmbeddedAlbum { get; init; }
}
