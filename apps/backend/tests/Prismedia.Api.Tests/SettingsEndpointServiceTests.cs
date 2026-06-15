using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Settings;

namespace Prismedia.Api.Tests;

public sealed class SettingsEndpointServiceTests {
    [Fact]
    public async Task SettingsCatalogEndpointReadsAndUpdatesThroughService() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        var catalog = await client.GetFromJsonAsync<SettingsCatalogResponse>("/api/settings");
        var updatedResponse = await client.PatchAsJsonAsync(
            $"/api/settings/{AppSettingKeys.JobsBackgroundConcurrency}",
            new SettingUpdateRequest(JsonSerializer.SerializeToElement(6)));
        var updated = await updatedResponse.Content.ReadFromJsonAsync<SettingDescriptor>();
        var values = await client.GetFromJsonAsync<SettingsValuesResponse>(
            $"/api/settings/values?keys={Uri.EscapeDataString(AppSettingKeys.JobsBackgroundConcurrency)}");

        Assert.NotNull(catalog);
        Assert.Contains(catalog.Groups.SelectMany(group => group.Settings), setting =>
            setting.Key == AppSettingKeys.VisibilityDefaultMode &&
            setting.Value.GetString() == "off" &&
            setting.IsDefault);
        Assert.True(updatedResponse.IsSuccessStatusCode);
        Assert.NotNull(updated);
        Assert.Equal(6, updated.Value.GetInt32());
        Assert.False(updated.IsDefault);
        Assert.NotNull(values);
        Assert.Equal(6, values.Values[AppSettingKeys.JobsBackgroundConcurrency].GetInt32());
    }

    [Fact]
    public async Task SettingsBatchAndResetEndpointsUseCentralRegistry() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        var batch = await client.PatchAsJsonAsync(
            "/api/settings",
            new SettingsBatchUpdateRequest(new Dictionary<string, JsonElement> {
                [AppSettingKeys.PlaybackDefaultMode] = JsonSerializer.SerializeToElement("hls"),
                [AppSettingKeys.PlaybackAudioPreferredLanguages] =
                    JsonSerializer.SerializeToElement(new[] { "ja", "jpn" }),
            }));
        var reset = await client.DeleteAsync($"/api/settings/{AppSettingKeys.PlaybackDefaultMode}");
        var defaulted = await reset.Content.ReadFromJsonAsync<SettingDescriptor>();

        Assert.True(batch.IsSuccessStatusCode);
        Assert.True(reset.IsSuccessStatusCode);
        Assert.NotNull(defaulted);
        Assert.Equal("direct", defaulted.Value.GetString());
        Assert.True(defaulted.IsDefault);
    }

    [Fact]
    public async Task SettingsEndpointsReturnProblemDetailsForUnknownAndInvalidKeys() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        var unknown = await client.GetAsync("/api/settings/not.real");
        var invalid = await client.PatchAsJsonAsync(
            $"/api/settings/{AppSettingKeys.JobsBackgroundConcurrency}",
            new SettingUpdateRequest(JsonSerializer.SerializeToElement(99)));
        var unknownJson = await unknown.Content.ReadFromJsonAsync<JsonElement>();
        var invalidJson = await invalid.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("setting_not_found", unknownJson.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("setting_invalid", invalidJson.GetProperty("code").GetString());
    }

    [Fact]
    public async Task LibraryConfigPayloadIncludesCatalogAndWatchedRoots() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        var config = await client.GetFromJsonAsync<LibraryConfigResponse>("/api/settings/library");
        var roots = await client.GetFromJsonAsync<IReadOnlyList<LibraryRoot>>("/api/libraries");
        var root = await client.PostAsJsonAsync(
            "/api/libraries",
            new LibraryRootCreateRequest("/media/series", "Series", false, null, null, null, null, null, null));

        Assert.NotNull(config);
        Assert.Single(config.Roots);
        Assert.NotEmpty(config.Settings.Groups);
        Assert.NotNull(roots);
        Assert.Single(roots);
        Assert.True(root.IsSuccessStatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton<ISettingsPersistence, FakeSettingsPersistence>();
                });
            })
            .WithTestAuth();

    private sealed class FakeSettingsPersistence : ISettingsPersistence {
        private static readonly Guid RootId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        private readonly Dictionary<string, string> _settings = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, LibraryRoot> _roots = new() { [RootId] = SampleRoot() };

        public Task<IReadOnlyDictionary<string, string>> LoadSettingOverridesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(_settings, StringComparer.Ordinal));

        public Task SaveSettingOverrideAsync(string key, string valueJson, CancellationToken cancellationToken) {
            _settings[key] = valueJson;
            return Task.CompletedTask;
        }

        public Task SaveSettingOverridesAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken) {
            foreach (var (key, valueJson) in values) {
                _settings[key] = valueJson;
            }

            return Task.CompletedTask;
        }

        public Task ReplaceSettingOverridesAsync(
            IReadOnlyDictionary<string, string> upserts,
            IReadOnlyCollection<string> deletes,
            CancellationToken cancellationToken) {
            foreach (var key in deletes) {
                _settings.Remove(key);
            }

            foreach (var (key, valueJson) in upserts) {
                _settings[key] = valueJson;
            }

            return Task.CompletedTask;
        }

        public Task DeleteSettingOverrideAsync(string key, CancellationToken cancellationToken) {
            _settings.Remove(key);
            return Task.CompletedTask;
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
