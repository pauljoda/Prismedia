using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Prismedia.Infrastructure.Persistence;
using Xunit.Sdk;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Owns an isolated PostgreSQL database for current-schema integration tests and simple
/// target-migration assertions. Fixture-heavy historical migration suites may retain dedicated
/// helpers for their specialized setup.
/// </summary>
internal sealed class PostgresTestDatabase(
    string databaseName,
    string adminConnectionString,
    string connectionString) : IAsyncDisposable {
    /// <summary>Creates a DbContext connected to this isolated test database.</summary>
    internal PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseNpgsql(connectionString)
            .Options);

    /// <summary>Opens a direct connection for target-migration fixtures and schema assertions.</summary>
    internal async Task<NpgsqlConnection> OpenConnectionAsync() {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>Migrates the isolated database to an explicit migration identifier.</summary>
    internal async Task MigrateAsync(string targetMigration) {
        await using var context = CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    /// <summary>
    /// Seeds the stable user and entity columns available to historical migration fixtures without
    /// asking the current EF model to write columns that did not exist at the fixture's schema point.
    /// </summary>
    internal async Task SeedHistoricalUserAndEntitiesAsync(
        Guid userId,
        string username,
        string displayName,
        string userRole,
        params (Guid Id, string KindCode, string Title)[] entities) {
        var now = DateTimeOffset.UtcNow;
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO users (
                id, username, normalized_username, display_name, allow_nsfw, enabled, role,
                can_create_libraries, created_at, updated_at)
            VALUES (
                @user_id, @username, @normalized_username, @display_name, TRUE, TRUE, @user_role,
                TRUE, @now, @now);

            INSERT INTO entities (id, kind_code, title, created_at, updated_at)
            SELECT id, kind_code, title, @now, @now
            FROM unnest(@entity_ids, @kind_codes, @titles) AS seed(id, kind_code, title);
            """,
            connection);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("username", username);
        command.Parameters.AddWithValue("normalized_username", username.ToUpperInvariant());
        command.Parameters.AddWithValue("display_name", displayName);
        command.Parameters.AddWithValue("user_role", userRole);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("entity_ids", entities.Select(entity => entity.Id).ToArray());
        command.Parameters.AddWithValue("kind_codes", entities.Select(entity => entity.KindCode).ToArray());
        command.Parameters.AddWithValue("titles", entities.Select(entity => entity.Title).ToArray());
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Creates, migrates, and returns a new current-schema test database.</summary>
    internal static async Task<PostgresTestDatabase> CreateAsync(string? targetMigration = null) {
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
                $"PostgreSQL test requires PRISMEDIA_TEST_DATABASE_URL or the local dev database: {exception.Message}");
        }

        var name = $"prismedia_test_{Guid.NewGuid():N}";
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
            if (targetMigration is null) {
                await using var context = database.CreateContext();
                await context.Database.MigrateAsync();
            } else {
                await database.MigrateAsync(targetMigration);
            }
            return database;
        } catch {
            await database.DisposeAsync();
            throw;
        }
    }

    /// <inheritdoc />
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
