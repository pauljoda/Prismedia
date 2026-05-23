namespace Prismedia.Application.Videos;

/// <summary>
/// Serves Jellyfin-style HLS image playlists and tiled JPEG sheets for timeline scrubbing.
/// </summary>
public interface ITrickplayService {
    Task<TrickplayPlaylist?> GetPlaylistAsync(Guid itemId, int width, CancellationToken cancellationToken);

    Task<TrickplayTile?> GetTileAsync(Guid itemId, int width, int index, CancellationToken cancellationToken);
}

/// <summary>
/// Text playlist metadata for an HLS images-only trickplay rendition.
/// </summary>
public sealed record TrickplayPlaylist(
    string Content,
    string CacheControl);

/// <summary>
/// JPEG tile-sheet asset used by a Jellyfin-style trickplay playlist.
/// </summary>
public sealed record TrickplayTile(
    string Path,
    string ContentType,
    string CacheControl);
