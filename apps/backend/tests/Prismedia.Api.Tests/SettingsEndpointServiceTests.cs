using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Settings;

namespace Prismedia.Api.Tests;

public sealed class SettingsEndpointServiceTests {
    [Fact]
    public async Task SettingsEndpointReadsAndUpdatesThroughService() {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddScoped<ISettingsPersistence, FakeSettingsPersistence>();
                });
            });
        using var client = factory.CreateClient();

        var before = await client.GetFromJsonAsync<SettingsResponse>("/api/settings");
        var after = await client.PatchAsJsonAsync("/api/settings", new SettingsUpdateRequest(true, false));
        var updated = await after.Content.ReadFromJsonAsync<SettingsResponse>();

        Assert.NotNull(before);
        Assert.False(before.HideNsfw);
        Assert.True(before.EnableCastControls);
        Assert.NotNull(updated);
        Assert.True(updated.HideNsfw);
        Assert.False(updated.EnableCastControls);
    }

    [Fact]
    public async Task LibrarySettingsEndpointsUseSettingsService() {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddScoped<ISettingsPersistence, FakeSettingsPersistence>();
                });
            });
        using var client = factory.CreateClient();

        var config = await client.GetFromJsonAsync<LibraryConfigResponse>("/api/settings/library");
        var updated = await client.PutAsJsonAsync(
            "/api/settings/library",
            new LibrarySettingsUpdateRequest(
                null, 15, null, null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null, null, null, null, null,
                null, null, null));
        var root = await client.PostAsJsonAsync(
            "/api/libraries",
            new LibraryRootCreateRequest("/media/videos", "Videos", null, null, null, null, null, null, null));

        Assert.NotNull(config);
        Assert.Single(config.Roots);
        Assert.True(updated.IsSuccessStatusCode);
        Assert.True(root.IsSuccessStatusCode);
    }

    private sealed class FakeSettingsPersistence : ISettingsPersistence {
        private static readonly Guid SettingsId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid RootId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        private LibrarySettings _settings = SampleSettings();
        private readonly Dictionary<Guid, LibraryRoot> _roots = new() { [RootId] = SampleRoot() };

        public Task<LibrarySettings> GetLibrarySettingsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_settings);

        public Task<LibrarySettings> SaveLibrarySettingsAsync(LibrarySettings state, CancellationToken cancellationToken) {
            _settings = state with { UpdatedAt = DateTimeOffset.UtcNow };
            return Task.FromResult(_settings);
        }

        public Task<IReadOnlyList<LibraryRoot>> ListLibraryRootsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LibraryRoot>>(_roots.Values.ToArray());

        public Task<LibraryRoot?> GetLibraryRootAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<LibraryRoot?>(_roots.GetValueOrDefault(id));

        public Task<LibraryRoot> AddLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken) {
            _roots[state.Id] = state;
            return Task.FromResult(state);
        }

        public Task<LibraryRoot> SaveLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken) {
            _roots[state.Id] = state;
            return Task.FromResult(state);
        }

        public Task<bool> DeleteLibraryRootAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_roots.Remove(id));

        private static LibrarySettings SampleSettings() =>
            new(
                SettingsId,
                false,
                60,
                true,
                true,
                false,
                true,
                true,
                10,
                8,
                2,
                2,
                1,
                false,
                true,
                false,
                "en,eng",
                "en,eng,en-US",
                "stylized",
                1,
                88,
                1,
                "direct",
                true,
                "Software",
                "ffmpeg",
                "/dev/dri/renderD128",
                false,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch);

        private static LibraryRoot SampleRoot() =>
            new(
                RootId,
                "/media/videos",
                "Videos",
                true,
                true,
                true,
                false,
                false,
                false,
                false,
                null,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch);
    }
}
