using Prismedia.Contracts.Entities;

namespace Prismedia.Contracts.Media;

/// <summary>
/// API-facing detail shape for a playable audio track.
/// </summary>
public sealed record AudioTrackDetail : EntityDetail {
    /// <summary>Artist value read from embedded audio tags, when known.</summary>
    public required string? EmbeddedArtist { get; init; }

    /// <summary>Album value read from embedded audio tags, when known.</summary>
    public required string? EmbeddedAlbum { get; init; }
}
