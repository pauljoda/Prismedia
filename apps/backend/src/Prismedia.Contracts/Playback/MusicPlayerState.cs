using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Playback;

/// <summary>
/// Context labels and artwork fallbacks used by the global music player.
/// </summary>
/// <param name="AlbumId">Album/library entity currently represented by the queue, when known.</param>
/// <param name="AlbumTitle">Album/library title currently represented by the queue, when known.</param>
/// <param name="ArtistId">Artist entity currently represented by the queue, when known.</param>
/// <param name="ArtistName">Artist name currently represented by the queue, when known.</param>
/// <param name="CoverUrl">Fallback artwork URL for the current queue.</param>
/// <param name="AlbumCoverUrls">Per-album artwork fallbacks for mixed-album queues.</param>
public sealed record MusicPlayerContext(
    Guid? AlbumId,
    string? AlbumTitle,
    Guid? ArtistId,
    string? ArtistName,
    string? CoverUrl,
    IReadOnlyDictionary<Guid, string?>? AlbumCoverUrls);

/// <summary>
/// Persisted global music player state returned to the web client.
/// </summary>
/// <param name="Tracks">Hydrated queue tracks in source queue order, with missing/deleted tracks filtered out.</param>
/// <param name="Order">Indices into <paramref name="Tracks"/> representing the current play order.</param>
/// <param name="Position">Index into <paramref name="Order"/> for the current track, or -1 when the queue is empty.</param>
/// <param name="Playing">Whether the last persisted transport intent was playing.</param>
/// <param name="Shuffle">Whether shuffle is enabled for the restored queue.</param>
/// <param name="Repeat">Repeat behavior for the restored queue.</param>
/// <param name="Volume">Player volume in the inclusive range 0..1.</param>
/// <param name="Muted">Whether audio output was muted.</param>
/// <param name="Collapsed">Whether the player was shown as the mini player.</param>
/// <param name="CollapsedSide">Horizontal side used by the mini player.</param>
/// <param name="Context">Optional now-playing context labels and artwork.</param>
public sealed record MusicPlayerStateResponse(
    IReadOnlyList<AudioTrackDetail> Tracks,
    IReadOnlyList<int> Order,
    int Position,
    bool Playing,
    bool Shuffle,
    MusicPlayerRepeatMode Repeat,
    double Volume,
    bool Muted,
    bool Collapsed,
    MusicPlayerMiniSide CollapsedSide,
    MusicPlayerContext? Context);

/// <summary>
/// Request body used to replace the persisted global music player state.
/// </summary>
/// <param name="QueueTrackIds">Audio track ids in source queue order.</param>
/// <param name="Order">Indices into <paramref name="QueueTrackIds"/> representing the current play order.</param>
/// <param name="Position">Index into <paramref name="Order"/> for the current track, or -1 when empty.</param>
/// <param name="Playing">Whether the client intends playback to be running.</param>
/// <param name="Shuffle">Whether shuffle is enabled.</param>
/// <param name="Repeat">Repeat behavior.</param>
/// <param name="Volume">Player volume in the inclusive range 0..1.</param>
/// <param name="Muted">Whether output is muted.</param>
/// <param name="Collapsed">Whether the mini player is active.</param>
/// <param name="CollapsedSide">Horizontal side used by the mini player.</param>
/// <param name="Context">Optional queue labels and artwork fallbacks.</param>
public sealed record UpdateMusicPlayerStateRequest(
    IReadOnlyList<Guid> QueueTrackIds,
    IReadOnlyList<int> Order,
    int Position,
    bool Playing,
    bool Shuffle,
    MusicPlayerRepeatMode Repeat,
    double Volume,
    bool Muted,
    bool Collapsed,
    MusicPlayerMiniSide CollapsedSide,
    MusicPlayerContext? Context);
