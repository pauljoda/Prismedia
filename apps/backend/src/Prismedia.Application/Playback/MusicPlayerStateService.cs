using System.Text.Json;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Playback;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Playback;

/// <summary>
/// Application use-case service for browser-scoped in-app music player state.
/// The client remains responsible for real audio playback; this service stores only
/// the queue, transport intent, timestamp, and browser-local output settings needed to
/// reconstruct the player after a page refresh.
/// </summary>
public sealed class MusicPlayerStateService {
    private static readonly JsonSerializerOptions SerializerOptions = JsonSerializerOptions.Web;
    private static readonly string[] MusicPlayerSettingKeys = [
        BrowserSessionConstants.AudioOutputSettingKey,
        BrowserSessionConstants.AudioPlaybackStateSettingKey
    ];

    private readonly IBrowserSessionPersistence _sessions;
    private readonly IEntityReadService _entities;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MusicPlayerStateService>? _logger;

    /// <summary>
    /// Creates the service over browser-session persistence and entity read ports.
    /// </summary>
    public MusicPlayerStateService(
        IBrowserSessionPersistence sessions,
        IEntityReadService entities,
        TimeProvider? timeProvider = null,
        ILogger<MusicPlayerStateService>? logger = null) {
        _sessions = sessions;
        _entities = entities;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    /// <summary>
    /// Loads the persisted player state, filtering missing tracks and repairing invalid
    /// order/position data into a safe empty or reduced queue.
    /// </summary>
    public async Task<MusicPlayerStateResponse> GetAsync(
        Guid browserSessionId,
        CancellationToken cancellationToken) {
        var settings = await _sessions.LoadSettingsAsync(browserSessionId, MusicPlayerSettingKeys, cancellationToken);
        var output = LoadOutput(settings);

        if (!settings.TryGetValue(BrowserSessionConstants.AudioPlaybackStateSettingKey, out var rawJson) ||
            string.IsNullOrWhiteSpace(rawJson)) {
            return Empty(output);
        }

        var stored = DeserializeOrNull<StoredMusicPlayerPlaybackState>(
            rawJson,
            "Stored music player playback state is invalid JSON and will be ignored.");
        return stored is null ? Empty(output) : await HydrateAsync(stored, output, cancellationToken);
    }

    /// <summary>
    /// Replaces the browser-scoped player state. Empty queues clear only the playback
    /// queue state so browser audio-output preferences survive closing the player.
    /// </summary>
    public async Task SaveAsync(
        Guid browserSessionId,
        UpdateMusicPlayerStateRequest request,
        CancellationToken cancellationToken) {
        var output = NormalizeOutput(request);
        var upserts = new Dictionary<string, string>(StringComparer.Ordinal) {
            [BrowserSessionConstants.AudioOutputSettingKey] = JsonSerializer.Serialize(output, SerializerOptions)
        };
        var deletes = new List<string>();
        StoredMusicPlayerPlaybackState? playback = null;

        if (request.QueueTrackIds.Count > 0) {
            playback = NormalizePlayback(request);
            if (playback.QueueTrackIds.Count > 0) {
                upserts[BrowserSessionConstants.AudioPlaybackStateSettingKey] =
                    JsonSerializer.Serialize(playback, SerializerOptions);
            } else {
                playback = null;
            }
        }

        if (playback is null) {
            deletes.Add(BrowserSessionConstants.AudioPlaybackStateSettingKey);
        }

        await _sessions.ReplaceSettingsAsync(
            browserSessionId,
            upserts,
            deletes,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    /// <summary>
    /// Advances only the current transport position for an existing persisted queue. The
    /// supplied track identity prevents a delayed browser heartbeat from moving a newer queue.
    /// </summary>
    /// <returns>True when the stored queue still matched and its progress was updated.</returns>
    public async Task<bool> UpdateProgressAsync(
        Guid browserSessionId,
        UpdateMusicPlayerProgressRequest request,
        CancellationToken cancellationToken) {
        var settings = await _sessions.LoadSettingsAsync(
            browserSessionId,
            [BrowserSessionConstants.AudioPlaybackStateSettingKey],
            cancellationToken);
        if (!settings.TryGetValue(BrowserSessionConstants.AudioPlaybackStateSettingKey, out var rawJson) ||
            string.IsNullOrWhiteSpace(rawJson)) {
            return false;
        }

        var stored = DeserializeOrNull<StoredMusicPlayerPlaybackState>(
            rawJson,
            "Stored music player playback state is invalid JSON and progress will be ignored.");
        if (stored is null || !ProgressMatchesStoredQueue(stored, request)) {
            return false;
        }

        var updated = stored with {
            Position = request.Position,
            CurrentTime = Math.Max(0, FiniteOrDefault(request.CurrentTime, 0)),
            Playing = request.Playing
        };
        await _sessions.ReplaceSettingsAsync(
            browserSessionId,
            new Dictionary<string, string>(StringComparer.Ordinal) {
                [BrowserSessionConstants.AudioPlaybackStateSettingKey] =
                    JsonSerializer.Serialize(updated, SerializerOptions)
            },
            [],
            _timeProvider.GetUtcNow(),
            cancellationToken);
        return true;
    }

    /// <summary>
    /// Removes any persisted playback queue for this browser session.
    /// </summary>
    public async Task ClearAsync(Guid browserSessionId, CancellationToken cancellationToken) =>
        await _sessions.ReplaceSettingsAsync(
            browserSessionId,
            new Dictionary<string, string>(StringComparer.Ordinal),
            [BrowserSessionConstants.AudioPlaybackStateSettingKey],
            _timeProvider.GetUtcNow(),
            cancellationToken);

    private async Task<MusicPlayerStateResponse> HydrateAsync(
        StoredMusicPlayerPlaybackState stored,
        StoredMusicPlayerOutput output,
        CancellationToken cancellationToken) {
        var tracks = new List<EntityCard>();
        var oldToNewIndex = new Dictionary<int, int>();

        for (var i = 0; i < stored.QueueTrackIds.Count; i++) {
            var card = await _entities.GetAsync(
                stored.QueueTrackIds[i],
                EntityKind.AudioTrack.ToCode(),
                hideNsfw: false,
                cancellationToken);
            if (card is null) {
                continue;
            }

            if (card.Capabilities.OfType<FlagsCapability>().Any(flags => flags.IsWanted == true)) {
                continue;
            }

            oldToNewIndex[i] = tracks.Count;
            tracks.Add(card);
        }

        if (tracks.Count == 0) {
            return Empty(output);
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
        var currentTime = ClampCurrentTime(stored.CurrentTime, tracks, order, position);

        return new MusicPlayerStateResponse(
            tracks,
            order,
            position,
            currentTime,
            stored.Playing,
            stored.Shuffle,
            repeat,
            output.Volume,
            output.Muted,
            output.Collapsed,
            DecodeOrDefault(output.CollapsedSide, MusicPlayerMiniSide.Left),
            stored.Context);
    }

    private StoredMusicPlayerOutput LoadOutput(IReadOnlyDictionary<string, string> settings) {
        if (!settings.TryGetValue(BrowserSessionConstants.AudioOutputSettingKey, out var rawJson) ||
            string.IsNullOrWhiteSpace(rawJson)) {
            return StoredMusicPlayerOutput.Default;
        }

        return DeserializeOrNull<StoredMusicPlayerOutput>(
            rawJson,
            "Stored music player output settings are invalid JSON and will be ignored.") is { } output
            ? NormalizeOutput(output)
            : StoredMusicPlayerOutput.Default;
    }

    private T? DeserializeOrNull<T>(string rawJson, string logMessage)
        where T : class {
        try {
            return JsonSerializer.Deserialize<T>(rawJson, SerializerOptions);
        } catch (JsonException ex) {
            _logger?.LogWarning(ex, logMessage);
            return null;
        }
    }

    private static StoredMusicPlayerOutput NormalizeOutput(UpdateMusicPlayerStateRequest request) =>
        NormalizeOutput(new StoredMusicPlayerOutput(
            Math.Clamp(FiniteOrDefault(request.Volume, 1), 0, 1),
            request.Muted,
            request.Collapsed,
            request.CollapsedSide.ToCode()));

    private static StoredMusicPlayerOutput NormalizeOutput(StoredMusicPlayerOutput output) =>
        new(
            Math.Clamp(FiniteOrDefault(output.Volume, 1), 0, 1),
            output.Muted,
            output.Collapsed,
            DecodeOrDefault(output.CollapsedSide, MusicPlayerMiniSide.Left).ToCode());

    private static StoredMusicPlayerPlaybackState NormalizePlayback(UpdateMusicPlayerStateRequest request) {
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

        return new StoredMusicPlayerPlaybackState(
            trackIds,
            order,
            position,
            Math.Max(0, FiniteOrDefault(request.CurrentTime, 0)),
            request.Playing,
            request.Shuffle,
            request.Repeat.ToCode(),
            request.Context);
    }

    private static double ClampCurrentTime(
        double value,
        IReadOnlyList<EntityCard> tracks,
        IReadOnlyList<int> order,
        int position) {
        var currentTime = Math.Max(0, FiniteOrDefault(value, 0));
        if (position < 0 || position >= order.Count) {
            return 0;
        }

        var queueIndex = order[position];
        if (queueIndex < 0 || queueIndex >= tracks.Count) {
            return 0;
        }

        var duration = tracks[queueIndex]
            .Capabilities
            .OfType<TechnicalCapability>()
            .FirstOrDefault()
            ?.Duration
            ?.TotalSeconds;
        return duration is > 0 ? Math.Min(currentTime, duration.Value) : currentTime;
    }

    private static bool ProgressMatchesStoredQueue(
        StoredMusicPlayerPlaybackState stored,
        UpdateMusicPlayerProgressRequest request) {
        if (request.CurrentTrackId == Guid.Empty ||
            request.Position < 0 ||
            request.Position >= stored.Order.Count) {
            return false;
        }

        var queueIndex = stored.Order[request.Position];
        return queueIndex >= 0 &&
            queueIndex < stored.QueueTrackIds.Count &&
            stored.QueueTrackIds[queueIndex] == request.CurrentTrackId;
    }

    private static double FiniteOrDefault(double value, double fallback) =>
        double.IsFinite(value) ? value : fallback;

    private static TEnum DecodeOrDefault<TEnum>(string? code, TEnum fallback)
        where TEnum : struct, Enum =>
        !string.IsNullOrWhiteSpace(code) && code.TryDecodeAs<TEnum>(out var decoded)
            ? decoded
            : fallback;

    private static MusicPlayerStateResponse Empty(StoredMusicPlayerOutput? output = null) {
        var normalized = output is null ? StoredMusicPlayerOutput.Default : NormalizeOutput(output);
        return new MusicPlayerStateResponse(
        [],
        [],
        -1,
        0,
        false,
        false,
        MusicPlayerRepeatMode.Off,
        normalized.Volume,
        normalized.Muted,
        normalized.Collapsed,
        DecodeOrDefault(normalized.CollapsedSide, MusicPlayerMiniSide.Left),
        null);
    }

    private sealed record StoredMusicPlayerOutput(
        double Volume,
        bool Muted,
        bool Collapsed,
        string CollapsedSide) {
        public static readonly StoredMusicPlayerOutput Default = new(
            1,
            false,
            false,
            MusicPlayerMiniSide.Left.ToCode());
    }

    private sealed record StoredMusicPlayerPlaybackState(
        IReadOnlyList<Guid> QueueTrackIds,
        IReadOnlyList<int> Order,
        int Position,
        double CurrentTime,
        bool Playing,
        bool Shuffle,
        string Repeat,
        MusicPlayerContext? Context);
}
