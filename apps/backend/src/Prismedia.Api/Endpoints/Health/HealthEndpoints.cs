using Prismedia.Application.Backups;
using Prismedia.Application.Health;
using Prismedia.Contracts.Settings;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

public static class HealthEndpoints {
    private static readonly TimeSpan WorkerHeartbeatStaleAfter = TimeSpan.FromSeconds(45);

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapGet("/api/health", () =>
            Results.Ok(new HealthResponse("ok", "dotnet")))
            .WithName("GetHealth")
            .WithSummary("Reports that the Prismedia .NET backend is ready to accept requests.")
            .Produces<HealthResponse>();

        routes.MapGet("/api/health/worker", async (
            IWorkerHeartbeatStore heartbeatStore,
            CancellationToken cancellationToken) => {
            var heartbeat = await heartbeatStore.ReadAsync(cancellationToken);
            var isOnline = heartbeat is not null &&
                DateTimeOffset.UtcNow - heartbeat.ObservedAt <= WorkerHeartbeatStaleAfter;

            return Results.Ok(new WorkerHealthResponse(
                isOnline ? "online" : "offline",
                heartbeat?.WorkerId,
                heartbeat?.ObservedAt,
                (int)WorkerHeartbeatStaleAfter.TotalSeconds));
        })
            .WithName("GetWorkerHealth")
            .WithSummary("Reports whether the Prismedia worker has published a fresh heartbeat.")
            .Produces<WorkerHealthResponse>();

        routes.MapGet("/api/health/database-restore", async (
            IDatabaseBackupService backups,
            CancellationToken cancellationToken) =>
            Results.Ok(await backups.GetRestoreStatusAsync(cancellationToken)))
            .WithName("GetDatabaseRestoreHealth")
            .WithSummary("Reports whether a destructive database restore is pending or failed.")
            .Produces<DatabaseRestoreStatusResponse>();

        return routes;
    }
}
