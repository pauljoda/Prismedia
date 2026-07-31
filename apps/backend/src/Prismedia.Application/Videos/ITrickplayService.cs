namespace Prismedia.Application.Videos;

/// <summary>
/// Serves HLS image playlists and tiled JPEG sheets for timeline scrubbing.
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
/// JPEG tile-sheet asset used by a trickplay playlist.
/// </summary>
public sealed record TrickplayTile(
    string Path,
    string ContentType,
    string CacheControl);
