using Prismedia.Application.Entities;
using Prismedia.Application.Playback;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Media;
using Prismedia.Contracts.Playback;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class MusicPlayerStateServiceTests {
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
    public async Task GetFiltersWantedTrackPlaceholdersAndRepairsOrder() {
        var browserSessionId = Guid.NewGuid();
        var playable = Guid.NewGuid();
        var wanted = Guid.NewGuid();
        var settings = new InMemoryBrowserSessionPersistence();
        var entities = new FakeEntityReadService(new HashSet<Guid> { wanted }, playable, wanted);
        var service = new MusicPlayerStateService(settings, entities);

        await service.SaveAsync(browserSessionId, new UpdateMusicPlayerStateRequest(
            QueueTrackIds: [wanted, playable],
            Order: [0, 1],
            Position: 0,
            CurrentTime: 12,
            Playing: true,
            Shuffle: false,
            Repeat: MusicPlayerRepeatMode.Off,
            Volume: 1,
            Muted: false,
            Collapsed: false,
            CollapsedSide: MusicPlayerMiniSide.Left,
            Context: null),
            CancellationToken.None);

        var loaded = await service.GetAsync(browserSessionId, CancellationToken.None);

        Assert.Equal(playable, Assert.Single(loaded.Tracks).Id);
        Assert.Equal([0], loaded.Order);
        Assert.Equal(0, loaded.Position);
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

    [Fact]
    public async Task MappedPlaybackOwnerSurvivesQueueRestore() {
        var browserSessionId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var settings = new InMemoryBrowserSessionPersistence();
        var service = new MusicPlayerStateService(settings, new FakeEntityReadService(trackId));
        var request = Request(trackId, 1) with {
            Context = new MusicPlayerContext(
                AlbumId: null,
                AlbumTitle: null,
                ArtistId: null,
                ArtistName: null,
                CoverUrl: null,
                AlbumCoverUrls: null,
                PlaybackOwnerEntityId: bookId,
                PlaybackOwnerTitle: "Dune",
                PlaybackOwnerEntityKind: EntityKind.Book,
                ProgressMappings: [
                    new PlaybackProgressMapping(
                        trackId,
                        bookId,
                        ProgressUnit.Cfi,
                        StartIndex: 2000,
                        EndIndex: 4000,
                        Total: 10000,
                        ReaderMode.Paged)
                ],
                PreservesQueueOrder: true,
                SupportsPlaybackRate: true)
        };

        await service.SaveAsync(browserSessionId, request, CancellationToken.None);
        var loaded = await service.GetAsync(browserSessionId, CancellationToken.None);

        Assert.Equal(bookId, loaded.Context?.PlaybackOwnerEntityId);
        Assert.Equal("Dune", loaded.Context?.PlaybackOwnerTitle);
        Assert.Equal(EntityKind.Book, loaded.Context?.PlaybackOwnerEntityKind);
        Assert.True(loaded.Context?.PreservesQueueOrder);
        Assert.True(loaded.Context?.SupportsPlaybackRate);
        var mapping = Assert.Single(loaded.Context?.ProgressMappings ?? []);
        Assert.Equal(trackId, mapping.ItemId);
        Assert.Equal(ProgressUnit.Cfi, mapping.Unit);
        Assert.Equal(2000, mapping.StartIndex);
        Assert.Equal(4000, mapping.EndIndex);
    }

    [Fact]
    public async Task LegacyBookProgressMappingsAreUpgradedOnRestore() {
        var browserSessionId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var settings = new InMemoryBrowserSessionPersistence();
        var service = new MusicPlayerStateService(settings, new FakeEntityReadService(trackId));
        var request = Request(trackId, 1) with {
            Context = new MusicPlayerContext(
                AlbumId: null,
                AlbumTitle: null,
                ArtistId: null,
                ArtistName: null,
                CoverUrl: null,
                AlbumCoverUrls: null,
                PlaybackOwnerEntityId: ownerId,
                ProgressMappings: [
                    new PlaybackProgressMapping(
                        trackId,
                        ownerId,
                        ProgressUnit.Item,
                        StartIndex: 3,
                        EndIndex: 4,
                        Total: 10,
                        Mode: null)
                ])
        };
        await service.SaveAsync(browserSessionId, request, CancellationToken.None);
        var currentJson = settings.ValuesFor(browserSessionId)[BrowserSessionConstants.AudioPlaybackStateSettingKey];
        var legacyJson = currentJson
            .Replace("\"progressMappings\"", "\"bookProgressMappings\"", StringComparison.Ordinal)
            .Replace("\"itemId\"", "\"trackId\"", StringComparison.Ordinal);
        await settings.ReplaceSettingsAsync(
            browserSessionId,
            new Dictionary<string, string> {
                [BrowserSessionConstants.AudioPlaybackStateSettingKey] = legacyJson
            },
            [],
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        var loaded = await service.GetAsync(browserSessionId, CancellationToken.None);

        var mapping = Assert.Single(loaded.Context?.ProgressMappings ?? []);
        Assert.Equal(trackId, mapping.ItemId);
        Assert.Equal(ownerId, mapping.CurrentEntityId);
        Assert.Equal(3, mapping.StartIndex);
    }

    [Fact]
    public async Task SaveAndProgressUpdateDoNotHydrateQueue() {
        var browserSessionId = Guid.NewGuid();
        var track1 = Guid.NewGuid();
        var track2 = Guid.NewGuid();
        var track3 = Guid.NewGuid();
        var settings = new InMemoryBrowserSessionPersistence();
        var entities = new FakeEntityReadService(track1, track2, track3);
        var service = new MusicPlayerStateService(settings, entities);
        var state = Request(track1, 1) with {
            QueueTrackIds = [track1, track2, track3],
            Order = [2, 0, 1],
            Position = 0,
            CurrentTime = 4,
            Playing = true
        };

        await service.SaveAsync(browserSessionId, state, CancellationToken.None);
        var updated = await service.UpdateProgressAsync(
            browserSessionId,
            new UpdateMusicPlayerProgressRequest(track1, Position: 1, CurrentTime: 19, Playing: false),
            CancellationToken.None);

        Assert.True(updated);
        Assert.Equal(0, entities.GetCount);
        Assert.Equal(0, entities.PlaybackBatchCount);

        var loaded = await service.GetAsync(browserSessionId, CancellationToken.None);
        Assert.Equal(0, entities.GetCount);
        Assert.Equal(1, entities.PlaybackBatchCount);
        Assert.Equal(1, loaded.Position);
        Assert.Equal(19, loaded.CurrentTime);
        Assert.False(loaded.Playing);
    }

    [Fact]
    public async Task ProgressUpdateIgnoresTrackThatNoLongerMatchesStoredQueue() {
        var browserSessionId = Guid.NewGuid();
        var currentTrack = Guid.NewGuid();
        var staleTrack = Guid.NewGuid();
        var settings = new InMemoryBrowserSessionPersistence();
        var service = new MusicPlayerStateService(settings, new FakeEntityReadService(currentTrack));
        await service.SaveAsync(
            browserSessionId,
            Request(currentTrack, 1) with { CurrentTime = 4 },
            CancellationToken.None);

        var updated = await service.UpdateProgressAsync(
            browserSessionId,
            new UpdateMusicPlayerProgressRequest(staleTrack, Position: 0, CurrentTime: 99, Playing: true),
            CancellationToken.None);
        var loaded = await service.GetAsync(browserSessionId, CancellationToken.None);

        Assert.False(updated);
        Assert.Equal(4, loaded.CurrentTime);
        Assert.False(loaded.Playing);
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
        private readonly IReadOnlyDictionary<Guid, EntityCard> _tracks;

        public int GetCount { get; private set; }
        public int ThumbnailBatchCount { get; private set; }
        public int PlaybackBatchCount { get; private set; }

        public FakeEntityReadService(params Guid[] trackIds)
            : this(new HashSet<Guid>(), trackIds) {
        }

        public FakeEntityReadService(IReadOnlySet<Guid> wantedTrackIds, params Guid[] trackIds) {
            _tracks = trackIds.ToDictionary(
                id => id,
                id => new EntityCard {
                    Id = id,
                    Kind = EntityKind.AudioTrack,
                    Title = $"Track {id:N}",
                    ParentEntityId = null,
                    SortOrder = null,
                    Capabilities = [
                        new TechnicalCapability(TimeSpan.FromSeconds(100), null, null, null, null, null, null, null, null, null),
                        new FlagsCapability(null, null, null, wantedTrackIds.Contains(id)),
                        new EmbeddedAudioMetadataCapability(null, null)
                    ],
                    ChildrenByKind = [],
                    Relationships = [],
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
            EntityListSort? sort = null,
            EntitySortDirection? sortDirection = null,
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
            bool? engaged = null,
            bool? orphaned = null,
            bool? wanted = null,
            AcquisitionStatus? acquisitionStatus = null) =>
            throw new NotSupportedException();

        public Task<EntityCard?> GetAsync(Guid id, bool hideNsfw, CancellationToken cancellationToken) {
            GetCount += 1;
            return Task.FromResult<EntityCard?>(_tracks.GetValueOrDefault(id));
        }

        public Task<EntityThumbnailBatchResponse> GetThumbnailsAsync(
            IReadOnlyList<Guid> ids,
            bool hideNsfw,
            CancellationToken cancellationToken) {
            ThumbnailBatchCount += 1;
            var items = ids
                .Where(_tracks.ContainsKey)
                .Select(id => {
                    var card = _tracks[id];
                    var technical = card.Capabilities.OfType<TechnicalCapability>().Single();
                    var flags = card.Capabilities.OfType<FlagsCapability>().Single();
                    return new EntityThumbnail(
                        id,
                        EntityKind.AudioTrack,
                        card.Title,
                        card.ParentEntityId,
                        card.SortOrder,
                        null,
                        null,
                        ThumbnailHoverKind.None,
                        null,
                        [],
                        [new EntityThumbnailMeta(EntityThumbnailMetaIcons.Duration, technical.Duration?.ToString() ?? "00:00:00")],
                        null,
                        false,
                        false,
                        false) {
                        IsWanted = flags.IsWanted == true,
                        HasSourceMedia = flags.IsWanted != true
                    };
                })
                .ToArray();
            return Task.FromResult(new EntityThumbnailBatchResponse(items));
        }

        public Task<IReadOnlyList<AudioPlaybackItem>> GetAudioPlaybackItemsAsync(
            IReadOnlyList<Guid> ids,
            bool hideNsfw,
            CancellationToken cancellationToken) {
            PlaybackBatchCount += 1;
            var items = ids
                .Where(_tracks.ContainsKey)
                .Select(id => {
                    var card = _tracks[id];
                    var technical = card.Capabilities.OfType<TechnicalCapability>().Single();
                    var flags = card.Capabilities.OfType<FlagsCapability>().Single();
                    return new AudioPlaybackItem(
                        id,
                        card.Title,
                        card.ParentEntityId,
                        card.SortOrder,
                        false,
                        false,
                        flags.IsWanted == true,
                        flags.IsWanted != true,
                        technical.Duration?.TotalSeconds,
                        technical.BitRate,
                        technical.SampleRate,
                        technical.Channels,
                        technical.Codec,
                        null,
                        null,
                        null,
                        null,
                        null,
                        0,
                        null,
                        DateTimeOffset.UnixEpoch);
                })
                .ToArray();
            return Task.FromResult<IReadOnlyList<AudioPlaybackItem>>(items);
        }

    }
}
