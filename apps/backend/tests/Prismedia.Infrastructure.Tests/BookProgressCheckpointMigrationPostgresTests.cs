using Npgsql;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// PostgreSQL upgrade coverage for seeding modality checkpoints from the released Book row shape:
/// an EPUB cursor plus the pre-unification absolute audiobook resume time.
/// </summary>
public sealed class BookProgressCheckpointMigrationPostgresTests {
    private const string PreviousMigration = "20260923055537_AddManagedBookRenditionScope";
    private const string MigrationUnderTest = "20260924171447_AddUserProgressCheckpoints";

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ReleasedCursorAndResumeTimeBecomeExactReadingAndListeningCheckpoints() {
        await using var database = await PostgresTestDatabase.CreateAsync(PreviousMigration);
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        var firstTrackId = Guid.NewGuid();
        var secondTrackId = Guid.NewGuid();
        var readAt = DateTimeOffset.Parse("2026-09-01T10:00:00Z");
        var listenedAt = readAt.AddHours(2);
        await database.SeedHistoricalUserAndEntitiesAsync(
            userId,
            "reader",
            "Reader",
            UserRole.Admin.ToCode(),
            (bookId, EntityKind.Book.ToCode(), "Fixture Book"),
            (firstTrackId, EntityKind.AudioTrack.ToCode(), "Part 1"),
            (secondTrackId, EntityKind.AudioTrack.ToCode(), "Part 2"));

        await using (var connection = await database.OpenConnectionAsync()) {
            await ExecuteAsync(
                connection,
                """
                UPDATE entities SET parent_entity_id = @book_id, sort_order = 0 WHERE id = @first_track_id;
                UPDATE entities SET parent_entity_id = @book_id, sort_order = 1 WHERE id = @second_track_id;
                INSERT INTO entity_files (id, entity_id, role, path, created_at, updated_at)
                VALUES (gen_random_uuid(), @first_track_id, @source, '/media/part-1.m4b', @read_at, @read_at),
                       (gen_random_uuid(), @second_track_id, @source, '/media/part-2.m4b', @read_at, @read_at);
                INSERT INTO entity_technical (entity_id, duration_seconds, updated_at)
                VALUES (@first_track_id, 600, @read_at), (@second_track_id, 900, @read_at);
                INSERT INTO user_entity_states (
                    user_id, entity_id, is_favorite, skip_count, active_seconds, resume_seconds,
                    last_active_at, progress_current_entity_id, progress_unit, progress_index,
                    progress_total, progress_mode, progress_location, progress_updated_at, updated_at)
                VALUES (
                    @user_id, @book_id, FALSE, 0, 0, 700.5,
                    @listened_at, @book_id, @cfi, 2300,
                    10000, @paged, 'epubcfi(/6/12!/4/2)', @read_at, @listened_at);
                """,
                ("book_id", bookId),
                ("first_track_id", firstTrackId),
                ("second_track_id", secondTrackId),
                ("user_id", userId),
                ("source", EntityFileRole.Source.ToCode()),
                ("cfi", ProgressUnit.Cfi.ToCode()),
                ("paged", ReaderMode.Paged.ToCode()),
                ("read_at", readAt),
                ("listened_at", listenedAt));
        }

        await database.MigrateAsync(MigrationUnderTest);

        await using var verification = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT modality, position_entity_id, unit, "index", total, offset_seconds, mode, location, updated_at
            FROM user_progress_checkpoints
            WHERE user_id = @user_id AND entity_id = @book_id
            ORDER BY modality
            """,
            verification);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("book_id", bookId);
        var rows = new List<object?[]>();
        await using (var reader = await command.ExecuteReaderAsync()) {
            while (await reader.ReadAsync()) {
                var values = new object?[reader.FieldCount];
                reader.GetValues(values!);
                rows.Add(values.Select(value => value is DBNull ? null : value).ToArray());
            }
        }

        Assert.Equal(2, rows.Count);
        var listening = rows.Single(row => (string)row[0]! == ConsumptionModality.Listening.ToCode());
        Assert.Equal(secondTrackId, listening[1]);
        Assert.Equal(ProgressUnit.Second.ToCode(), listening[2]);
        Assert.Equal(100, listening[3]);
        Assert.Equal(900, listening[4]);
        Assert.Equal(100.5, listening[5]);
        Assert.Null(listening[6]);
        Assert.Equal(listenedAt, new DateTimeOffset((DateTime)listening[8]!));

        var reading = rows.Single(row => (string)row[0]! == ConsumptionModality.Reading.ToCode());
        Assert.Equal(bookId, reading[1]);
        Assert.Equal(ProgressUnit.Cfi.ToCode(), reading[2]);
        Assert.Equal(2300, reading[3]);
        Assert.Equal(10000, reading[4]);
        Assert.Null(reading[5]);
        Assert.Equal(ReaderMode.Paged.ToCode(), reading[6]);
        Assert.Equal("epubcfi(/6/12!/4/2)", reading[7]);
        Assert.Equal(readAt, new DateTimeOffset((DateTime)reading[8]!));
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        string sql,
        params (string Name, object Value)[] parameters) {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) {
            command.Parameters.AddWithValue(name, value);
        }
        await command.ExecuteNonQueryAsync();
    }
}
