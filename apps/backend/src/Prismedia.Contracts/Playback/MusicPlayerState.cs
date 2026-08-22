using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Playback;

/// <summary>
/// Converts one concrete playback item into its owning Entity's canonical progress cursor.
/// </summary>
/// <param name="ItemId">Concrete item presented by the shared player.</param>
/// <param name="CurrentEntityId">Entity stored as the canonical cursor owner.</param>
/// <param name="Unit">Canonical unit stored by the shared progress capability.</param>
/// <param name="StartIndex">Canonical index at the start of the playback item.</param>
/// <param name="EndIndex">Canonical index at the end of the playback item.</param>
/// <param name="Total">Canonical unit total for the owning cursor.</param>
/// <param name="Mode">Optional presentation mode retained when playback advances the cursor.</param>
/// <param name="ResourceLocation">Optional portable resource location advanced within the item.</param>
public sealed record PlaybackProgressMapping(
    Guid ItemId,
    Guid CurrentEntityId,
    ProgressUnit Unit,
    int StartIndex,
    int EndIndex,
    int Total,
    ReaderMode? Mode,
    string? ResourceLocation = null);

/// <summary>
/// Compact, exact queue item consumed by every shared audio-player client. This is a
/// playback projection rather than an Entity-kind DTO: any Entity accepted by the
/// audio source endpoint can be represented without hydrating its full detail graph.
/// </summary>
/// <param name="Id">Playable Entity identifier.</param>
/// <param name="Title">Display title.</param>
/// <param name="ParentEntityId">Optional structural parent used as queue/library context.</param>
/// <param name="SortOrder">Optional structural source order.</param>
/// <param name="IsNsfw">Whether the item is marked NSFW.</param>
/// <param name="IsOrganized">Whether file organization is complete.</param>
/// <param name="IsWanted">Whether this is an unfulfilled request placeholder.</param>
/// <param name="HasSourceMedia">Whether the Entity owns playable source media.</param>
/// <param name="DurationSeconds">Exact probed duration in seconds.</param>
/// <param name="BitRate">Optional probed bit rate.</param>
/// <param name="SampleRate">Optional probed sample rate.</param>
/// <param name="Channels">Optional probed channel count.</param>
/// <param name="Codec">Optional probed codec.</param>
/// <param name="EmbeddedArtist">Optional artist embedded in the source.</param>
/// <param name="EmbeddedAlbum">Optional album embedded in the source.</param>
/// <param name="SectionLabel">Optional source section or disc label.</param>
/// <param name="WaveformPath">Optional generated waveform asset path.</param>
/// <param name="Rating">Current user's rating.</param>
/// <param name="AccessCount">Current user's access count.</param>
/// <param name="LastActiveAt">Current user's latest activity timestamp.</param>
/// <param name="CreatedAt">Library creation timestamp.</param>
public sealed record AudioPlaybackItem(
    Guid Id,
    string Title,
    Guid? ParentEntityId,
    int? SortOrder,
    bool IsNsfw,
    bool IsOrganized,
    bool IsWanted,
    bool HasSourceMedia,
    double? DurationSeconds,
    int? BitRate,
    int? SampleRate,
    int? Channels,
    string? Codec,
    string? EmbeddedArtist,
    string? EmbeddedAlbum,
    string? SectionLabel,
    string? WaveformPath,
    int? Rating,
    int AccessCount,
    DateTimeOffset? LastActiveAt,
    DateTimeOffset CreatedAt);

/// <summary>
/// Context labels and artwork fallbacks used by the global music player.
/// </summary>
/// <param name="AlbumId">Album/library entity currently represented by the queue, when known.</param>
/// <param name="AlbumTitle">Album/library title currently represented by the queue, when known.</param>
/// <param name="ArtistId">Artist entity currently represented by the queue, when known.</param>
/// <param name="ArtistName">Artist name currently represented by the queue, when known.</param>
/// <param name="CoverUrl">Fallback artwork URL for the current queue.</param>
/// <param name="AlbumCoverUrls">Per-album artwork fallbacks for mixed-album queues.</param>
/// <param name="PlaybackOwnerEntityId">
/// Optional aggregate whose time-based resume state owns this queue. Audiobook queues set this to the
/// parent Book while their individual files remain ordinary AudioTrack items.
/// </param>
/// <param name="PlaybackOwnerTitle">Display title for <paramref name="PlaybackOwnerEntityId"/>.</param>
/// <param name="PlaybackOwnerEntityKind">Typed kind of the playback owner; never inferred from the id.</param>
/// <param name="ProgressMappings">
/// Optional item-to-owner mappings used by the shared player to advance canonical Entity progress.
/// Missing mappings leave the owner's existing progress cursor untouched.
/// </param>
/// <param name="PreservesQueueOrder">Whether the queue capability requires semantic source order.</param>
/// <param name="SupportsPlaybackRate">Whether the queue capability permits variable-rate playback.</param>
public sealed record MusicPlayerContext(
    Guid? AlbumId,
    string? AlbumTitle,
    Guid? ArtistId,
    string? ArtistName,
    string? CoverUrl,
    IReadOnlyDictionary<Guid, string?>? AlbumCoverUrls,
    Guid? PlaybackOwnerEntityId = null,
    string? PlaybackOwnerTitle = null,
    EntityKind? PlaybackOwnerEntityKind = null,
    IReadOnlyList<PlaybackProgressMapping>? ProgressMappings = null,
    bool PreservesQueueOrder = false,
    bool SupportsPlaybackRate = false);

/// <summary>
/// Persisted browser-scoped music player state returned to the web client.
/// </summary>
/// <param name="Tracks">Compact queue tracks in source order, with missing/deleted tracks filtered out.</param>
/// <param name="Order">Indices into <paramref name="Tracks"/> representing the current play order.</param>
/// <param name="Position">Index into <paramref name="Order"/> for the current track, or -1 when the queue is empty.</param>
/// <param name="CurrentTime">Current playback time in seconds for the restored track.</param>
/// <param name="Playing">Whether the last persisted transport intent was playing.</param>
/// <param name="Shuffle">Whether shuffle is enabled for the restored queue.</param>
/// <param name="Repeat">Repeat behavior for the restored queue.</param>
/// <param name="Volume">Player volume in the inclusive range 0..1.</param>
/// <param name="Muted">Whether audio output was muted.</param>
/// <param name="Collapsed">Whether the player was shown as the mini player.</param>
/// <param name="CollapsedSide">Horizontal side used by the mini player.</param>
/// <param name="Context">Optional now-playing context labels and artwork.</param>
public sealed record MusicPlayerStateResponse(
    IReadOnlyList<AudioPlaybackItem> Tracks,
    IReadOnlyList<int> Order,
    int Position,
    double CurrentTime,
    bool Playing,
    bool Shuffle,
    MusicPlayerRepeatMode Repeat,
    double Volume,
    bool Muted,
    bool Collapsed,
    MusicPlayerMiniSide CollapsedSide,
    MusicPlayerContext? Context);

/// <summary>
/// Request body used to replace the persisted browser-scoped music player state.
/// </summary>
/// <param name="QueueTrackIds">Audio track ids in source queue order.</param>
/// <param name="Order">Indices into <paramref name="QueueTrackIds"/> representing the current play order.</param>
/// <param name="Position">Index into <paramref name="Order"/> for the current track, or -1 when empty.</param>
/// <param name="CurrentTime">Current playback time in seconds for the current track.</param>
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
    double CurrentTime,
    bool Playing,
    bool Shuffle,
    MusicPlayerRepeatMode Repeat,
    double Volume,
    bool Muted,
    bool Collapsed,
    MusicPlayerMiniSide CollapsedSide,
    MusicPlayerContext? Context);

/// <summary>
/// Request body used to advance transport progress without replacing or returning the
/// persisted browser-scoped music queue.
/// </summary>
/// <param name="CurrentTrackId">
/// Track expected at <paramref name="Position"/>. Stale progress is ignored when it no
/// longer matches the persisted queue.
/// </param>
/// <param name="Position">Index into the persisted play order for the current track.</param>
/// <param name="CurrentTime">Current playback time in seconds for the current track.</param>
/// <param name="Playing">Whether the client intends playback to be running.</param>
public sealed record UpdateMusicPlayerProgressRequest(
    Guid CurrentTrackId,
    int Position,
    double CurrentTime,
    bool Playing);
