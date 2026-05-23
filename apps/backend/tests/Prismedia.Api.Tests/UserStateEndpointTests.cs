using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Prismedia.Application.UserState;

namespace Prismedia.Api.Tests;

public sealed class UserStateEndpointTests : IClassFixture<WebApplicationFactory<Program>> {
    private readonly WebApplicationFactory<Program> _factory;

    public UserStateEndpointTests(WebApplicationFactory<Program> factory) {
        _factory = factory;
    }

    [Fact]
    public async Task UpdateCheckEndpointReturnsNonBlockingStatus() {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/update-check");
        var payload = await response.Content.ReadFromJsonAsync<UpdateCheckResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("unknown", payload.Status);
        Assert.False(payload.UpdateAvailable);
    }

    [Fact]
    public async Task PlaylistSessionEndpointReturnsJsonNullWhenEmpty() {
        using var factory = _factory.WithWebHostBuilder(builder => {
            builder.ConfigureServices(services => {
                services.AddScoped<IUserStatePersistence, FakeUserStatePersistence>();
            });
        });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/playlist-session");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("null", body);
    }

    private sealed record UpdateCheckResponse(string Status, bool UpdateAvailable);

    private sealed class FakeUserStatePersistence : IUserStatePersistence {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(_values.GetValueOrDefault(key));

        public Task SaveAsync(string key, string valueJson, CancellationToken cancellationToken) {
            _values[key] = valueJson;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken) {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }
}
