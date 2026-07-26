using Microsoft.EntityFrameworkCore;
using Npgsql;
using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Queue;
using Xunit.Sdk;

namespace Prismedia.Infrastructure.Tests;

/// <summary>PostgreSQL regressions for durable resource locking and xmin concurrency.</summary>
public sealed class JobResourcePostgresTests {
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ClaimingAResourceGatedNodeLocksAndUpdatesTheRealPostgresRow() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var queue = new JobQueueService(db);
        var resourceKey = JobResourceKeys.Plugin("postgres-resource-test");
        await queue.DeclareResourceAsync(resourceKey, 1, TimeSpan.FromMilliseconds(25), CancellationToken.None);
        var queued = await queue.EnqueueAsync(
            new EnqueueJobRequest(
                JobType.Noop,
                Origin: JobGraphOrigin.Interactive,
                ResourceKey: resourceKey),
            CancellationToken.None);

        var claimed = await queue.ClaimNextGraphNodeAsync(
            "postgres-resource-worker",
            JobGraphOrigin.Interactive,
            CancellationToken.None);

        Assert.NotNull(claimed);
        Assert.Equal(queued.Id, claimed.Id);
        Assert.Equal(resourceKey, claimed.ResourceKey);
        Assert.Single(await db.JobResourceLeases.AsNoTracking().ToArrayAsync());
        Assert.True((await db.JobResourceStates.AsNoTracking().SingleAsync()).NextAvailableAt > DateTimeOffset.MinValue);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ConcurrentSchedulersCannotExceedOneDurableResourceLease() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var resourceKey = JobResourceKeys.Plugin("postgres-concurrency-test");
        await using (var setup = database.CreateContext()) {
            var queue = new JobQueueService(setup);
            await queue.DeclareResourceAsync(resourceKey, 1, TimeSpan.Zero, CancellationToken.None);
            await queue.EnqueueAsync(
                new EnqueueJobRequest(JobType.Noop, Origin: JobGraphOrigin.Interactive, ResourceKey: resourceKey),
                CancellationToken.None);
            await queue.EnqueueAsync(
                new EnqueueJobRequest(JobType.Noop, Origin: JobGraphOrigin.Interactive, ResourceKey: resourceKey),
                CancellationToken.None);
        }

        await using var firstDb = database.CreateContext();
        await using var secondDb = database.CreateContext();
        var claims = await Task.WhenAll(
            new JobQueueService(firstDb).ClaimNextGraphNodeAsync(
                "postgres-worker-a", JobGraphOrigin.Interactive, CancellationToken.None),
            new JobQueueService(secondDb).ClaimNextGraphNodeAsync(
                "postgres-worker-b", JobGraphOrigin.Interactive, CancellationToken.None));

        Assert.Single(claims, claim => claim is not null);
        await using var verification = database.CreateContext();
        Assert.Single(await verification.JobResourceLeases.AsNoTracking().ToArrayAsync());
        Assert.Equal(1, await verification.JobRuns.CountAsync(run => run.Status == JobRunStatus.Running));
        Assert.Equal(1, await verification.JobRuns.CountAsync(run => run.Status == JobRunStatus.Queued));
    }

    private sealed class PostgresTestDatabase(
        string databaseName,
        string adminConnectionString,
        string connectionString) : IAsyncDisposable {
        public PrismediaDbContext CreateContext() =>
            new(new DbContextOptionsBuilder<PrismediaDbContext>()
                .UseNpgsql(connectionString)
                .Options);

        public static async Task<PostgresTestDatabase> CreateAsync() {
            var configured = Environment.GetEnvironmentVariable("PRISMEDIA_TEST_DATABASE_URL")
                ?? "Host=localhost;Port=5432;Database=postgres;Username=prismedia;Password=prismedia";
            var adminBuilder = new NpgsqlConnectionStringBuilder(configured) {
                Database = "postgres",
                Pooling = false
            };
            try {
                await using var probe = new NpgsqlConnection(adminBuilder.ConnectionString);
                await probe.OpenAsync();
            } catch (Exception exception) when (exception is NpgsqlException or TimeoutException) {
                throw SkipException.ForSkip(
                    $"PostgreSQL job-resource test requires PRISMEDIA_TEST_DATABASE_URL or the local dev database: {exception.Message}");
            }

            var name = $"prismedia_job_resource_{Guid.NewGuid():N}";
            await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString)) {
                await admin.OpenAsync();
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
                await create.ExecuteNonQueryAsync();
            }

            var testBuilder = new NpgsqlConnectionStringBuilder(adminBuilder.ConnectionString) {
                Database = name,
                Pooling = false
            };
            var database = new PostgresTestDatabase(
                name,
                adminBuilder.ConnectionString,
                testBuilder.ConnectionString);
            try {
                await using var context = database.CreateContext();
                await context.Database.MigrateAsync();
                return database;
            } catch {
                await database.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync() {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(adminConnectionString);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand(
                $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)",
                admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
