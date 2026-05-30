using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prismedia.Application.Health;

namespace Prismedia.Api.Tests;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>> {
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory) {
        _factory = factory;
    }

    [Fact]
    public async Task HealthEndpointReportsBackendReadiness() {
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync("/api/health");
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("ok", payload.Status);
        Assert.Equal("dotnet", payload.Runtime);
    }

    [Fact]
    public async Task WorkerHealthEndpointReportsOnlineHeartbeat() {
        using var factory = CreateFactory(new WorkerHeartbeatSnapshot(
            "worker-1",
            DateTimeOffset.UtcNow));
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync("/api/health/worker");
        var payload = await response.Content.ReadFromJsonAsync<WorkerHealthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("online", payload.Status);
        Assert.Equal("worker-1", payload.WorkerId);
        Assert.NotNull(payload.LastSeenAt);
    }

    [Fact]
    public async Task WorkerHealthEndpointReportsOfflineWhenHeartbeatIsMissing() {
        using var factory = CreateFactory(null);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync("/api/health/worker");
        var payload = await response.Content.ReadFromJsonAsync<WorkerHealthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("offline", payload.Status);
        Assert.Null(payload.WorkerId);
        Assert.Null(payload.LastSeenAt);
    }

    private static WebApplicationFactory<Program> CreateFactory(WorkerHeartbeatSnapshot? heartbeat) {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
            builder.ConfigureServices(services => {
                services.RemoveAll<IWorkerHeartbeatStore>();
                services.AddSingleton<IWorkerHeartbeatStore>(new FakeWorkerHeartbeatStore(heartbeat));
            });
        });
    }

    private sealed record HealthResponse(string Status, string Runtime);

    private sealed record WorkerHealthResponse(
        string Status,
        string? WorkerId,
        DateTimeOffset? LastSeenAt,
        int StaleAfterSeconds);

    private sealed class FakeWorkerHeartbeatStore(WorkerHeartbeatSnapshot? heartbeat) : IWorkerHeartbeatStore {
        public Task<WorkerHeartbeatSnapshot?> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(heartbeat);

        public Task WriteAsync(
            string workerId,
            DateTimeOffset observedAt,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
