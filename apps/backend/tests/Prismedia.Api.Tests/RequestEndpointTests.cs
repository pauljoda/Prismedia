using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Serialization;

namespace Prismedia.Api.Tests;

public sealed class RequestEndpointTests {
    [Fact]
    public async Task RequestEndpointsManageServicesSearchAndSubmit() {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton<IRequestServiceInstanceStore, FakeRequestServiceInstanceStore>();
                    services.AddSingleton<IRequestHistoryStore, FakeRequestHistoryStore>();
                    services.AddSingleton<FakeRequestProviderClient>();
                    services.AddSingleton<IRequestProviderClient>(provider => provider.GetRequiredService<FakeRequestProviderClient>());
                    services.AddSingleton<IRequestProviderClientFactory, FakeRequestProviderClientFactory>();
                });
            })
            .WithTestAuth();
        using var client = factory.CreateAuthenticatedClient();
        var jsonOptions = new System.Text.Json.JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        };
        jsonOptions.Converters.Add(new CodecJsonConverterFactory());

        var save = await client.PostAsJsonAsync("/api/requests/services", new RequestServiceInstanceSaveRequest(
            null,
            RequestProviderKind.Radarr,
            "Movies",
            "http://radarr.test",
            "secret",
            "/movies",
            4,
            null,
            RequestMinimumAvailability.Released,
            [],
            true,
            false),
            jsonOptions);
        var saveText = await save.Content.ReadAsStringAsync();
        Assert.True(save.IsSuccessStatusCode, saveText);
        var service = System.Text.Json.JsonSerializer.Deserialize<RequestServiceInstanceSummary>(saveText, jsonOptions);
        var services = await client.GetFromJsonAsync<IReadOnlyList<RequestServiceInstanceSummary>>("/api/requests/services", jsonOptions);
        var test = await client.PostAsJsonAsync("/api/requests/services/test", new RequestServiceTestRequest(
            service!.Id, RequestProviderKind.Radarr, "http://radarr.test", null), jsonOptions);
        var testResult = System.Text.Json.JsonSerializer.Deserialize<RequestServiceTestResponse>(
            await test.Content.ReadAsStringAsync(), jsonOptions);
        var search = await client.GetFromJsonAsync<RequestSearchResponse>("/api/requests/search?query=blade&kinds=movie&sources=radarr", jsonOptions);
        var detailResponse = await client.GetAsync($"/api/requests/details/radarr/movie/424?serviceId={service.Id}");
        var detailText = await detailResponse.Content.ReadAsStringAsync();
        Assert.True(detailResponse.IsSuccessStatusCode, detailText);
        var detail = System.Text.Json.JsonSerializer.Deserialize<RequestDetailResponse>(detailText, jsonOptions);
        var submit = await client.PostAsJsonAsync("/api/requests", new RequestSubmitRequest(
            service.Id,
            RequestProviderKind.Radarr,
            RequestMediaKind.Movie,
            "424",
            "Blade Runner",
            4,
            "/movies",
            null,
            true,
            true,
            []),
            jsonOptions);
        var submitted = await submit.Content.ReadFromJsonAsync<RequestSubmitResponse>(jsonOptions);

        Assert.NotNull(services);
        Assert.Single(services);
        Assert.True(test.IsSuccessStatusCode);
        Assert.NotNull(testResult);
        Assert.True(testResult.Connected);
        Assert.Equal("HD", Assert.Single(testResult.Options!.QualityProfiles).Name);
        Assert.Equal("prismedia", Assert.Single(testResult.Options.Tags).Name);
        Assert.NotNull(search);
        Assert.Equal("Blade Runner", Assert.Single(search.Results).Title);
        Assert.NotNull(detail);
        Assert.Equal("Blade Runner", detail.Title);
        Assert.True(submit.IsSuccessStatusCode);
        Assert.True(submitted!.Submitted);
    }

    [Fact]
    public async Task SaveRequestServiceRejectsMissingNameAndRelativeBaseUrl() {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton<IRequestServiceInstanceStore, FakeRequestServiceInstanceStore>();
                    services.AddSingleton<IRequestHistoryStore, FakeRequestHistoryStore>();
                    services.AddSingleton<FakeRequestProviderClient>();
                    services.AddSingleton<IRequestProviderClient>(provider => provider.GetRequiredService<FakeRequestProviderClient>());
                    services.AddSingleton<IRequestProviderClientFactory, FakeRequestProviderClientFactory>();
                });
            })
            .WithTestAuth();
        using var client = factory.CreateAuthenticatedClient();
        var jsonOptions = new System.Text.Json.JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        };
        jsonOptions.Converters.Add(new CodecJsonConverterFactory());

        var missingName = await client.PostAsJsonAsync("/api/requests/services", new RequestServiceInstanceSaveRequest(
            null, RequestProviderKind.Radarr, "  ", "http://radarr.test", "secret", null, null, null, RequestMinimumAvailability.Released, [], true, false), jsonOptions);
        var relativeUrl = await client.PostAsJsonAsync("/api/requests/services", new RequestServiceInstanceSaveRequest(
            null, RequestProviderKind.Radarr, "Movies", "radarr:7878", "secret", null, null, null, RequestMinimumAvailability.Released, [], true, false), jsonOptions);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, missingName.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, relativeUrl.StatusCode);
    }

    [Fact]
    public async Task RequestDetailReturnsNotFoundWhenServiceDoesNotMatchRequestedSource() {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton<IRequestServiceInstanceStore, FakeRequestServiceInstanceStore>();
                    services.AddSingleton<IRequestHistoryStore, FakeRequestHistoryStore>();
                    services.AddSingleton<FakeRequestProviderClient>();
                    services.AddSingleton<IRequestProviderClient>(provider => provider.GetRequiredService<FakeRequestProviderClient>());
                    services.AddSingleton<IRequestProviderClientFactory, FakeRequestProviderClientFactory>();
                });
            })
            .WithTestAuth();
        using var client = factory.CreateAuthenticatedClient();
        var jsonOptions = new System.Text.Json.JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        };
        jsonOptions.Converters.Add(new CodecJsonConverterFactory());
        var save = await client.PostAsJsonAsync("/api/requests/services", new RequestServiceInstanceSaveRequest(
            null,
            RequestProviderKind.Radarr,
            "Movies",
            "http://radarr.test",
            "secret",
            "/movies",
            4,
            null,
            RequestMinimumAvailability.Released,
            [],
            true,
            false),
            jsonOptions);
        var service = await save.Content.ReadFromJsonAsync<RequestServiceInstanceSummary>(jsonOptions);

        var detail = await client.GetAsync($"/api/requests/details/sonarr/series/79169?serviceId={service!.Id}");
        var submit = await client.PostAsJsonAsync("/api/requests", new RequestSubmitRequest(
            service.Id,
            RequestProviderKind.Sonarr,
            RequestMediaKind.Series,
            "79169",
            "Twin Peaks",
            4,
            "/series",
            null,
            true,
            true,
            []),
            jsonOptions);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, detail.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, submit.StatusCode);
    }

    private sealed class FakeRequestProviderClientFactory(FakeRequestProviderClient client) : IRequestProviderClientFactory {
        public IRequestProviderClient Get(RequestProviderKind kind) => client;
    }

    private sealed class FakeRequestServiceInstanceStore : IRequestServiceInstanceStore {
        private readonly Dictionary<Guid, RequestServiceInstanceDetail> _items = new();

        public Task<IReadOnlyList<RequestServiceInstanceSummary>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RequestServiceInstanceSummary>>(_items.Values.Select(ToSummary).ToArray());

        public Task<IReadOnlyList<RequestServiceInstanceDetail>> ListDetailsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RequestServiceInstanceDetail>>(_items.Values.ToArray());

        public Task<RequestServiceInstanceDetail?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_items.GetValueOrDefault(id));

        public Task<RequestServiceInstanceSummary> SaveAsync(RequestServiceInstanceSaveRequest request, CancellationToken cancellationToken) {
            var id = request.Id ?? Guid.NewGuid();
            var detail = new RequestServiceInstanceDetail(
                id,
                request.Kind,
                request.DisplayName,
                request.BaseUrl,
                true,
                request.DefaultRootFolderPath,
                request.DefaultQualityProfileId,
                request.DefaultMetadataProfileId,
                request.MinimumAvailability,
                request.DefaultTagIds,
                request.SearchOnRequest,
                !string.IsNullOrWhiteSpace(request.ApiKey),
                request.ApiKey);
            _items[id] = detail;
            return Task.FromResult(ToSummary(detail));
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_items.Remove(id));

        private static RequestServiceInstanceSummary ToSummary(RequestServiceInstanceDetail detail) =>
            new(detail.Id, detail.Kind, detail.DisplayName, detail.BaseUrl, detail.IsDefault, detail.DefaultRootFolderPath,
                detail.DefaultQualityProfileId, detail.DefaultMetadataProfileId, detail.MinimumAvailability, detail.DefaultTagIds,
                detail.SearchOnRequest, detail.HasApiKey);
    }

    private sealed class FakeRequestHistoryStore : IRequestHistoryStore {
        private readonly List<RequestHistoryEntry> _entries = [];

        public Task AddAsync(RequestHistoryAddRequest request, CancellationToken cancellationToken) {
            var now = DateTimeOffset.UtcNow;
            _entries.Add(new RequestHistoryEntry(Guid.NewGuid(), request.ServiceInstanceId, request.ServiceName, request.Source,
                request.Kind, request.ExternalId, request.Title, request.Subtitle, request.Year, request.PosterUrl,
                request.UpstreamId, request.Monitored, request.SelectedChildIds.Count, RequestHistoryStatus.Submitted, null, now, now));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RequestHistoryEntry>> ListAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RequestHistoryEntry>>(_entries.Take(limit).ToArray());

        public Task UpdateStatusesAsync(IReadOnlyList<RequestHistoryStatusUpdate> updates, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_entries.RemoveAll(entry => entry.Id == id) > 0);
    }

    private sealed class FakeRequestProviderClient : IRequestProviderClient {
        public RequestProviderKind Kind => RequestProviderKind.Radarr;

        public Task<IReadOnlyList<RequestSearchResult>> SearchAsync(RequestServiceInstanceDetail instance, string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RequestSearchResult>>([
                new(instance.Id, RequestProviderKind.Radarr, RequestMediaKind.Movie, "424", "Blade Runner", null, 1982, null, null, null, null, null, null, null, [], false, null, null, true)
            ]);

        public Task<RequestDetailResponse> GetDetailAsync(RequestServiceInstanceDetail instance, RequestMediaKind kind, string externalId, CancellationToken cancellationToken) =>
            Task.FromResult(new RequestDetailResponse(RequestProviderKind.Radarr, RequestMediaKind.Movie, externalId, "Blade Runner", null, 1982, null, null, null, null, null, null, null, [], [], [], [], [], false, null, null, new RequestServiceOptionsResponse([], [], [], [])));

        public Task<RequestSubmitResponse> SubmitAsync(RequestServiceInstanceDetail instance, RequestDetailResponse detail, RequestSubmitRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new RequestSubmitResponse(true, "12", null));

        public Task<IReadOnlyList<RequestStatusResult>> GetStatusesAsync(RequestServiceInstanceDetail instance, IReadOnlyList<RequestStatusProbe> probes, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RequestStatusResult>>(
                probes.Select(probe => new RequestStatusResult(probe.HistoryId, RequestHistoryStatus.Available, null, probe.UpstreamId)).ToArray());

        public Task<RequestConnectionTestResponse> TestAsync(RequestServiceInstanceDetail instance, CancellationToken cancellationToken) =>
            Task.FromResult(new RequestConnectionTestResponse(true, "Connected"));

        public Task<RequestServiceOptionsResponse> GetOptionsAsync(RequestServiceInstanceDetail instance, CancellationToken cancellationToken) =>
            Task.FromResult(new RequestServiceOptionsResponse(
                [new RequestServiceOption("4", "HD", null)],
                [new RequestServiceOption("/movies", "/movies", "/movies")],
                [],
                [new RequestServiceOption("1", "prismedia", null)]));
    }
}
