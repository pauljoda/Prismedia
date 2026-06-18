using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prismedia.Application.Backups;
using Prismedia.Application.Health;
using Prismedia.Contracts.Settings;

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

    [Fact]
    public async Task RestoreHealthEndpointReportsPendingWithoutApiKey() {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
            builder.ConfigureServices(services => {
                services.RemoveAll<IDatabaseBackupService>();
                services.AddSingleton<IDatabaseBackupService>(new FakeDatabaseBackupService(PendingRestore: true));
            });
        });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/health/database-restore");
        var payload = await response.Content.ReadFromJsonAsync<DatabaseRestoreStatusResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.True(payload.RestorePending);
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

    private sealed class FakeDatabaseBackupService(bool PendingRestore) : IDatabaseBackupService {
        public Task<DatabaseBackupListResponse> ListAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DatabaseBackupDto> CreateManualBackupAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DatabaseBackupDto> CreateAutomaticBackupAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> IsAutomaticBackupDueAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> PruneExpiredAutomaticBackupsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DatabaseRestoreScheduledResponse> ScheduleRestoreAsync(
            Guid backupId,
            string confirmationText,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> RunPendingRestoreAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DatabaseRestoreStatusResponse> GetRestoreStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new DatabaseRestoreStatusResponse(PendingRestore, RestoreFailed: false, Error: null));

        public Task<bool> HasPendingRestoreAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PendingRestore);
    }
}
