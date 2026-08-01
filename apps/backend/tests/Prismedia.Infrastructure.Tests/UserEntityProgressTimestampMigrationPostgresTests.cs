using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Verifies the progress-specific recency column against a real database migrated from the schema
/// immediately before it was introduced.
/// </summary>
public sealed class UserEntityProgressTimestampMigrationPostgresTests {
    private const string PreviousMigration = "20260731190249_MigrateDirectPlayableEntities";
    private const string MigrationUnderTest = "20260731235116_AddUserEntityProgressUpdatedAt";

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task MigrationAddsThePhysicalColumnAndBackfillsOnlyLegacyProgressRows() {
        await using var database = await PostgresTestDatabase.CreateAsync(PreviousMigration);
        var userId = Guid.NewGuid();
        var progressEntityId = Guid.NewGuid();
        var ratingEntityId = Guid.NewGuid();
        var progressUpdatedAt = DateTimeOffset.Parse("2026-07-31T12:00:00Z");
        var ratingUpdatedAt = progressUpdatedAt.AddMinutes(1);

        await SeedUserAndEntitiesAsync(database, userId, progressEntityId, ratingEntityId);
        await using (var connection = await database.OpenConnectionAsync()) {
            await using var command = new Npgsql.NpgsqlCommand(
                """
                INSERT INTO user_entity_states (
                    user_id, entity_id, is_favorite, rating_value, play_count, skip_count,
                    play_duration_seconds, resume_seconds, last_played_at, completed_at,
                    progress_current_entity_id, progress_unit, progress_index, progress_total,
                    progress_mode, progress_location, progress_completed_at, updated_at)
                VALUES
                    (@user_id, @progress_entity_id, FALSE, NULL, 0, 0, 0, 0, NULL, NULL,
                     @progress_entity_id, @progress_unit, 4, 10, @progress_mode, 'chapter-1', NULL, @progress_updated_at),
                    (@user_id, @rating_entity_id, FALSE, 5, 0, 0, 0, 0, NULL, NULL,
                     NULL, @empty_progress_unit, 0, 0, NULL, NULL, NULL, @rating_updated_at)
                """,
                connection);
            command.Parameters.AddWithValue("user_id", userId);
            command.Parameters.AddWithValue("progress_entity_id", progressEntityId);
            command.Parameters.AddWithValue("rating_entity_id", ratingEntityId);
            command.Parameters.AddWithValue("progress_unit", ProgressUnit.Page.ToCode());
            command.Parameters.AddWithValue("progress_mode", ReaderMode.Paged.ToCode());
            command.Parameters.AddWithValue("empty_progress_unit", ProgressUnit.Item.ToCode());
            command.Parameters.AddWithValue("progress_updated_at", progressUpdatedAt);
            command.Parameters.AddWithValue("rating_updated_at", ratingUpdatedAt);
            await command.ExecuteNonQueryAsync();
        }

        await database.MigrateAsync(MigrationUnderTest);

        await using (var verification = database.CreateContext()) {
            var states = await verification.UserEntityStates
                .Where(row => row.UserId == userId)
                .ToDictionaryAsync(row => row.EntityId);
            Assert.Equal(progressUpdatedAt, states[progressEntityId].ProgressUpdatedAt);
            Assert.Null(states[ratingEntityId].ProgressUpdatedAt);
        }
        Assert.True(await ColumnExistsAsync(database, "progress_updated_at"));

        await database.MigrateAsync(PreviousMigration);
        Assert.False(await ColumnExistsAsync(database, "progress_updated_at"));
    }

    private static async Task SeedUserAndEntitiesAsync(
        PostgresTestDatabase database,
        Guid userId,
        Guid progressEntityId,
        Guid ratingEntityId) {
        var now = DateTimeOffset.UtcNow;
        await using var context = database.CreateContext();
        context.Users.Add(new UserRow {
            Id = userId,
            Username = $"migration-user-{userId:N}",
            NormalizedUsername = $"migration-user-{userId:N}",
            DisplayName = "Migration Tester",
            Role = UserRole.Admin,
            AllowNsfw = true,
            CanCreateLibraries = true,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        context.Entities.AddRange(
            new EntityRow {
                Id = progressEntityId,
                KindCode = EntityKind.Book.ToCode(),
                Title = "Progress row",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = ratingEntityId,
                KindCode = EntityKind.Video.ToCode(),
                Title = "Rating row",
                CreatedAt = now,
                UpdatedAt = now
            });
        await context.SaveChangesAsync();
    }

    private static async Task<bool> ColumnExistsAsync(PostgresTestDatabase database, string column) {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'user_entity_states'
                  AND column_name = @column)
            """,
            connection);
        command.Parameters.AddWithValue("column", column);
        return (bool)(await command.ExecuteScalarAsync())!;
    }
}
