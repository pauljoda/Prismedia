using Prismedia.Application.Entities;
using Prismedia.Application.Playback;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Media;
using Prismedia.Contracts.Playback;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class MusicPlayerStateServiceTests {
    [Fact]
    public async Task SaveThenGetRoundTripsQueueAndPlayerSettings() {
        var browserSessionId = Guid.NewGuid();
        var track1 = Guid.NewGuid();
        var track2 = Guid.NewGuid();
        var settings = new InMemoryBrowserSessionPersistence();
        var entities = new FakeEntityReadService(track1, track2);
        var service = new MusicPlayerStateService(settings, entities);

        await service.SaveAsync(browserSessionId, new UpdateMusicPlayerStateRequest(
            QueueTrackIds: [track1, track2],
            Order: [1, 0],
            Position: 1,
            CurrentTime: 37.5,
            Playing: true,
            Shuffle: true,
            Repeat: MusicPlayerRepeatMode.One,
            Volume: 0.4,
            Muted: true,
            Collapsed: true,
            CollapsedSide: MusicPlayerMiniSide.Right,
            Context: new MusicPlayerContext(null, "Album", null, "Artist", "/cover.jpg", null)),
            CancellationToken.None);

        var loaded = await service.GetAsync(browserSessionId, CancellationToken.None);

        Assert.Equal([track1, track2], loaded.Tracks.Select(track => track.Id));
        Assert.Equal([1, 0], loaded.Order);
        Assert.Equal(1, loaded.Position);
        Assert.Equal(37.5, loaded.CurrentTime);
        Assert.True(loaded.Playing);
        Assert.True(loaded.Shuffle);
        Assert.Equal(MusicPlayerRepeatMode.One, loaded.Repeat);
        Assert.Equal(0.4, loaded.Volume);
        Assert.True(loaded.Muted);
        Assert.True(loaded.Collapsed);
        Assert.Equal(MusicPlayerMiniSide.Right, loaded.CollapsedSide);
        Assert.Equal("Album", loaded.Context?.AlbumTitle);
    }

    [Fact]
    public async Task GetFiltersDeletedTracksAndRepairsOrder() {
        var browserSessionId = Guid.NewGuid();
        var existing = Guid.NewGuid();
        var deleted = Guid.NewGuid();
        var settings = new InMemoryBrowserSessionPersistence();
        var entities = new FakeEntityReadService(existing);
        var service = new MusicPlayerStateService(settings, entities);

        await service.SaveAsync(browserSessionId, new UpdateMusicPlayerStateRequest(
            QueueTrackIds: [existing, deleted],
            Order: [1, 0],
            Position: 0,
            CurrentTime: 150,
            Playing: true,
            Shuffle: true,
            Repeat: MusicPlayerRepeatMode.All,
            Volume: 1.5,
            Muted: false,
            Collapsed: false,
            CollapsedSide: MusicPlayerMiniSide.Left,
            Context: null),
            CancellationToken.None);

        var loaded = await service.GetAsync(browserSessionId, CancellationToken.None);

        var track = Assert.Single(loaded.Tracks);
        Assert.Equal(existing, track.Id);
        Assert.Equal([0], loaded.Order);
        Assert.Equal(0, loaded.Position);
        Assert.Equal(100, loaded.CurrentTime);
        Assert.Equal(1, loaded.Volume);
    }

    [Fact]
    public async Task ClearRemovesQueueButKeepsBrowserOutputSettings() {
        var browserSessionId = Guid.NewGuid();
        var track = Guid.NewGuid();
        var settings = new InMemoryBrowserSessionPersistence();
        var service = new MusicPlayerStateService(settings, new FakeEntityReadService(track));

        await service.SaveAsync(browserSessionId, new UpdateMusicPlayerStateRequest(
            QueueTrackIds: [track],
            Order: [0],
            Position: 0,
            CurrentTime: 12,
            Playing: true,
            Shuffle: false,
            Repeat: MusicPlayerRepeatMode.Off,
            Volume: 0.35,
            Muted: true,
            Collapsed: true,
            CollapsedSide: MusicPlayerMiniSide.Right,
            Context: null),
            CancellationToken.None);
        await service.ClearAsync(browserSessionId, CancellationToken.None);

        var loaded = await service.GetAsync(browserSessionId, CancellationToken.None);
        Assert.Empty(loaded.Tracks);
        Assert.Equal(0.35, loaded.Volume);
        Assert.True(loaded.Muted);
        Assert.True(loaded.Collapsed);
        Assert.Equal(MusicPlayerMiniSide.Right, loaded.CollapsedSide);
        Assert.DoesNotContain(BrowserSessionConstants.AudioPlaybackStateSettingKey, settings.ValuesFor(browserSessionId).Keys);
        Assert.Contains(BrowserSessionConstants.AudioOutputSettingKey, settings.ValuesFor(browserSessionId).Keys);
    }

    [Fact]
    public async Task StateIsIsolatedByBrowserSession() {
        var session1 = Guid.NewGuid();
        var session2 = Guid.NewGuid();
        var track1 = Guid.NewGuid();
        var track2 = Guid.NewGuid();
        var settings = new InMemoryBrowserSessionPersistence();
        var service = new MusicPlayerStateService(settings, new FakeEntityReadService(track1, track2));

        await service.SaveAsync(session1, Request(track1, 0.2), CancellationToken.None);
        await service.SaveAsync(session2, Request(track2, 0.8), CancellationToken.None);

        var loaded1 = await service.GetAsync(session1, CancellationToken.None);
        var loaded2 = await service.GetAsync(session2, CancellationToken.None);

        Assert.Equal(track1, Assert.Single(loaded1.Tracks).Id);
        Assert.Equal(0.2, loaded1.Volume);
        Assert.Equal(track2, Assert.Single(loaded2.Tracks).Id);
        Assert.Equal(0.8, loaded2.Volume);
    }

    private static UpdateMusicPlayerStateRequest Request(Guid trackId, double volume) =>
        new(
            QueueTrackIds: [trackId],
            Order: [0],
            Position: 0,
            CurrentTime: 0,
            Playing: false,
            Shuffle: false,
            Repeat: MusicPlayerRepeatMode.Off,
            Volume: volume,
            Muted: false,
            Collapsed: false,
            CollapsedSide: MusicPlayerMiniSide.Left,
            Context: null);

    private sealed class InMemoryBrowserSessionPersistence : IBrowserSessionPersistence {
        private readonly Dictionary<Guid, Dictionary<string, string>> _values = new();
        private readonly Dictionary<Guid, BrowserSessionState> _sessions = new();

        public IReadOnlyDictionary<string, string> ValuesFor(Guid sessionId) =>
            _values.TryGetValue(sessionId, out var values)
                ? values
                : new Dictionary<string, string>(StringComparer.Ordinal);

        public Task<BrowserSessionState> EnsureAsync(
            Guid? requestedSessionId,
            DateTimeOffset now,
            DateTimeOffset staleBefore,
            CancellationToken cancellationToken) {
            foreach (var stale in _sessions.Where(pair => pair.Value.LastSeenAt < staleBefore).Select(pair => pair.Key).ToArray()) {
                _sessions.Remove(stale);
                _values.Remove(stale);
            }

            if (requestedSessionId is { } id && _sessions.TryGetValue(id, out var existing)) {
                var refreshed = existing with { LastSeenAt = now };
                _sessions[id] = refreshed;
                return Task.FromResult(refreshed);
            }

            var sessionId = Guid.NewGuid();
            var created = new BrowserSessionState(sessionId, now, now);
            _sessions[sessionId] = created;
            return Task.FromResult(created);
        }

        public Task<IReadOnlyDictionary<string, string>> LoadSettingsAsync(
            Guid sessionId,
            IReadOnlyCollection<string> keys,
            CancellationToken cancellationToken) {
            var sessionValues = ValuesFor(sessionId);
            var values = sessionValues
                .Where(pair => keys.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            return Task.FromResult<IReadOnlyDictionary<string, string>>(values);
        }

        public Task ReplaceSettingsAsync(
            Guid sessionId,
            IReadOnlyDictionary<string, string> upserts,
            IReadOnlyCollection<string> deletes,
            DateTimeOffset now,
            CancellationToken cancellationToken) {
            if (!_values.TryGetValue(sessionId, out var values)) {
                values = new Dictionary<string, string>(StringComparer.Ordinal);
                _values[sessionId] = values;
            }

            foreach (var key in deletes) {
                values.Remove(key);
            }

            foreach (var (key, value) in upserts) {
                values[key] = value;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeEntityReadService : IEntityReadService {
        private readonly IReadOnlyDictionary<Guid, AudioTrackDetail> _tracks;

        public FakeEntityReadService(params Guid[] trackIds) {
            _tracks = trackIds.ToDictionary(
                id => id,
                id => new AudioTrackDetail {
                    Id = id,
                    Kind = EntityKind.AudioTrack,
                    Title = $"Track {id:N}",
                    ParentEntityId = null,
                    SortOrder = null,
                    Capabilities = [new TechnicalCapability(TimeSpan.FromSeconds(100), null, null, null, null, null, null, null, null, null)],
                    ChildrenByKind = [],
                    Relationships = [],
                    EmbeddedArtist = null,
                    EmbeddedAlbum = null,
                });
        }

        public Task<EntityListResponse> ListAsync(
            string? kind,
            string? query,
            string? cursor,
            bool? hideNsfw,
            int? limit,
            CancellationToken cancellationToken,
            Guid? referencedBy = null,
            string? relationshipCode = null,
            string? sort = null,
            string? sortDir = null,
            int? seed = null,
            bool? favorite = null,
            bool? organized = null,
            int? ratingMin = null,
            int? ratingMax = null,
            bool? unrated = null,
            string? status = null,
            string? bookType = null,
            string? bookFormat = null,
            bool? nsfw = null,
            bool? hasFile = null,
            bool? played = null,
            bool? orphaned = null,
        bool? wanted = null) =>
            throw new NotSupportedException();

        public Task<EntityCard?> GetAsync(Guid id, bool hideNsfw, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<EntityThumbnailBatchResponse> GetThumbnailsAsync(
            IReadOnlyList<Guid> ids,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IEntityCard?> GetDetailAsync(Guid id, string kind, bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IEntityCard?>(_tracks.GetValueOrDefault(id));
    }
}
