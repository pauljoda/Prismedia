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
    private const string TestDatabasePrefix = "prismedia_test_";
    private const string TemplatePrefix = "prismedia_template_";
    private static readonly object TemplateLock = new();
    private static readonly SemaphoreSlim CloneGate = new(1, 1);
    private static Task<string>? template;

    /// <summary>Name of this isolated database on the test server.</summary>
    internal string Name => databaseName;

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

    /// <summary>
    /// Creates and returns a new test database. A current-schema database is cloned from a template that is
    /// migrated once per test process; a target-migration database is migrated from scratch.
    /// </summary>
    internal static async Task<PostgresTestDatabase> CreateAsync(string? targetMigration = null) {
        var adminConnectionString = await RequireAdminConnectionStringAsync();
        var template = targetMigration is null ? await CurrentSchemaTemplateAsync(adminConnectionString) : null;
        var database = await CreateEmptyAsync(adminConnectionString, template);
        if (template is not null) {
            return database;
        }

        try {
            await database.MigrateAsync(targetMigration!);
            return database;
        } catch {
            await database.DisposeAsync();
            throw;
        }
    }

    private static async Task<string> RequireAdminConnectionStringAsync() {
        var explicitlyConfigured = Environment.GetEnvironmentVariable("PRISMEDIA_TEST_DATABASE_URL");
        var configured = explicitlyConfigured
            ?? "Host=localhost;Port=5432;Database=postgres;Username=prismedia;Password=prismedia";
        var adminBuilder = new NpgsqlConnectionStringBuilder(configured) {
            Database = "postgres",
            Pooling = false
        };
        try {
            await using var probe = new NpgsqlConnection(adminBuilder.ConnectionString);
            await probe.OpenAsync();
        } catch (Exception exception) when (explicitlyConfigured is null && exception is NpgsqlException or TimeoutException) {
            // Only the implicit local dev database is optional; an explicitly configured one (CI) must be reachable.
            throw SkipException.ForSkip(
                $"PostgreSQL test requires PRISMEDIA_TEST_DATABASE_URL or the local dev database: {exception.Message}");
        }

        return adminBuilder.ConnectionString;
    }

    private static async Task<PostgresTestDatabase> CreateEmptyAsync(string adminConnectionString, string? template) {
        var name = $"{TestDatabasePrefix}{Guid.NewGuid():N}";
        var templateClause = template is null ? string.Empty : $" TEMPLATE \"{template}\"";
        await CloneGate.WaitAsync();
        try {
            await using var admin = new NpgsqlConnection(adminConnectionString);
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"{templateClause}", admin);
            await create.ExecuteNonQueryAsync();
        } finally {
            CloneGate.Release();
        }

        var testBuilder = new NpgsqlConnectionStringBuilder(adminConnectionString) {
            Database = name,
            Pooling = false
        };
        return new PostgresTestDatabase(name, adminConnectionString, testBuilder.ConnectionString);
    }

    /// <summary>
    /// Migrates one template per test process and build. The name carries the test assembly's module version
    /// so a rebuilt model never reuses a stale schema, and templates left by earlier runs are removed.
    /// </summary>
    private static Task<string> CurrentSchemaTemplateAsync(string adminConnectionString) {
        lock (TemplateLock) {
            return template ??= CreateTemplateAsync(adminConnectionString);
        }
    }

    private static async Task<string> CreateTemplateAsync(string adminConnectionString) {
        var name = $"{TemplatePrefix}{typeof(PrismediaDbContext).Assembly.ManifestModule.ModuleVersionId:N}_{Environment.ProcessId}";
        await DropStaleTemplatesAsync(adminConnectionString);
        var database = await CreateEmptyAsync(adminConnectionString, template: null);
        await using (var context = database.CreateContext()) {
            await context.Database.MigrateAsync();
        }

        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var rename = new NpgsqlCommand(
            $"ALTER DATABASE \"{database.Name}\" RENAME TO \"{name}\"; ALTER DATABASE \"{name}\" WITH IS_TEMPLATE TRUE",
            admin);
        await rename.ExecuteNonQueryAsync();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DropTemplate(adminConnectionString, name);
        return name;
    }

    private static async Task DropStaleTemplatesAsync(string adminConnectionString) {
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var list = new NpgsqlCommand(
            "SELECT datname FROM pg_database WHERE datname LIKE @prefix AND datname NOT LIKE @current",
            admin);
        list.Parameters.AddWithValue("prefix", TemplatePrefix + "%");
        list.Parameters.AddWithValue("current", $"%\\_{Environment.ProcessId}");
        var stale = new List<string>();
        await using (var reader = await list.ExecuteReaderAsync()) {
            while (await reader.ReadAsync()) {
                stale.Add(reader.GetString(0));
            }
        }

        foreach (var name in stale.Where(name => !IsRunningProcess(name))) {
            DropTemplate(adminConnectionString, name);
        }
    }

    private static bool IsRunningProcess(string templateName) {
        var suffix = templateName[(templateName.LastIndexOf('_') + 1)..];
        if (!int.TryParse(suffix, out var processId)) {
            return false;
        }

        try {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        } catch (ArgumentException) {
            return false;
        }
    }

    private static void DropTemplate(string adminConnectionString, string name) {
        try {
            using var admin = new NpgsqlConnection(adminConnectionString);
            admin.Open();
            using var command = new NpgsqlCommand(
                $"ALTER DATABASE \"{name}\" WITH IS_TEMPLATE FALSE; DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)",
                admin);
            command.ExecuteNonQuery();
        } catch (NpgsqlException) {
            // Another run may be removing the same leftover template.
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
