using System.Text.Json;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Entities;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Media;
using Prismedia.Contracts.Playback;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Playback;

/// <summary>
/// Application use-case service for the household-wide in-app music player state.
/// The client remains responsible for real audio playback; this service stores only
/// the queue, transport intent, and UI settings needed to reconstruct the player after
/// a page refresh.
/// </summary>
public sealed class MusicPlayerStateService {
    /// <summary>
    /// Reserved app-settings key holding the music player state JSON document. Not a
    /// registered catalog setting.
    /// </summary>
    internal const string StateKey = "ui.music-player-state";

    private static readonly JsonSerializerOptions SerializerOptions = JsonSerializerOptions.Web;

    private readonly ISettingsPersistence _settings;
    private readonly IEntityReadService _entities;
    private readonly ILogger<MusicPlayerStateService>? _logger;

    /// <summary>
    /// Creates the service over settings persistence and entity read ports.
    /// </summary>
    public MusicPlayerStateService(
        ISettingsPersistence settings,
        IEntityReadService entities,
        ILogger<MusicPlayerStateService>? logger = null) {
        _settings = settings;
        _entities = entities;
        _logger = logger;
    }

    /// <summary>
    /// Loads the persisted player state, filtering missing tracks and repairing invalid
    /// order/position data into a safe empty or reduced queue.
    /// </summary>
    public async Task<MusicPlayerStateResponse> GetAsync(CancellationToken cancellationToken) {
        var overrides = await _settings.LoadSettingOverridesAsync(cancellationToken);
        if (!overrides.TryGetValue(StateKey, out var rawJson) || string.IsNullOrWhiteSpace(rawJson)) {
            return Empty();
        }

        StoredMusicPlayerState? stored;
        try {
            stored = JsonSerializer.Deserialize<StoredMusicPlayerState>(rawJson, SerializerOptions);
        } catch (JsonException ex) {
            _logger?.LogWarning(ex, "Stored music player state is invalid JSON and will be ignored.");
            return Empty();
        }

        return stored is null ? Empty() : await HydrateAsync(stored, cancellationToken);
    }

    /// <summary>
    /// Replaces the persisted player state. Empty queues clear the server state so a
    /// dismissed player does not reappear after refresh.
    /// </summary>
    public async Task<MusicPlayerStateResponse> SaveAsync(
        UpdateMusicPlayerStateRequest request,
        CancellationToken cancellationToken) {
        if (request.QueueTrackIds.Count == 0) {
            await ClearAsync(cancellationToken);
            return Empty();
        }

        var stored = Normalize(request);
        if (stored.QueueTrackIds.Count == 0) {
            await ClearAsync(cancellationToken);
            return Empty();
        }

        var json = JsonSerializer.Serialize(stored, SerializerOptions);
        await _settings.SaveSettingOverrideAsync(StateKey, json, cancellationToken);
        return await HydrateAsync(stored, cancellationToken);
    }

    /// <summary>
    /// Removes any persisted music player state.
    /// </summary>
    public async Task ClearAsync(CancellationToken cancellationToken) =>
        await _settings.DeleteSettingOverrideAsync(StateKey, cancellationToken);

    private async Task<MusicPlayerStateResponse> HydrateAsync(
        StoredMusicPlayerState stored,
        CancellationToken cancellationToken) {
        var tracks = new List<AudioTrackDetail>();
        var oldToNewIndex = new Dictionary<int, int>();

        for (var i = 0; i < stored.QueueTrackIds.Count; i++) {
            var card = await _entities.GetDetailAsync(
                stored.QueueTrackIds[i],
                EntityKindRegistry.AudioTrack.Code,
                hideNsfw: false,
                cancellationToken);
            if (card is not AudioTrackDetail track) {
                continue;
            }

            oldToNewIndex[i] = tracks.Count;
            tracks.Add(track);
        }

        if (tracks.Count == 0) {
            return Empty() with {
                Volume = stored.Volume,
                Muted = stored.Muted,
                Collapsed = stored.Collapsed,
                CollapsedSide = DecodeOrDefault(stored.CollapsedSide, MusicPlayerMiniSide.Left),
                Context = stored.Context,
            };
        }

        var order = stored.Order
            .Where(oldToNewIndex.ContainsKey)
            .Select(index => oldToNewIndex[index])
            .Distinct()
            .ToArray();
        if (order.Length != tracks.Count) {
            order = Enumerable.Range(0, tracks.Count).ToArray();
        }

        var position = stored.Position >= 0 && stored.Position < order.Length
            ? stored.Position
            : 0;

        var repeat = DecodeOrDefault(stored.Repeat, MusicPlayerRepeatMode.Off);
        var collapsedSide = DecodeOrDefault(stored.CollapsedSide, MusicPlayerMiniSide.Left);

        return new MusicPlayerStateResponse(
            tracks,
            order,
            position,
            stored.Playing,
            stored.Shuffle,
            repeat,
            stored.Volume,
            stored.Muted,
            stored.Collapsed,
            collapsedSide,
            stored.Context);
    }

    private static StoredMusicPlayerState Normalize(UpdateMusicPlayerStateRequest request) {
        var trackIds = request.QueueTrackIds
            .Where(id => id != Guid.Empty)
            .ToArray();
        var order = request.Order
            .Where(index => index >= 0 && index < trackIds.Length)
            .Distinct()
            .ToArray();
        if (order.Length != trackIds.Length) {
            order = Enumerable.Range(0, trackIds.Length).ToArray();
        }

        var position = trackIds.Length == 0
            ? -1
            : Math.Clamp(request.Position, 0, Math.Max(0, order.Length - 1));

        return new StoredMusicPlayerState(
            trackIds,
            order,
            position,
            request.Playing,
            request.Shuffle,
            request.Repeat.ToCode(),
            Math.Clamp(request.Volume, 0, 1),
            request.Muted,
            request.Collapsed,
            request.CollapsedSide.ToCode(),
            request.Context);
    }

    private static TEnum DecodeOrDefault<TEnum>(string? code, TEnum fallback)
        where TEnum : struct, Enum =>
        !string.IsNullOrWhiteSpace(code) && code.TryDecodeAs<TEnum>(out var decoded)
            ? decoded
            : fallback;

    private static MusicPlayerStateResponse Empty() => new(
        [],
        [],
        -1,
        false,
        false,
        MusicPlayerRepeatMode.Off,
        1,
        false,
        false,
        MusicPlayerMiniSide.Left,
        null);

    private sealed record StoredMusicPlayerState(
        IReadOnlyList<Guid> QueueTrackIds,
        IReadOnlyList<int> Order,
        int Position,
        bool Playing,
        bool Shuffle,
        string Repeat,
        double Volume,
        bool Muted,
        bool Collapsed,
        string CollapsedSide,
        MusicPlayerContext? Context);
}
