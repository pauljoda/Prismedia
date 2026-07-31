using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Prismedia.Infrastructure.Media.Processing;

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
                var migrator = db.GetService<IMigrator>();
                await ApplyMigrationsUnderSessionLockAsync(
                    db,
                    migrator,
                    scope.ServiceProvider.GetRequiredService<AssetPathService>(),
                    cancellationToken);
            },
            cancellationToken);
    }

    private static async Task ApplyMigrationsUnderSessionLockAsync(
        PrismediaDbContext db,
        IMigrator migrator,
        AssetPathService assets,
        CancellationToken cancellationToken) {
        await ExecuteUnderSessionLockAsync(
            db,
            async () => {
                await ValidateMigrationHistoryAsync(db, cancellationToken);
                var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken))
                    .ToHashSet(StringComparer.Ordinal);
                if (pending.Contains(DirectPlayableMigrationAssetPreparer.MigrationId)) {
                    await migrator.MigrateAsync(
                        DirectPlayableMigrationAssetPreparer.PreviousMigrationId,
                        cancellationToken);
                    await ValidateMigrationHistoryAsync(db, cancellationToken);
                    var pendingAfterPrefixMigration = (await db.Database.GetPendingMigrationsAsync(cancellationToken))
                        .ToHashSet(StringComparer.Ordinal);
                    if (!pendingAfterPrefixMigration.Contains(DirectPlayableMigrationAssetPreparer.MigrationId)) {
                        throw new InvalidOperationException(
                            $"Migration history changed while preparing {DirectPlayableMigrationAssetPreparer.MigrationId}.");
                    }

                    await DirectPlayableMigrationAssetPreparer.PrepareAsync(db, assets, cancellationToken);
                }

                await migrator.MigrateAsync(targetMigration: null, cancellationToken);
            },
            cancellationToken);
    }

    private static async Task ExecuteUnderSessionLockAsync(
        PrismediaDbContext db,
        Func<Task> action,
        CancellationToken cancellationToken) {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var lockAcquired = false;
        try {
            await ExecuteAdvisoryLockAsync(connection, acquire: true, cancellationToken);
            lockAcquired = true;
            await action();
        } finally {
            try {
                if (lockAcquired) {
                    await ExecuteAdvisoryLockAsync(connection, acquire: false, CancellationToken.None);
                }
            } finally {
                await db.Database.CloseConnectionAsync();
            }
        }
    }

    private static async Task ExecuteAdvisoryLockAsync(
        NpgsqlConnection connection,
        bool acquire,
        CancellationToken cancellationToken) {
        var operation = acquire ? "pg_advisory_lock" : "pg_advisory_unlock";
        await using var command = new NpgsqlCommand(
            $"SELECT {operation}(hashtextextended(@lock_name, 0))",
            connection);
        command.Parameters.AddWithValue("lock_name", DirectPlayableMigrationAssetPreparer.AdvisoryLockName);
        await command.ExecuteScalarAsync(cancellationToken);
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
                    throw new DatabaseNotReadyException("Database is not accepting connections yet.");
                }

                await ExecuteUnderSessionLockAsync(
                    db,
                    async () => {
                        await ValidateMigrationHistoryAsync(db, cancellationToken);
                        var pending = await db.Database.GetPendingMigrationsAsync(cancellationToken);
                        if (pending.Any()) {
                            throw new DatabaseNotReadyException(
                                "Database schema has not been migrated by the API yet.");
                        }
                    },
                    cancellationToken);
            },
            cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="action"/>, retrying transient connection/readiness failures with
    /// exponential backoff until it succeeds, the cancellation token fires, or
    /// <see cref="MaxWait"/> elapses. Model, history, and schema incompatibilities fail immediately.
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
                IsTransientStartupFailure(ex) &&
                DateTimeOffset.UtcNow < deadline &&
                !cancellationToken.IsCancellationRequested) {
                logger.LogWarning(
                    "Could not {Operation} (attempt {Attempt}): {Message}. Retrying in {Delay:n0}s.",
                    operation, attempt, ex.Message, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxDelay.Ticks));
            }
        }
    }

    private static async Task ValidateMigrationHistoryAsync(
        PrismediaDbContext db,
        CancellationToken cancellationToken) {
        var known = db.Database.GetMigrations().ToArray();
        var knownSet = known.ToHashSet(StringComparer.Ordinal);
        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
        var unknown = applied
            .Where(migration => !knownSet.Contains(migration))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unknown.Length > 0) {
            throw new InvalidOperationException(
                $"Database contains migrations unknown to this Prismedia build: {string.Join(", ", unknown)}.");
        }

        if (!applied.SequenceEqual(known.Take(applied.Length), StringComparer.Ordinal)) {
            throw new InvalidOperationException(
                "Database migration history is not a prefix of the migrations known to this Prismedia build.");
        }
    }

    private static bool IsTransientStartupFailure(Exception exception) =>
        exception is DatabaseNotReadyException or TimeoutException ||
        exception is NpgsqlException { IsTransient: true } ||
        exception.InnerException is { } inner && IsTransientStartupFailure(inner);

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

    private sealed class DatabaseNotReadyException(string message) : Exception(message);
}
