using Prismedia.Application.Entities;
using Prismedia.Application.Playback;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Media;
using Prismedia.Contracts.Playback;
using Prismedia.Contracts.Settings;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class MusicPlayerStateServiceTests {
    [Fact]
    public async Task SaveThenGetRoundTripsQueueAndPlayerSettings() {
        var track1 = Guid.NewGuid();
        var track2 = Guid.NewGuid();
        var settings = new InMemorySettingsPersistence();
        var entities = new FakeEntityReadService(track1, track2);
        var service = new MusicPlayerStateService(settings, entities);

        await service.SaveAsync(new UpdateMusicPlayerStateRequest(
            QueueTrackIds: [track1, track2],
            Order: [1, 0],
            Position: 1,
            Playing: true,
            Shuffle: true,
            Repeat: MusicPlayerRepeatMode.One,
            Volume: 0.4,
            Muted: true,
            Collapsed: true,
            CollapsedSide: MusicPlayerMiniSide.Right,
            Context: new MusicPlayerContext(null, "Album", null, "Artist", "/cover.jpg", null)),
            CancellationToken.None);

        var loaded = await service.GetAsync(CancellationToken.None);

        Assert.Equal([track1, track2], loaded.Tracks.Select(track => track.Id));
        Assert.Equal([1, 0], loaded.Order);
        Assert.Equal(1, loaded.Position);
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
        var existing = Guid.NewGuid();
        var deleted = Guid.NewGuid();
        var settings = new InMemorySettingsPersistence();
        var entities = new FakeEntityReadService(existing);
        var service = new MusicPlayerStateService(settings, entities);

        await service.SaveAsync(new UpdateMusicPlayerStateRequest(
            QueueTrackIds: [existing, deleted],
            Order: [1, 0],
            Position: 0,
            Playing: true,
            Shuffle: true,
            Repeat: MusicPlayerRepeatMode.All,
            Volume: 1.5,
            Muted: false,
            Collapsed: false,
            CollapsedSide: MusicPlayerMiniSide.Left,
            Context: null),
            CancellationToken.None);

        var loaded = await service.GetAsync(CancellationToken.None);

        var track = Assert.Single(loaded.Tracks);
        Assert.Equal(existing, track.Id);
        Assert.Equal([0], loaded.Order);
        Assert.Equal(0, loaded.Position);
        Assert.Equal(1, loaded.Volume);
    }

    [Fact]
    public async Task EmptyQueueClearsPersistedState() {
        var track = Guid.NewGuid();
        var settings = new InMemorySettingsPersistence();
        var service = new MusicPlayerStateService(settings, new FakeEntityReadService(track));

        await service.SaveAsync(new UpdateMusicPlayerStateRequest(
            QueueTrackIds: [track],
            Order: [0],
            Position: 0,
            Playing: true,
            Shuffle: false,
            Repeat: MusicPlayerRepeatMode.Off,
            Volume: 1,
            Muted: false,
            Collapsed: false,
            CollapsedSide: MusicPlayerMiniSide.Left,
            Context: null),
            CancellationToken.None);
        await service.SaveAsync(new UpdateMusicPlayerStateRequest(
            QueueTrackIds: [],
            Order: [],
            Position: -1,
            Playing: false,
            Shuffle: false,
            Repeat: MusicPlayerRepeatMode.Off,
            Volume: 1,
            Muted: false,
            Collapsed: false,
            CollapsedSide: MusicPlayerMiniSide.Left,
            Context: null),
            CancellationToken.None);

        var loaded = await service.GetAsync(CancellationToken.None);
        Assert.Empty(loaded.Tracks);
        Assert.False(settings.Values.ContainsKey(MusicPlayerStateService.StateKey));
    }

    private sealed class InMemorySettingsPersistence : ISettingsPersistence {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public Task<IReadOnlyDictionary<string, string>> LoadSettingOverridesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(Values);

        public Task SaveSettingOverrideAsync(string key, string valueJson, CancellationToken cancellationToken) {
            Values[key] = valueJson;
            return Task.CompletedTask;
        }

        public Task SaveSettingOverridesAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken) {
            foreach (var (key, value) in values) {
                Values[key] = value;
            }

            return Task.CompletedTask;
        }

        public Task DeleteSettingOverrideAsync(string key, CancellationToken cancellationToken) {
            Values.Remove(key);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LibraryRoot>> ListLibraryRootsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<LibraryRoot?> GetLibraryRootAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<LibraryRoot> AddLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<LibraryRoot> SaveLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> DeleteLibraryRootAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
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
                    Capabilities = [],
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
            bool? orphaned = null) =>
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
