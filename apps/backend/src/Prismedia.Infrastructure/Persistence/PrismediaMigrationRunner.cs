using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Prismedia.Infrastructure.Persistence;

/// <summary>
/// Applies and waits for EF Core migrations during process startup. Startup is made resilient
/// to a database that is not yet accepting connections — common on first boot when PostgreSQL
/// and the .NET processes start together — by retrying with exponential backoff instead of
/// throwing and terminating the process.
/// </summary>
public static class PrismediaMigrationRunner {
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Applies pending EF Core migrations. This is the single schema owner path (the API).
    /// Waits for the database to accept connections, retrying transient failures with
    /// exponential backoff so a database that is still starting up does not crash the host.
    /// </summary>
    /// <param name="services">Root service provider used to resolve a scoped <see cref="PrismediaDbContext"/>.</param>
    /// <param name="configuration">Configuration used to decide whether migrations should be applied.</param>
    /// <param name="cancellationToken">Token that aborts the retry loop.</param>
    public static async Task ApplyPrismediaMigrationsAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken = default) {
        if (!ShouldApply(configuration)) {
            return;
        }

        var logger = CreateLogger(services);
        await RunWithRetryAsync(
            "apply database migrations",
            logger,
            async () => {
                await using var scope = services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<PrismediaDbContext>();
                await db.Database.MigrateAsync(cancellationToken);
            },
            cancellationToken);
    }

    /// <summary>
    /// Blocks until the database is reachable and all migrations have been applied by the schema
    /// owner, retrying with exponential backoff. Intended for processes that consume the schema
    /// but must not apply it themselves (the worker), so they no longer race the API to migrate a
    /// fresh database and no longer terminate when the database is not yet ready on first boot.
    /// </summary>
    /// <param name="services">Root service provider used to resolve a scoped <see cref="PrismediaDbContext"/>.</param>
    /// <param name="configuration">Configuration used to decide whether a migrated schema is expected.</param>
    /// <param name="cancellationToken">Token that aborts the wait loop.</param>
    public static async Task WaitForDatabaseReadyAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken = default) {
        // When migrations are not expected to be applied in-process (e.g. tests, or an
        // externally managed schema) there is nothing to wait for.
        if (!ShouldApply(configuration)) {
            return;
        }

        var logger = CreateLogger(services);
        await RunWithRetryAsync(
            "wait for database schema",
            logger,
            async () => {
                await using var scope = services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<PrismediaDbContext>();
                if (!await db.Database.CanConnectAsync(cancellationToken)) {
                    throw new InvalidOperationException("Database is not accepting connections yet.");
                }

                var pending = await db.Database.GetPendingMigrationsAsync(cancellationToken);
                if (pending.Any()) {
                    throw new InvalidOperationException(
                        "Database schema has not been migrated by the API yet.");
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="action"/>, retrying on any failure with exponential backoff until it
    /// succeeds, the cancellation token fires, or <see cref="MaxWait"/> elapses (after which the
    /// failure propagates so a supervisor can restart the process).
    /// </summary>
    private static async Task RunWithRetryAsync(
        string operation,
        ILogger logger,
        Func<Task> action,
        CancellationToken cancellationToken) {
        var delay = InitialDelay;
        var deadline = DateTimeOffset.UtcNow + MaxWait;
        var attempt = 0;

        while (true) {
            attempt++;
            try {
                await action();
                if (attempt > 1) {
                    logger.LogInformation(
                        "Succeeded to {Operation} after {Attempts} attempt(s).", operation, attempt);
                }
                return;
            } catch (Exception ex) when (
                DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested) {
                logger.LogWarning(
                    "Could not {Operation} (attempt {Attempt}): {Message}. Retrying in {Delay:n0}s.",
                    operation, attempt, ex.Message, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxDelay.Ticks));
            }
        }
    }

    private static ILogger CreateLogger(IServiceProvider services) =>
        services.GetService<ILoggerFactory>()?.CreateLogger("Prismedia.Migrations")
        ?? NullLogger.Instance;

    private static bool ShouldApply(IConfiguration configuration) {
        if (AppDomain.CurrentDomain
            .GetAssemblies()
            .Any(assembly => assembly.GetName().Name == "Microsoft.AspNetCore.Mvc.Testing")) {
            return false;
        }

        var configured = configuration["Prismedia:ApplyMigrations"];
        return configured is null || bool.TryParse(configured, out var enabled) && enabled;
    }
}
