using Microsoft.EntityFrameworkCore;
using Npgsql;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Verifies the 3.0 consumption upgrade against PostgreSQL so legacy progress, counters, event
/// history, and active-time totals remain durable while newly introduced access facts stay exact.
/// </summary>
public sealed class ConsumptionTrackingMigrationPostgresTests {
    private const string PreviousMigration = "20260731235116_AddUserEntityProgressUpdatedAt";
    private const string CentralizationMigration = "20260802002318_CentralizeConsumptionTracking";
    private const string MigrationUnderTest = "20260802141848_ReconcileConsumptionAccess";

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task UpgradePreservesLegacyFactsWithoutInventingAccesses() {
        await using var database = await PostgresTestDatabase.CreateAsync(PreviousMigration);
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        var chapterId = Guid.NewGuid();
        var completedEventId = Guid.NewGuid();
        var skippedEventId = Guid.NewGuid();
        var progressAt = DateTimeOffset.Parse("2026-07-30T02:03:04Z");
        var lastActiveAt = progressAt.AddHours(1);
        await AddCurrentUserModelCompatibilityAsync(database);
        await SeedUserAndEntitiesAsync(database, userId, bookId, chapterId);

        await using (var connection = await database.OpenConnectionAsync()) {
            await InsertLegacyStateAsync(connection, userId, bookId, chapterId, progressAt, lastActiveAt);
            await InsertLegacyEventsAsync(
                connection,
                userId,
                bookId,
                completedEventId,
                skippedEventId,
                lastActiveAt);
            await InsertLegacyActivityAsync(connection, userId, bookId, lastActiveAt);
        }

        await database.MigrateAsync(MigrationUnderTest);

        await using var verification = database.CreateContext();
        var state = await verification.UserEntityStates.SingleAsync(row =>
            row.UserId == userId && row.EntityId == bookId);
        Assert.True(state.IsFavorite);
        Assert.Equal(4, state.RatingValue);
        Assert.Equal(7, state.CompletionCount);
        Assert.Equal(2, state.SkipCount);
        Assert.Equal(0, state.AccessCount);
        Assert.Null(state.LastAccessedAt);
        Assert.Equal(158.5, state.ActiveSeconds);
        Assert.Equal(42.25, state.ResumeSeconds);
        Assert.Equal(lastActiveAt, state.LastActiveAt);
        Assert.Equal(chapterId, state.ProgressCurrentEntityId);
        Assert.Equal(ProgressUnit.Page.ToCode(), state.ProgressUnit);
        Assert.Equal(3, state.ProgressIndex);
        Assert.Equal(10, state.ProgressTotal);
        Assert.Equal(ReaderMode.Scrolled.ToCode(), state.ProgressMode);
        Assert.Equal("epubcfi(/6/14!/4/2/8)", state.ProgressLocation);
        Assert.Equal(progressAt, state.ProgressUpdatedAt);
        Assert.Equal(4, state.ProgressConsumedCount);

        var events = await verification.EntityConsumptionEvents
            .Where(row => row.EntityId == bookId)
            .OrderBy(row => row.OccurredAt)
            .ToArrayAsync();
        Assert.Equal([completedEventId, skippedEventId], events.Select(row => row.Id));
        Assert.Equal([ConsumptionEventKind.Completed, ConsumptionEventKind.Skipped], events.Select(row => row.Kind));
        Assert.All(events, row => Assert.Equal(userId, row.UserId));

        var days = await verification.EntityConsumptionDays
            .Where(row => row.EntityId == bookId)
            .OrderBy(row => row.Kind)
            .ToArrayAsync();
        Assert.Equal(2, days.Length);
        Assert.Equal(35, days.Sum(row => row.DurationSeconds));
        Assert.Equal(30, days.Single(row => row.Kind == ConsumptionActivityKind.Reading).DurationSeconds);
        Assert.Equal(5, days.Single(row => row.Kind == ConsumptionActivityKind.Listening).DurationSeconds);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task CorrectiveMigrationRetainsRecordedAccessesAndDiscardsOnlyLegacyEstimates() {
        await using var database = await PostgresTestDatabase.CreateAsync(CentralizationMigration);
        var userId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-08-02T12:00:00Z");
        await AddCurrentUserModelCompatibilityAsync(database);
        await SeedUserAndEntitiesAsync(database, userId, entityId);

        await using (var seed = database.CreateContext()) {
            seed.UserEntityStates.Add(new UserEntityStateRow {
                UserId = userId,
                EntityId = entityId,
                AccessCount = 9,
                CompletionCount = 7,
                SkipCount = 2,
                LastAccessedAt = now.AddDays(-10),
                UpdatedAt = now
            });
            seed.EntityConsumptionEvents.AddRange(
                Access(entityId, userId, "session-1", now.AddMinutes(-2)),
                Access(entityId, userId, "session-2", now.AddMinutes(-1)),
                new EntityConsumptionEventRow {
                    Id = Guid.NewGuid(),
                    EntityId = entityId,
                    UserId = userId,
                    Kind = ConsumptionEventKind.Completed,
                    OccurredAt = now,
                    CreatedAt = now
                });
            await seed.SaveChangesAsync();
        }

        await database.MigrateAsync(MigrationUnderTest);

        await using var verification = database.CreateContext();
        var state = await verification.UserEntityStates.SingleAsync(row =>
            row.UserId == userId && row.EntityId == entityId);
        Assert.Equal(2, state.AccessCount);
        Assert.Equal(now.AddMinutes(-1), state.LastAccessedAt);
        Assert.Equal(7, state.CompletionCount);
        Assert.Equal(2, state.SkipCount);
        Assert.Equal(3, await verification.EntityConsumptionEvents.CountAsync(row => row.EntityId == entityId));
    }

    private static EntityConsumptionEventRow Access(
        Guid entityId,
        Guid userId,
        string sessionId,
        DateTimeOffset occurredAt) => new() {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            UserId = userId,
            Kind = ConsumptionEventKind.Accessed,
            OccurredAt = occurredAt,
            SessionId = sessionId,
            CreatedAt = occurredAt
        };

    private static async Task AddCurrentUserModelCompatibilityAsync(PostgresTestDatabase database) {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "ALTER TABLE users ADD COLUMN can_request_content boolean NOT NULL DEFAULT false",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedUserAndEntitiesAsync(
        PostgresTestDatabase database,
        Guid userId,
        params Guid[] entityIds) {
        var now = DateTimeOffset.UtcNow;
        await using var context = database.CreateContext();
        context.Users.Add(new UserRow {
            Id = userId,
            Username = $"consumption-migration-{userId:N}",
            NormalizedUsername = $"consumption-migration-{userId:N}",
            DisplayName = "Consumption Migration Tester",
            Role = UserRole.Admin,
            AllowNsfw = true,
            CanCreateLibraries = true,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        context.Entities.AddRange(entityIds.Select((id, index) => new EntityRow {
            Id = id,
            KindCode = index == 0 ? EntityKind.Book.ToCode() : EntityKind.BookChapter.ToCode(),
            Title = $"Migration entity {index + 1}",
            CreatedAt = now,
            UpdatedAt = now
        }));
        await context.SaveChangesAsync();
    }

    private static async Task InsertLegacyStateAsync(
        NpgsqlConnection connection,
        Guid userId,
        Guid bookId,
        Guid chapterId,
        DateTimeOffset progressAt,
        DateTimeOffset lastActiveAt) {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO user_entity_states (
                user_id, entity_id, is_favorite, rating_value, play_count, skip_count,
                play_duration_seconds, resume_seconds, last_played_at, completed_at,
                progress_current_entity_id, progress_unit, progress_index, progress_total,
                progress_mode, progress_location, progress_completed_at, progress_updated_at, updated_at)
            VALUES (
                @user_id, @book_id, TRUE, 4, 7, 2,
                158.5, 42.25, @last_active_at, NULL,
                @chapter_id, @progress_unit, 3, 10,
                @progress_mode, 'epubcfi(/6/14!/4/2/8)', NULL, @progress_at, @last_active_at)
            """,
            connection);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("book_id", bookId);
        command.Parameters.AddWithValue("chapter_id", chapterId);
        command.Parameters.AddWithValue("last_active_at", lastActiveAt);
        command.Parameters.AddWithValue("progress_unit", ProgressUnit.Page.ToCode());
        command.Parameters.AddWithValue("progress_mode", ReaderMode.Scrolled.ToCode());
        command.Parameters.AddWithValue("progress_at", progressAt);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertLegacyEventsAsync(
        NpgsqlConnection connection,
        Guid userId,
        Guid entityId,
        Guid completedEventId,
        Guid skippedEventId,
        DateTimeOffset occurredAt) {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO entity_playback_events (
                id, entity_id, user_id, kind, occurred_at, position_seconds, duration_seconds, created_at)
            VALUES
                (@completed_id, @entity_id, @user_id, 'completed', @completed_at, 42.25, 300, @completed_at),
                (@skipped_id, @entity_id, @user_id, 'skipped', @skipped_at, 3, 300, @skipped_at)
            """,
            connection);
        command.Parameters.AddWithValue("completed_id", completedEventId);
        command.Parameters.AddWithValue("skipped_id", skippedEventId);
        command.Parameters.AddWithValue("entity_id", entityId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("completed_at", occurredAt);
        command.Parameters.AddWithValue("skipped_at", occurredAt.AddMinutes(1));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertLegacyActivityAsync(
        NpgsqlConnection connection,
        Guid userId,
        Guid entityId,
        DateTimeOffset occurredAt) {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO entity_activity_events (
                id, entity_id, user_id, kind, occurred_at, duration_seconds, created_at)
            VALUES
                (@reading_one, @entity_id, @user_id, 'reading', @occurred_at, 10, @occurred_at),
                (@reading_two, @entity_id, @user_id, 'reading', @occurred_at, 20, @occurred_at),
                (@listening, @entity_id, @user_id, 'listening', @occurred_at, 5, @occurred_at)
            """,
            connection);
        command.Parameters.AddWithValue("reading_one", Guid.NewGuid());
        command.Parameters.AddWithValue("reading_two", Guid.NewGuid());
        command.Parameters.AddWithValue("listening", Guid.NewGuid());
        command.Parameters.AddWithValue("entity_id", entityId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        await command.ExecuteNonQueryAsync();
    }
}
