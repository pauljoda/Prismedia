using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Media.Persistence;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Migrations;
using Xunit.Sdk;

namespace Prismedia.Infrastructure.Tests;

/// <summary>PostgreSQL upgrade coverage for release-gating direct playable Entity identities.</summary>
public sealed class DirectPlayableEntityMigrationPostgresTests {
    private const string PreviousMigration = DirectPlayableMigrationAssetPreparer.PreviousMigrationId;
    private const string MigrationUnderTest = DirectPlayableMigrationAssetPreparer.MigrationId;
    private const string LegacyPerformerRoleCode = MigrateDirectPlayableEntities.LegacyPerformerRoleCode;

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task RunnerCollapsesMovieRetypesEpisodesAndPreservesDurableReferencesAndRescanIdentity() {
        await using var database = await PostgresTestDatabase.CreateAsync(PreviousMigration);
        using var files = MigrationFiles.Create();
        var fixture = MigrationFixture.Create(files);
        await SeedPopulatedLegacyLibraryAsync(database, fixture);
        var audit = await SeedMappedAuditStateAsync(database, fixture);

        await database.RunMigrationRunnerAsync(files.Assets);

        await using (var connection = await database.OpenConnectionAsync()) {
            Assert.False(await ExistsAsync(connection, "SELECT EXISTS (SELECT 1 FROM entities WHERE id = @id)", fixture.LegacyMovieVideoId));
            Assert.Equal(EntityKind.Movie.ToCode(), await ScalarAsync<string>(connection, "SELECT kind_code FROM entities WHERE id = @id", fixture.MovieId));
            Assert.Equal(EntityKind.VideoEpisode.ToCode(), await ScalarAsync<string>(connection, "SELECT kind_code FROM entities WHERE id = @id", fixture.FirstEpisodeId));
            Assert.Equal(EntityKind.VideoEpisode.ToCode(), await ScalarAsync<string>(connection, "SELECT kind_code FROM entities WHERE id = @id", fixture.SecondEpisodeId));
            Assert.Equal(EntityKind.Video.ToCode(), await ScalarAsync<string>(connection, "SELECT kind_code FROM entities WHERE id = @id", fixture.StandaloneVideoId));
            Assert.Equal(2, await ScalarAsync<int>(connection, "SELECT count(*)::int FROM entity_files WHERE path = @id", fixture.SharedEpisodePath));

            Assert.Equal(fixture.MovieSourcePath, await ScalarAsync<string>(connection, "SELECT path FROM entity_files WHERE entity_id = @id AND role = @role", fixture.MovieId, ("role", EntityFileRole.Source.ToCode())));
            Assert.Equal(fixture.MovieFolder, await ScalarAsync<string>(connection, "SELECT value FROM entity_sources WHERE entity_id = @id AND code = @code", fixture.MovieId, ("code", EntitySourceCode.Folder.ToCode())));
            Assert.Equal(fixture.SeriesFolder, await ScalarAsync<string>(connection, "SELECT value FROM entity_sources WHERE entity_id = @id AND code = @code", fixture.SeriesId, ("code", EntitySourceCode.Folder.ToCode())));
            Assert.Equal(fixture.BookFolder, await ScalarAsync<string>(connection, "SELECT value FROM entity_sources WHERE entity_id = @id AND code = @code", fixture.FolderBookId, ("code", EntitySourceCode.Folder.ToCode())));
            Assert.Equal(fixture.FileBookPath, await ScalarAsync<string>(connection, "SELECT path FROM entity_files WHERE entity_id = @id AND role = @role", fixture.FileBookId, ("role", EntityFileRole.Source.ToCode())));
            Assert.False(await ExistsAsync(connection, "SELECT EXISTS (SELECT 1 FROM entity_files WHERE entity_id = @id AND role = @role)", fixture.MovieId, ("role", EntityFileRole.Thumbnail.ToCode())));

            Assert.Equal(fixture.MovieId, await ScalarAsync<Guid>(connection, "SELECT entity_id FROM entity_technical WHERE duration_seconds = 123"));
            Assert.Equal(fixture.MovieId, await ScalarAsync<Guid>(connection, "SELECT entity_id FROM entity_markers WHERE title = 'Opening'"));
            Assert.Equal(fixture.MovieId, await ScalarAsync<Guid>(connection, "SELECT entity_id FROM media_sources WHERE path = @id", fixture.MovieSourcePath));
            Assert.Equal(fixture.MovieId, await ScalarAsync<Guid>(connection, "SELECT entity_id FROM user_entity_states WHERE user_id = @id", fixture.UserId));
            Assert.Equal(1, await ScalarAsync<int>(connection, "SELECT count(*)::int FROM user_entity_states WHERE user_id = @id", fixture.UserId));
            Assert.True(await ScalarAsync<bool>(connection, "SELECT is_favorite FROM user_entity_states WHERE user_id = @id", fixture.UserId));
            Assert.Equal(9, await ScalarAsync<int>(connection, "SELECT rating_value FROM user_entity_states WHERE user_id = @id", fixture.UserId));
            Assert.Equal(7, await ScalarAsync<int>(connection, "SELECT play_count FROM user_entity_states WHERE user_id = @id", fixture.UserId));
            Assert.Equal(1, await ScalarAsync<int>(connection, "SELECT skip_count FROM user_entity_states WHERE user_id = @id", fixture.UserId));
            Assert.Equal(120d, await ScalarAsync<double>(connection, "SELECT play_duration_seconds FROM user_entity_states WHERE user_id = @id", fixture.UserId));
            Assert.Equal(30d, await ScalarAsync<double>(connection, "SELECT resume_seconds FROM user_entity_states WHERE user_id = @id", fixture.UserId));
            Assert.Equal(fixture.MovieId, await ScalarAsync<Guid>(connection, "SELECT progress_current_entity_id FROM user_entity_states WHERE user_id = @id", fixture.UserId));
            Assert.Equal("child", await ScalarAsync<string>(connection, "SELECT progress_location FROM user_entity_states WHERE user_id = @id", fixture.UserId));
            Assert.True(await ExistsAsync(connection, "SELECT completed_at IS NOT NULL FROM user_entity_states WHERE user_id = @id", fixture.UserId));
            Assert.Equal(fixture.MovieId, await ScalarAsync<Guid>(connection, "SELECT item_entity_id FROM collection_item_details WHERE collection_entity_id = @id", fixture.CollectionId));
            Assert.Equal(fixture.MovieId, await ScalarAsync<Guid>(connection, "SELECT cover_item_entity_id FROM collection_details WHERE entity_id = @id", fixture.CollectionId));

            var metadata = await ScalarAsync<string>(connection, "SELECT metadata_json::text FROM entity_relationship_links WHERE entity_id = @id", fixture.MovieId);
            Assert.Contains($"\"role\": \"{CreditRole.Actor.ToCode()}\"", metadata, StringComparison.Ordinal);
            Assert.Contains($"\"roles\": [\"{CreditRole.Actor.ToCode()}\", \"director\"]", metadata, StringComparison.Ordinal);
            Assert.Contains("performer biography", metadata, StringComparison.Ordinal);
            var unrelatedMetadata = await ScalarAsync<string>(connection, "SELECT metadata_json::text FROM entity_relationship_links WHERE entity_id = @id AND relationship_code = @relationship", fixture.MovieId, ("relationship", RelationshipKind.Related.ToCode()));
            Assert.Contains($"\"role\": \"{LegacyPerformerRoleCode}\"", unrelatedMetadata, StringComparison.Ordinal);

            var rule = await ScalarAsync<string>(connection, "SELECT rule_tree_json::text FROM collection_details WHERE entity_id = @id", fixture.CollectionId);
            Assert.Contains(EntityKind.Video.ToCode(), rule, StringComparison.Ordinal);
            Assert.Contains(EntityKind.VideoEpisode.ToCode(), rule, StringComparison.Ordinal);
            Assert.Contains(EntityKind.Movie.ToCode(), rule, StringComparison.Ordinal);
            var providers = await ScalarAsync<string>(connection, "SELECT value_json::text FROM app_settings WHERE key = @id", AppSettings.Identify.DefaultProviders.Key);
            Assert.Contains($"\"{EntityKind.VideoEpisode.ToCode()}\": \"fixture-provider\"", providers, StringComparison.Ordinal);

            Assert.Equal(fixture.MovieId.ToString(), await ScalarAsync<string>(connection, "SELECT target_entity_id FROM job_runs WHERE id = @id", fixture.JobRunId));
            Assert.Equal(EntityKind.Movie.ToCode(), await ScalarAsync<string>(connection, "SELECT target_entity_kind FROM job_runs WHERE id = @id", fixture.JobRunId));
            Assert.Equal($"{JobType.ScanLibrary.ToCode()}:{fixture.MovieId}", await ScalarAsync<string>(connection, "SELECT node_key FROM job_runs WHERE id = @id", fixture.JobRunId));
            Assert.Equal(JobResourceKeys.Entity(fixture.MovieId.ToString()), await ScalarAsync<string>(connection, "SELECT resource_key FROM job_runs WHERE id = @id", fixture.JobRunId));
            var payload = await ScalarAsync<string>(connection, "SELECT payload_json::text FROM job_runs WHERE id = @id", fixture.JobRunId);
            Assert.Contains(fixture.MovieId.ToString(), payload, StringComparison.Ordinal);
            Assert.Contains($"legacy text {fixture.LegacyMovieVideoId}", payload, StringComparison.Ordinal);
            Assert.DoesNotContain(
                $"\"EntityKind\": \"{EntityKind.Video.ToCode()}\"",
                payload,
                StringComparison.Ordinal);
            var mixedPayload = await ScalarAsync<string>(connection, "SELECT payload_json::text FROM job_runs WHERE id = @id", fixture.MixedJobRunId);
            Assert.Contains($"\"EntityKind\": \"{EntityKind.Movie.ToCode()}\"", mixedPayload, StringComparison.Ordinal);
            Assert.Contains($"\"EntityKind\": \"{EntityKind.VideoEpisode.ToCode()}\"", mixedPayload, StringComparison.Ordinal);
            Assert.DoesNotContain($"\"EntityKind\": \"{EntityKind.Video.ToCode()}\"", mixedPayload, StringComparison.Ordinal);

            Assert.Equal(fixture.MovieId, await ScalarAsync<Guid>(connection, "SELECT entity_id FROM acquisitions WHERE id = @id", fixture.AcquisitionId));
            Assert.Equal(
                EntityKind.Movie,
                ImportPlacementCheckpointJson.Deserialize(await ScalarAsync<string>(
                    connection,
                    "SELECT tv_import_checkpoint_json::text FROM acquisitions WHERE id = @id",
                    fixture.AcquisitionId))!.Kind);
            Assert.Equal(
                AcquisitionImportPhase.Imported,
                AcquisitionImportFileLedgerJson.Deserialize(await ScalarAsync<string>(
                    connection,
                    "SELECT import_result_json::text FROM acquisitions WHERE id = @id",
                    fixture.AcquisitionId))!.Phase);
            Assert.Equal(EntityKind.VideoEpisode.ToCode(), await ScalarAsync<string>(connection, "SELECT kind FROM acquisitions WHERE id = @id", audit.EpisodeAcquisitionId));
            Assert.NotNull(TvImportCheckpointJson.Deserialize(await ScalarAsync<string>(
                connection,
                "SELECT tv_import_checkpoint_json::text FROM acquisitions WHERE id = @id",
                audit.EpisodeAcquisitionId)));
            Assert.Equal(
                AcquisitionImportPhase.Importing,
                AcquisitionImportFileLedgerJson.Deserialize(await ScalarAsync<string>(
                    connection,
                    "SELECT import_result_json::text FROM acquisitions WHERE id = @id",
                    audit.EpisodeAcquisitionId))!.Phase);
            Assert.Equal(EntityKind.Movie.ToCode(), await ScalarAsync<string>(connection, "SELECT kind FROM monitors WHERE id = @id", fixture.MonitorId));
            Assert.Equal(fixture.MovieId, await ScalarAsync<Guid>(connection, "SELECT entity_id FROM identify_queue_items WHERE id = @id", audit.DoneQueueItemId));
            Assert.Equal(IdentifyQueueState.Done.ToCode(), await ScalarAsync<string>(connection, "SELECT state FROM identify_queue_items WHERE id = @id", audit.DoneQueueItemId));
            Assert.Equal(IdentifyQueueState.Deleted.ToCode(), await ScalarAsync<string>(connection, "SELECT state FROM identify_queue_items WHERE id = @id", audit.DeletedQueueItemId));
            Assert.False(await ExistsAsync(
                connection,
                "SELECT EXISTS (SELECT 1 FROM identify_queue_items WHERE id IN (@done_id, @deleted_id) AND state NOT IN (@done_state, @deleted_state))",
                id: null,
                ("done_id", audit.DoneQueueItemId),
                ("deleted_id", audit.DeletedQueueItemId),
                ("done_state", IdentifyQueueState.Done.ToCode()),
                ("deleted_state", IdentifyQueueState.Deleted.ToCode())));
            Assert.Equal(fixture.MovieId, await ScalarAsync<Guid>(connection, "SELECT entity_id FROM identify_results WHERE id = @id", audit.IdentifyResultId));
            Assert.Contains(
                fixture.LegacyMovieVideoId.ToString(),
                await ScalarAsync<string>(connection, "SELECT proposed_result_json::text FROM identify_results WHERE id = @id", audit.IdentifyResultId),
                StringComparison.Ordinal);
            Assert.False(await ExistsAsync(connection, "SELECT to_regclass('public.video_details') IS NOT NULL"));
            Assert.True(await ExistsAsync(connection, "SELECT EXISTS (SELECT 1 FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = @id)", MigrationUnderTest));
        }

        Assert.Equal("normalized subtitle", await File.ReadAllTextAsync(fixture.MigratedSubtitlePath));
        Assert.Equal("styled subtitle", await File.ReadAllTextAsync(fixture.MigratedSubtitleSourcePath));
        Assert.Equal("custom poster", await File.ReadAllTextAsync(fixture.MigratedArtworkPath));
        Assert.True(File.Exists(fixture.LegacySubtitlePath));
        Assert.True(File.Exists(fixture.LegacyArtworkPath));

        await using (var db = database.CreateContext()) {
            var service = new LibraryScanPersistenceService(db, files.Assets);
            var rescanned = await service.UpsertVideosBatchAsync([
                new VideoUpsertItem(
                    fixture.MovieSourcePath,
                    "Direct Movie",
                    fixture.LibraryRootId,
                    IsNsfw: false,
                    ScanPlacement: PlayableVideoScanPlacement.Movie,
                    Movie: new MovieScanInfo(fixture.MovieFolder, "Direct Movie")),
                new VideoUpsertItem(
                    fixture.SharedEpisodePath,
                    "Shared Episode File",
                    fixture.LibraryRootId,
                    IsNsfw: false,
                    ScanPlacement: PlayableVideoScanPlacement.Episode,
                    new VideoSeriesScanInfo(fixture.SeriesFolder, "Series"),
                    new VideoSeasonScanInfo(fixture.SeasonFolder, "Season 1", 1),
                    EpisodeNumber: 5,
                    AbsoluteEpisodeNumber: null),
                new VideoUpsertItem(
                    fixture.SharedEpisodePath,
                    "Shared Episode File",
                    fixture.LibraryRootId,
                    IsNsfw: false,
                    ScanPlacement: PlayableVideoScanPlacement.Episode,
                    new VideoSeriesScanInfo(fixture.SeriesFolder, "Series"),
                    new VideoSeasonScanInfo(fixture.SeasonFolder, "Season 1", 1),
                    EpisodeNumber: 6,
                    AbsoluteEpisodeNumber: null)
            ], CancellationToken.None);
            Assert.Equal([fixture.MovieId, fixture.FirstEpisodeId, fixture.SecondEpisodeId], rescanned);
        }

        await database.MigrateAsync(MigrationUnderTest);
        var down = await Assert.ThrowsAsync<PostgresException>(() => database.MigrateAsync(PreviousMigration));
        Assert.Equal(PostgresErrorCodes.RaiseException, down.SqlState);
        Assert.Contains("irreversible", down.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task DirectEfUpgradeFailsClosedWhenLegacyRowsBypassFilesystemPreparation() {
        await using var database = await PostgresTestDatabase.CreateAsync(PreviousMigration);
        var movieId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        await SeedMinimalMovieAsync(database, movieId, childId, movieFolder: null, videoPath: null);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => database.MigrateAsync(MigrationUnderTest));

        Assert.Equal(PostgresErrorCodes.RaiseException, exception.SqlState);
        Assert.Contains("filesystem preparation manifest", exception.MessageText, StringComparison.Ordinal);
        await using var connection = await database.OpenConnectionAsync();
        Assert.True(await ExistsAsync(connection, "SELECT EXISTS (SELECT 1 FROM entities WHERE id = @id)", childId));
        Assert.False(await ExistsAsync(connection, "SELECT EXISTS (SELECT 1 FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = @id)", MigrationUnderTest));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task PreparerRejectsAmbiguousMovieBeforeCopyingManagedArtwork() {
        await using var database = await PostgresTestDatabase.CreateAsync(PreviousMigration);
        using var files = MigrationFiles.Create();
        var movieId = Guid.NewGuid();
        var firstChildId = Guid.NewGuid();
        var secondChildId = Guid.NewGuid();
        await SeedMinimalMovieAsync(database, movieId, firstChildId, files.CreateFolder("ambiguous-movie"), files.CreateFile("ambiguous-movie/first.mkv", "first"));
        await using (var connection = await database.OpenConnectionAsync()) {
            await ExecuteAsync(
                connection,
                """
                INSERT INTO entities (id, kind_code, title, is_nsfw, is_organized, is_wanted, created_at, updated_at)
                VALUES (@id, @kind, 'Second', FALSE, FALSE, FALSE, now(), now());
                UPDATE entities SET parent_entity_id = @movie_id WHERE id = @id;
                INSERT INTO video_details (entity_id) VALUES (@id);
                INSERT INTO entity_files (id, entity_id, role, path, source, created_at, updated_at)
                VALUES (@file_id, @id, @role, @path, @source, now(), now());
                """,
                ("id", secondChildId),
                ("kind", EntityKind.Video.ToCode()),
                ("movie_id", movieId),
                ("file_id", Guid.NewGuid()),
                ("role", EntityFileRole.Source.ToCode()),
                ("path", files.CreateFile("ambiguous-movie/second.mkv", "second")),
                ("source", FileSourceKind.Scan.ToCode()));
        }

        var legacyUrl = $"/assets/custom/artwork/{firstChildId}/poster.jpg";
        var legacyPath = files.AssetDiskPath(legacyUrl);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        await File.WriteAllTextAsync(legacyPath, "poster");
        await using (var connection = await database.OpenConnectionAsync()) {
            await ExecuteAsync(
                connection,
                "INSERT INTO entity_files (id, entity_id, role, path, source, created_at, updated_at) VALUES (@file_id, @id, @role, @path, @source, now(), now())",
                ("file_id", Guid.NewGuid()), ("id", firstChildId), ("role", EntityFileRole.Poster.ToCode()),
                ("path", legacyUrl), ("source", FileSourceKind.Custom.ToCode()));
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => database.RunMigrationRunnerAsync(files.Assets));

        Assert.Contains("multiple Video children", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(files.AssetDiskPath($"/assets/custom/artwork/{movieId}/poster.jpg")));
        await using var verify = await database.OpenConnectionAsync();
        Assert.False(await ExistsAsync(verify, "SELECT EXISTS (SELECT 1 FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = @id)", MigrationUnderTest));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task RunningJobGuardRollsBackEveryDatabaseChange() {
        await using var database = await PostgresTestDatabase.CreateAsync(PreviousMigration);
        using var files = MigrationFiles.Create();
        var movieId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var movieFolder = files.CreateFolder("guard/movie");
        var videoPath = files.CreateFile("guard/movie/movie.mkv", "movie");
        await SeedMinimalMovieAsync(database, movieId, childId, movieFolder, videoPath);
        await using (var connection = await database.OpenConnectionAsync()) {
            var graphId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            await ExecuteAsync(
                connection,
                """
                INSERT INTO job_graphs
                    (id, origin, status, display_name, root_run_id, root_entity_kind, root_entity_id,
                     cancellation_requested, created_at, updated_at)
                VALUES (@graph_id, @origin, @graph_status, 'Guard', @run_id, @kind, @entity_id,
                        FALSE, now(), now());
                INSERT INTO job_runs
                    (id, type, status, payload_json, attempts, max_attempts, progress,
                     target_entity_kind, target_entity_id, available_at, created_at, graph_id)
                VALUES (@run_id, @job_type, @run_status, '{}'::jsonb, 0, 3, 0, @kind, @entity_id,
                        now(), now(), @graph_id);
                """,
                ("graph_id", graphId), ("run_id", runId),
                ("origin", JobGraphOrigin.Background.ToCode()),
                ("graph_status", JobGraphStatus.Running.ToCode()),
                ("job_type", JobType.ScanLibrary.ToCode()),
                ("run_status", JobRunStatus.Running.ToCode()),
                ("kind", EntityKind.Video.ToCode()), ("entity_id", childId.ToString()));
        }

        var exception = await Assert.ThrowsAsync<PostgresException>(() => database.RunMigrationRunnerAsync(files.Assets));

        Assert.Equal(PostgresErrorCodes.RaiseException, exception.SqlState);
        Assert.Contains("running or leased job work", exception.MessageText, StringComparison.Ordinal);
        await using var verify = await database.OpenConnectionAsync();
        Assert.True(await ExistsAsync(verify, "SELECT EXISTS (SELECT 1 FROM entities WHERE id = @id)", childId));
        Assert.True(await ExistsAsync(verify, "SELECT EXISTS (SELECT 1 FROM entity_files WHERE entity_id = @id AND path = @path)", movieId, ("path", movieFolder)));
        Assert.False(await ExistsAsync(verify, "SELECT EXISTS (SELECT 1 FROM entity_sources WHERE entity_id = @id)", movieId));
        Assert.False(await ExistsAsync(verify, "SELECT EXISTS (SELECT 1 FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = @id)", MigrationUnderTest));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task PreparerRequiresOneOrdinaryMoviePayloadSourceRow() {
        await using var database = await PostgresTestDatabase.CreateAsync(PreviousMigration);
        using var files = MigrationFiles.Create();
        var movieId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var movieFolder = files.CreateFolder("payload-guard/movie");
        await SeedMinimalMovieAsync(database, movieId, childId, movieFolder: null, videoPath: null);
        await using (var connection = await database.OpenConnectionAsync()) {
            await ExecuteAsync(
                connection,
                "INSERT INTO entity_files (id, entity_id, role, path, source, created_at, updated_at) VALUES (@file_id, @id, @role, @path, @source, now(), now())",
                ("file_id", Guid.NewGuid()), ("id", movieId), ("role", EntityFileRole.Source.ToCode()),
                ("path", movieFolder), ("source", FileSourceKind.Scan.ToCode()));
        }

        var missing = await Assert.ThrowsAsync<InvalidOperationException>(() => database.RunMigrationRunnerAsync(files.Assets));
        Assert.Contains("exactly one source file", missing.Message, StringComparison.Ordinal);

        var directoryPayload = files.CreateFolder("payload-guard/movie/not-a-file.mkv");
        await using (var connection = await database.OpenConnectionAsync()) {
            await ExecuteAsync(
                connection,
                "INSERT INTO entity_files (id, entity_id, role, path, source, created_at, updated_at) VALUES (@file_id, @id, @role, @path, @source, now(), now())",
                ("file_id", Guid.NewGuid()), ("id", childId), ("role", EntityFileRole.Source.ToCode()),
                ("path", directoryPayload), ("source", FileSourceKind.Scan.ToCode()));
        }

        var directory = await Assert.ThrowsAsync<InvalidOperationException>(() => database.RunMigrationRunnerAsync(files.Assets));
        Assert.Contains("source is a directory instead of a file", directory.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task RunnerPreservesMissingLegacyStructuralFoldersAndMoviePayloadWithoutMountingPaths() {
        await using var database = await PostgresTestDatabase.CreateAsync(PreviousMigration);
        using var files = MigrationFiles.Create();
        var movieId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var seriesId = Guid.NewGuid();
        var missingMovieFolder = Path.Combine(files.Root, "unmounted", "movies", "Movie");
        var missingSeriesFolder = Path.Combine(files.Root, "unmounted", "series", "Series");
        var missingVideoPath = Path.Combine(files.Root, "unmounted", "movies", "Movie", "movie.mkv");
        await SeedMinimalMovieAsync(database, movieId, childId, missingMovieFolder, missingVideoPath);
        await using (var connection = await database.OpenConnectionAsync()) {
            await ExecuteAsync(
                connection,
                """
                INSERT INTO entities
                    (id, kind_code, title, is_nsfw, is_organized, is_wanted, created_at, updated_at)
                VALUES (@id, @kind, 'Series', FALSE, FALSE, FALSE, now(), now());
                INSERT INTO entity_files (id, entity_id, role, path, source, created_at, updated_at)
                VALUES (@file_id, @id, @role, @path, @source, now(), now());
                """,
                ("id", seriesId),
                ("kind", EntityKind.VideoSeries.ToCode()),
                ("file_id", Guid.NewGuid()),
                ("role", EntityFileRole.Source.ToCode()),
                ("path", missingSeriesFolder),
                ("source", FileSourceKind.Scan.ToCode()));
        }

        await database.RunMigrationRunnerAsync(files.Assets);

        await using var verify = await database.OpenConnectionAsync();
        Assert.Equal(missingMovieFolder, await ScalarAsync<string>(
            verify,
            "SELECT value FROM entity_sources WHERE entity_id = @id AND code = @code",
            movieId,
            ("code", EntitySourceCode.Folder.ToCode())));
        Assert.Equal(missingSeriesFolder, await ScalarAsync<string>(
            verify,
            "SELECT value FROM entity_sources WHERE entity_id = @id AND code = @code",
            seriesId,
            ("code", EntitySourceCode.Folder.ToCode())));
        Assert.False(await ExistsAsync(
            verify,
            "SELECT EXISTS (SELECT 1 FROM entity_files WHERE entity_id = @id AND role = @role)",
            seriesId,
            ("role", EntityFileRole.Source.ToCode())));
        Assert.Equal(movieId, await ScalarAsync<Guid>(
            verify,
            "SELECT entity_id FROM entity_files WHERE path = @id",
            missingVideoPath));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task PreparerStillRejectsMissingStructuralSourceWithoutUnambiguousMetadata() {
        await using var database = await PostgresTestDatabase.CreateAsync(PreviousMigration);
        using var files = MigrationFiles.Create();
        var bookId = Guid.NewGuid();
        var missingBookPath = Path.Combine(files.Root, "unmounted", "ambiguous-book-source");
        await using (var connection = await database.OpenConnectionAsync()) {
            await ExecuteAsync(
                connection,
                """
                INSERT INTO entities
                    (id, kind_code, title, is_nsfw, is_organized, is_wanted, created_at, updated_at)
                VALUES (@id, @kind, 'Ambiguous Book', FALSE, FALSE, FALSE, now(), now());
                INSERT INTO entity_files (id, entity_id, role, path, source, created_at, updated_at)
                VALUES (@file_id, @id, @role, @path, @source, now(), now());
                """,
                ("id", bookId),
                ("kind", EntityKind.Book.ToCode()),
                ("file_id", Guid.NewGuid()),
                ("role", EntityFileRole.Source.ToCode()),
                ("path", missingBookPath),
                ("source", FileSourceKind.Scan.ToCode()));
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.RunMigrationRunnerAsync(files.Assets));

        Assert.Contains("Cannot infer legacy source", exception.Message, StringComparison.Ordinal);
        await using var verify = await database.OpenConnectionAsync();
        Assert.False(await ExistsAsync(
            verify,
            "SELECT EXISTS (SELECT 1 FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = @id)",
            MigrationUnderTest));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task DirectUpgradeRejectsMappedEpisodeThatStillOwnsAChildEntity() {
        await using var database = await PostgresTestDatabase.CreateAsync(PreviousMigration);
        using var files = MigrationFiles.Create();
        var seriesId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var descendantId = Guid.NewGuid();
        await using (var connection = await database.OpenConnectionAsync()) {
            await ExecuteAsync(
                connection,
                """
                INSERT INTO entities
                    (id, kind_code, title, parent_entity_id, is_nsfw, is_organized,
                     is_wanted, created_at, updated_at)
                VALUES
                    (@series_id, @series_kind, 'Series', NULL, FALSE, FALSE, FALSE, now(), now()),
                    (@episode_id, @video_kind, 'Legacy episode', @series_id, FALSE, FALSE, FALSE, now(), now()),
                    (@descendant_id, @person_kind, 'Unexpected child', @episode_id, FALSE, FALSE, FALSE, now(), now());
                INSERT INTO video_details (entity_id) VALUES (@episode_id);
                """,
                ("series_id", seriesId),
                ("series_kind", EntityKind.VideoSeries.ToCode()),
                ("episode_id", episodeId),
                ("video_kind", EntityKind.Video.ToCode()),
                ("descendant_id", descendantId),
                ("person_kind", EntityKind.Person.ToCode()));
        }

        var preparation = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.RunMigrationRunnerAsync(files.Assets));
        Assert.Contains("mapped legacy Video still owns child Entities", preparation.Message, StringComparison.Ordinal);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => database.MigrateAsync(MigrationUnderTest));

        Assert.Equal(PostgresErrorCodes.RaiseException, exception.SqlState);
        Assert.Contains("mapped legacy Video that still owns child Entities", exception.MessageText, StringComparison.Ordinal);
        await using var verify = await database.OpenConnectionAsync();
        Assert.Equal(EntityKind.Video.ToCode(), await ScalarAsync<string>(verify, "SELECT kind_code FROM entities WHERE id = @id", episodeId));
        Assert.False(await ExistsAsync(verify, "SELECT EXISTS (SELECT 1 FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = @id)", MigrationUnderTest));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task UpgradeRejectsMappedEpisodeWithAnActiveLifecycleClaim() {
        await using var database = await PostgresTestDatabase.CreateAsync(PreviousMigration);
        using var files = MigrationFiles.Create();
        var seriesId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        await using (var connection = await database.OpenConnectionAsync()) {
            await ExecuteAsync(
                connection,
                """
                INSERT INTO entities
                    (id, kind_code, title, parent_entity_id, is_nsfw, is_organized,
                     is_wanted, lifecycle_claim_kind, lifecycle_claim_id, lifecycle_claimed_at,
                     created_at, updated_at)
                VALUES
                    (@series_id, @series_kind, 'Series', NULL, FALSE, FALSE, FALSE,
                     NULL, NULL, NULL, now(), now()),
                    (@episode_id, @video_kind, 'Claimed legacy episode', @series_id,
                     FALSE, FALSE, FALSE, @claim_kind, @claim_id, now(), now(), now());
                INSERT INTO video_details (entity_id) VALUES (@episode_id);
                """,
                ("series_id", seriesId),
                ("series_kind", EntityKind.VideoSeries.ToCode()),
                ("episode_id", episodeId),
                ("video_kind", EntityKind.Video.ToCode()),
                ("claim_kind", EntityLifecycleClaimKind.DeletingFiles.ToCode()),
                ("claim_id", Guid.NewGuid()));
        }

        var preparation = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.RunMigrationRunnerAsync(files.Assets));
        Assert.Contains("active lifecycle claim", preparation.Message, StringComparison.Ordinal);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => database.MigrateAsync(MigrationUnderTest));
        Assert.Equal(PostgresErrorCodes.RaiseException, exception.SqlState);
        Assert.Contains("active Entity lifecycle claim", exception.MessageText, StringComparison.Ordinal);
        await using var verify = await database.OpenConnectionAsync();
        Assert.Equal(EntityKind.Video.ToCode(), await ScalarAsync<string>(verify, "SELECT kind_code FROM entities WHERE id = @id", episodeId));
        Assert.False(await ExistsAsync(verify, "SELECT EXISTS (SELECT 1 FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = @id)", MigrationUnderTest));
    }

    public static TheoryData<IdentifyQueueState> NonterminalIdentifyQueueStates => new() {
        IdentifyQueueState.Search,
        IdentifyQueueState.Queued,
        IdentifyQueueState.Searching,
        IdentifyQueueState.Proposal,
        IdentifyQueueState.Applying,
        IdentifyQueueState.Error
    };

    [Theory]
    [MemberData(nameof(NonterminalIdentifyQueueStates))]
    [Trait("Category", "PostgreSQL")]
    public async Task NonterminalIdentifyQueueItemIsRetiredAndRemainsRetryableAfterMappedVideoUpgrade(
        IdentifyQueueState state) {
        await using var database = await PostgresTestDatabase.CreateAsync(PreviousMigration);
        var seriesId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var queueItemId = Guid.NewGuid();
        var staleJson = $"{{\"EntityId\":\"{episodeId}\",\"TargetKind\":\"{EntityKind.Video.ToCode()}\"}}";
        await using (var connection = await database.OpenConnectionAsync()) {
            await ExecuteAsync(
                connection,
                """
                INSERT INTO entities
                    (id, kind_code, title, parent_entity_id, is_nsfw, is_organized,
                     is_wanted, created_at, updated_at)
                VALUES
                    (@series_id, @series_kind, 'Series', NULL, FALSE, FALSE, FALSE, now(), now()),
                    (@episode_id, @video_kind, 'Legacy episode', @series_id, FALSE, FALSE, FALSE, now(), now());
                INSERT INTO video_details (entity_id) VALUES (@episode_id);
                INSERT INTO identify_queue_items
                    (id, entity_id, state, action, query_json, candidates_json, proposal_json,
                     error, cascade_job_id, search_job_id, created_at, updated_at)
                VALUES
                    (@queue_id, @episode_id, @state, @action, @stale_json::jsonb,
                     @stale_json::jsonb, @stale_json::jsonb, 'stale failure',
                     @cascade_job_id, @search_job_id, now() - interval '1 day', now() - interval '1 day');
                """,
                ("series_id", seriesId),
                ("series_kind", EntityKind.VideoSeries.ToCode()),
                ("episode_id", episodeId),
                ("video_kind", EntityKind.Video.ToCode()),
                ("queue_id", queueItemId),
                ("state", state.ToCode()),
                ("action", IdentifyAction.Search.ToCode()),
                ("cascade_job_id", Guid.NewGuid()),
                ("search_job_id", Guid.NewGuid()),
                ("stale_json", staleJson));
        }

        await database.MigrateAsync(MigrationUnderTest);

        await using var verify = await database.OpenConnectionAsync();
        Assert.Equal(EntityKind.VideoEpisode.ToCode(), await ScalarAsync<string>(verify, "SELECT kind_code FROM entities WHERE id = @id", episodeId));
        Assert.Equal(IdentifyQueueState.Deleted.ToCode(), await ScalarAsync<string>(verify, "SELECT state FROM identify_queue_items WHERE id = @id", queueItemId));
        Assert.False(await ExistsAsync(
            verify,
            "SELECT error IS NOT NULL OR cascade_job_id IS NOT NULL OR search_job_id IS NOT NULL OR completed_at IS NULL FROM identify_queue_items WHERE id = @id",
            queueItemId));
        Assert.True(await ExistsAsync(
            verify,
            "SELECT updated_at > created_at AND completed_at = updated_at FROM identify_queue_items WHERE id = @id",
            queueItemId));
        Assert.Contains(
            episodeId.ToString(),
            await ScalarAsync<string>(verify, "SELECT proposal_json::text FROM identify_queue_items WHERE id = @id", queueItemId),
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task RetiringMappedIdentifyItemCancelsItsGraphRunsSignalsAndLeases() {
        await using var database = await PostgresTestDatabase.CreateAsync(PreviousMigration);
        var seriesId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var queueItemId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var runningRunId = Guid.NewGuid();
        var queuedRunId = Guid.NewGuid();
        var unresolvedSignalId = Guid.NewGuid();
        var resolvedSignalId = Guid.NewGuid();
        var resourceKey = JobResourceKeys.Entity(episodeId.ToString());
        await using (var connection = await database.OpenConnectionAsync()) {
            await ExecuteAsync(
                connection,
                """
                INSERT INTO entities
                    (id, kind_code, title, parent_entity_id, is_nsfw, is_organized,
                     is_wanted, created_at, updated_at)
                VALUES
                    (@series_id, @series_kind, 'Series', NULL, FALSE, FALSE, FALSE, now(), now()),
                    (@episode_id, @video_kind, 'Legacy episode', @series_id, FALSE, FALSE, FALSE, now(), now());
                INSERT INTO video_details (entity_id) VALUES (@episode_id);
                INSERT INTO job_graphs
                    (id, origin, status, display_name, root_run_id, root_entity_kind,
                     root_entity_id, cancellation_requested, created_at, updated_at)
                VALUES
                    (@graph_id, @origin, @graph_status, 'Identify retirement', @running_run_id,
                     @video_kind, @episode_id_text, FALSE, now(), now());
                INSERT INTO job_runs
                    (id, type, status, payload_json, attempts, max_attempts, progress,
                     target_entity_kind, target_entity_id, available_at, locked_at, locked_by,
                     created_at, graph_id, resource_key)
                VALUES
                    (@running_run_id, @search_type, @running_status, '{}'::jsonb, 0, 3, 0,
                     @video_kind, @episode_id_text, now(), now(), 'fixture-worker', now(),
                     @graph_id, @resource_key),
                    (@queued_run_id, @provider_type, @queued_status, '{}'::jsonb, 0, 3, 0,
                     @video_kind, @episode_id_text, now(), NULL, NULL, now(),
                     @graph_id, @resource_key);
                INSERT INTO job_graph_signals
                    (id, graph_id, key, kind, created_at, resolved_at)
                VALUES
                    (@unresolved_signal_id, @graph_id, 'review', @signal_kind, now(), NULL),
                    (@resolved_signal_id, @graph_id, 'resolved', @signal_kind, now(), now());
                INSERT INTO job_resource_states
                    (key, max_concurrency, minimum_start_interval_ms, next_available_at, updated_at)
                VALUES (@resource_key, 1, 0, now(), now());
                INSERT INTO job_resource_leases (resource_key, job_run_id, expires_at)
                VALUES
                    (@resource_key, @running_run_id, now() + interval '1 hour'),
                    (@resource_key, @queued_run_id, now() + interval '1 hour');
                INSERT INTO identify_queue_items
                    (id, entity_id, job_graph_id, state, action, error, cascade_job_id,
                     search_job_id, created_at, updated_at)
                VALUES
                    (@queue_id, @episode_id, NULL, @identify_state, @identify_action,
                     'stale failure', @queued_run_id, @running_run_id, now(), now());
                """,
                ("series_id", seriesId),
                ("series_kind", EntityKind.VideoSeries.ToCode()),
                ("episode_id", episodeId),
                ("episode_id_text", episodeId.ToString()),
                ("video_kind", EntityKind.Video.ToCode()),
                ("queue_id", queueItemId),
                ("graph_id", graphId),
                ("running_run_id", runningRunId),
                ("queued_run_id", queuedRunId),
                ("unresolved_signal_id", unresolvedSignalId),
                ("resolved_signal_id", resolvedSignalId),
                ("resource_key", resourceKey),
                ("origin", JobGraphOrigin.Interactive.ToCode()),
                ("graph_status", JobGraphStatus.Running.ToCode()),
                ("search_type", JobType.IdentifySearch.ToCode()),
                ("provider_type", JobType.IdentifyProviderCall.ToCode()),
                ("running_status", JobRunStatus.Running.ToCode()),
                ("queued_status", JobRunStatus.Queued.ToCode()),
                ("signal_kind", JobGraphSignalKind.IdentifyReview.ToCode()),
                ("identify_state", IdentifyQueueState.Applying.ToCode()),
                ("identify_action", IdentifyAction.Search.ToCode()));
        }

        await database.MigrateAsync(MigrationUnderTest);

        await using var verify = await database.OpenConnectionAsync();
        Assert.Equal(IdentifyQueueState.Deleted.ToCode(), await ScalarAsync<string>(verify, "SELECT state FROM identify_queue_items WHERE id = @id", queueItemId));
        Assert.False(await ExistsAsync(
            verify,
            "SELECT error IS NOT NULL OR cascade_job_id IS NOT NULL OR search_job_id IS NOT NULL OR completed_at IS NULL FROM identify_queue_items WHERE id = @id",
            queueItemId));
        Assert.True(await ExistsAsync(
            verify,
            "SELECT cancellation_requested AND status = @status AND finished_at IS NOT NULL FROM job_graphs WHERE id = @id",
            graphId,
            ("status", JobGraphStatus.Cancelled.ToCode())));
        Assert.Equal(2, await ScalarAsync<int>(
            verify,
            "SELECT count(*)::int FROM job_runs WHERE graph_id = @id AND status = @status AND message = 'Cancelled with graph.' AND locked_at IS NULL AND locked_by IS NULL AND finished_at IS NOT NULL",
            graphId,
            ("status", JobRunStatus.Cancelled.ToCode())));
        Assert.True(await ExistsAsync(verify, "SELECT cancelled_at IS NOT NULL FROM job_graph_signals WHERE id = @id", unresolvedSignalId));
        Assert.False(await ExistsAsync(verify, "SELECT cancelled_at IS NOT NULL FROM job_graph_signals WHERE id = @id", resolvedSignalId));
        Assert.Equal(0, await ScalarAsync<int>(verify, "SELECT count(*)::int FROM job_resource_leases WHERE job_run_id IN (@first_id, @second_id)", id: null, ("first_id", runningRunId), ("second_id", queuedRunId)));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task MovieIdentifyCollisionPreservesExistingMovieSurvivorAndRemovesLegacyChild() {
        await using var database = await PostgresTestDatabase.CreateAsync(PreviousMigration);
        using var files = MigrationFiles.Create();
        var movieId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var survivorId = Guid.NewGuid();
        var childQueueId = Guid.NewGuid();
        var movieFolder = files.CreateFolder("identify-collision/movie");
        var videoPath = files.CreateFile("identify-collision/movie/movie.mkv", "movie");
        await SeedMinimalMovieAsync(database, movieId, childId, movieFolder, videoPath);
        await using (var connection = await database.OpenConnectionAsync()) {
            await ExecuteAsync(
                connection,
                """
                INSERT INTO identify_queue_items
                    (id, entity_id, state, action, proposal_json, created_at, updated_at, completed_at)
                VALUES
                    (@survivor_id, @movie_id, @done_state, @action,
                     '{"source":"movie-survivor"}'::jsonb, now(), now(), now()),
                    (@child_queue_id, @child_id, @proposal_state, @action,
                     '{"source":"legacy-child"}'::jsonb, now(), now(), NULL);
                """,
                ("survivor_id", survivorId),
                ("child_queue_id", childQueueId),
                ("movie_id", movieId),
                ("child_id", childId),
                ("done_state", IdentifyQueueState.Done.ToCode()),
                ("proposal_state", IdentifyQueueState.Proposal.ToCode()),
                ("action", IdentifyAction.Search.ToCode()));
        }

        await database.RunMigrationRunnerAsync(files.Assets);

        await using var verify = await database.OpenConnectionAsync();
        Assert.Equal(1, await ScalarAsync<int>(verify, "SELECT count(*)::int FROM identify_queue_items WHERE entity_id = @id", movieId));
        Assert.Equal(survivorId, await ScalarAsync<Guid>(verify, "SELECT id FROM identify_queue_items WHERE entity_id = @id", movieId));
        Assert.Equal(IdentifyQueueState.Done.ToCode(), await ScalarAsync<string>(verify, "SELECT state FROM identify_queue_items WHERE id = @id", survivorId));
        Assert.Contains(
            "movie-survivor",
            await ScalarAsync<string>(verify, "SELECT proposal_json::text FROM identify_queue_items WHERE id = @id", survivorId),
            StringComparison.Ordinal);
        Assert.False(await ExistsAsync(verify, "SELECT EXISTS (SELECT 1 FROM identify_queue_items WHERE id = @id)", childQueueId));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task RetainedTelevisionCheckpointBlocksMovieCollapseProtocolChange() {
        await using var database = await PostgresTestDatabase.CreateAsync(PreviousMigration);
        using var files = MigrationFiles.Create();
        var movieId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var movieFolder = files.CreateFolder("checkpoint-guard/movie");
        var videoPath = files.CreateFile("checkpoint-guard/movie/movie.mkv", "movie");
        await SeedMinimalMovieAsync(database, movieId, childId, movieFolder, videoPath);
        var checkpoint = CreateTelevisionCheckpoint(Guid.NewGuid(), movieFolder, videoPath);
        var checkpointJson = TvImportCheckpointJson.Serialize(checkpoint);
        var resultJson = AcquisitionImportFileLedgerJson.Serialize(
            AcquisitionImportFileLedger.Create(checkpoint, files.Root));
        var acquisitionId = Guid.NewGuid();
        await using (var connection = await database.OpenConnectionAsync()) {
            await ExecuteAsync(
                connection,
                """
                INSERT INTO acquisitions
                    (id, status, title, external_ids_json, source_urls_json, created_at, updated_at,
                     kind, entity_id, tv_import_checkpoint_json, import_result_json)
                VALUES
                    (@id, @status, 'Interrupted legacy Movie import', '{}'::jsonb, '[]'::jsonb,
                     now(), now(), @kind, @entity_id, @checkpoint::jsonb, @result::jsonb);
                """,
                ("id", acquisitionId),
                ("status", AcquisitionStatus.Failed.ToCode()),
                ("kind", EntityKind.Video.ToCode()),
                ("entity_id", childId),
                ("checkpoint", checkpointJson),
                ("result", resultJson));
        }

        var exception = await Assert.ThrowsAsync<PostgresException>(() => database.RunMigrationRunnerAsync(files.Assets));

        Assert.Equal(PostgresErrorCodes.RaiseException, exception.SqlState);
        Assert.Contains("retained import checkpoint whose protocol would change", exception.MessageText, StringComparison.Ordinal);
        await using var verify = await database.OpenConnectionAsync();
        Assert.Equal(EntityKind.Video.ToCode(), await ScalarAsync<string>(verify, "SELECT kind FROM acquisitions WHERE id = @id", acquisitionId));
        Assert.NotNull(TvImportCheckpointJson.Deserialize(await ScalarAsync<string>(
            verify,
            "SELECT tv_import_checkpoint_json::text FROM acquisitions WHERE id = @id",
            acquisitionId)));
        Assert.True(await ExistsAsync(verify, "SELECT EXISTS (SELECT 1 FROM entities WHERE id = @id)", childId));
        Assert.False(await ExistsAsync(verify, "SELECT EXISTS (SELECT 1 FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = @id)", MigrationUnderTest));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task FreshDatabaseCanMigrateWithoutAFilesystemManifest() {
        await using var database = await PostgresTestDatabase.CreateAsync(MigrationUnderTest);
        using var files = MigrationFiles.Create();

        await database.RunMigrationRunnerAsync(files.Assets);

        await using var connection = await database.OpenConnectionAsync();
        Assert.False(await ExistsAsync(connection, "SELECT to_regclass('public.video_details') IS NOT NULL"));
        Assert.Equal(EntityStorageShape.File.ToCode(), await ScalarAsync<string>(connection, "SELECT storage_shape FROM entity_kinds WHERE code = @id", EntityKind.Movie.ToCode()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task RunnerBackfillsSourceOwnershipToMostSpecificConfiguredRoot() {
        await using var database = await PostgresTestDatabase.CreateAsync(PreviousMigration);
        using var files = MigrationFiles.Create();
        var outerRootId = Guid.NewGuid();
        var nestedDisabledRootId = Guid.NewGuid();
        var parentGalleryId = Guid.NewGuid();
        var nestedImageId = Guid.NewGuid();
        var looseTrackId = Guid.NewGuid();
        var preservedImageId = Guid.NewGuid();
        var outerPath = files.CreateFolder("ownership");
        var nestedPath = files.CreateFolder("ownership/private");
        var nestedImagePath = files.CreateFile("ownership/private/nested.jpg", "image");
        var looseTrackPath = files.CreateFile("ownership/private/loose.flac", "audio");
        var preservedImagePath = files.CreateFile("ownership/private/preserved.jpg", "preserved");

        await using (var connection = await database.OpenConnectionAsync()) {
            await ExecuteAsync(
                connection,
                """
                INSERT INTO library_roots
                    (id, path, label, enabled, recursive, scan_videos, scan_images, scan_audio,
                     scan_books, is_nsfw, auto_identify, created_at, updated_at)
                VALUES
                    (@outer_root_id, @outer_path, 'Outer', TRUE, TRUE, FALSE, TRUE, TRUE,
                     FALSE, FALSE, FALSE, now(), now()),
                    (@nested_root_id, @nested_path, 'Nested disabled', FALSE, TRUE, FALSE, FALSE, FALSE,
                     FALSE, FALSE, FALSE, now(), now());

                INSERT INTO entities
                    (id, kind_code, title, parent_entity_id, is_nsfw, is_organized, is_wanted,
                     created_at, updated_at)
                VALUES
                    (@gallery_id, @gallery_kind, 'Outer Gallery', NULL, FALSE, FALSE, FALSE, now(), now()),
                    (@image_id, @image_kind, 'Nested Image', @gallery_id, FALSE, FALSE, FALSE, now(), now()),
                    (@track_id, @track_kind, 'Loose Track', NULL, FALSE, FALSE, FALSE, now(), now()),
                    (@preserved_image_id, @image_kind, 'Preserved Image', NULL, FALSE, FALSE, FALSE, now(), now());

                INSERT INTO audio_track_details (entity_id) VALUES (@track_id);
                INSERT INTO entity_library_roots (entity_id, library_root_id)
                VALUES
                    (@gallery_id, @outer_root_id),
                    (@preserved_image_id, @outer_root_id);
                INSERT INTO entity_files (id, entity_id, role, path, source, created_at, updated_at)
                VALUES
                    (@image_file_id, @image_id, @source_role, @image_path, @scan_source, now(), now()),
                    (@track_file_id, @track_id, @source_role, @track_path, @scan_source, now(), now()),
                    (@preserved_file_id, @preserved_image_id, @source_role, @preserved_path, @scan_source, now(), now());
                """,
                ("outer_root_id", outerRootId),
                ("outer_path", outerPath),
                ("nested_root_id", nestedDisabledRootId),
                ("nested_path", nestedPath),
                ("gallery_id", parentGalleryId),
                ("gallery_kind", EntityKind.Gallery.ToCode()),
                ("image_id", nestedImageId),
                ("preserved_image_id", preservedImageId),
                ("image_kind", EntityKind.Image.ToCode()),
                ("track_id", looseTrackId),
                ("track_kind", EntityKind.AudioTrack.ToCode()),
                ("image_file_id", Guid.NewGuid()),
                ("track_file_id", Guid.NewGuid()),
                ("preserved_file_id", Guid.NewGuid()),
                ("source_role", EntityFileRole.Source.ToCode()),
                ("scan_source", FileSourceKind.Scan.ToCode()),
                ("image_path", nestedImagePath),
                ("track_path", looseTrackPath),
                ("preserved_path", preservedImagePath));
        }

        await database.RunMigrationRunnerAsync(files.Assets);

        await using var verify = await database.OpenConnectionAsync();
        Assert.Equal(nestedDisabledRootId, await ScalarAsync<Guid>(
            verify, "SELECT library_root_id FROM entity_library_roots WHERE entity_id = @id", nestedImageId));
        Assert.Equal(nestedDisabledRootId, await ScalarAsync<Guid>(
            verify, "SELECT library_root_id FROM entity_library_roots WHERE entity_id = @id", looseTrackId));
        Assert.Equal(outerRootId, await ScalarAsync<Guid>(
            verify, "SELECT library_root_id FROM entity_library_roots WHERE entity_id = @id", preservedImageId));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task RunnerRejectsSourceOutsideConfiguredRootsBeforeApplyingMigration() {
        await using var database = await PostgresTestDatabase.CreateAsync(PreviousMigration);
        using var files = MigrationFiles.Create();
        var rootId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var rootPath = files.CreateFolder("configured-root");
        var sourcePath = files.CreateFile("outside-root/image.jpg", "image");
        await using (var connection = await database.OpenConnectionAsync()) {
            await ExecuteAsync(
                connection,
                """
                INSERT INTO library_roots
                    (id, path, label, enabled, recursive, scan_videos, scan_images, scan_audio,
                     scan_books, is_nsfw, auto_identify, created_at, updated_at)
                VALUES (@root_id, @root_path, 'Root', TRUE, TRUE, FALSE, TRUE, FALSE,
                        FALSE, FALSE, FALSE, now(), now());
                INSERT INTO entities
                    (id, kind_code, title, is_nsfw, is_organized, is_wanted, created_at, updated_at)
                VALUES (@image_id, @image_kind, 'Outside root', FALSE, FALSE, FALSE, now(), now());
                INSERT INTO entity_files (id, entity_id, role, path, source, created_at, updated_at)
                VALUES (@file_id, @image_id, @source_role, @source_path, @scan_source, now(), now());
                """,
                ("root_id", rootId),
                ("root_path", rootPath),
                ("image_id", imageId),
                ("image_kind", EntityKind.Image.ToCode()),
                ("file_id", Guid.NewGuid()),
                ("source_role", EntityFileRole.Source.ToCode()),
                ("scan_source", FileSourceKind.Scan.ToCode()),
                ("source_path", sourcePath));
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.RunMigrationRunnerAsync(files.Assets));

        Assert.Contains("outside every configured library root", exception.Message, StringComparison.Ordinal);
        await using var verify = await database.OpenConnectionAsync();
        Assert.False(await ExistsAsync(
            verify,
            "SELECT EXISTS (SELECT 1 FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = @id)",
            MigrationUnderTest));
        Assert.False(await ExistsAsync(
            verify,
            "SELECT EXISTS (SELECT 1 FROM entity_library_roots WHERE entity_id = @id)",
            imageId));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task MigrationRejectsLibraryRootChangesAfterManifestPreparation() {
        await using var database = await PostgresTestDatabase.CreateAsync(PreviousMigration);
        using var files = MigrationFiles.Create();
        var rootId = Guid.NewGuid();
        var originalPath = files.CreateFolder("snapshot-original");
        var changedPath = files.CreateFolder("snapshot-changed");
        await using (var connection = await database.OpenConnectionAsync()) {
            await ExecuteAsync(
                connection,
                """
                INSERT INTO library_roots
                    (id, path, label, enabled, recursive, scan_videos, scan_images, scan_audio,
                     scan_books, is_nsfw, auto_identify, created_at, updated_at)
                VALUES (@root_id, @root_path, 'Root', TRUE, TRUE, FALSE, TRUE, TRUE,
                        FALSE, FALSE, FALSE, now(), now());
                """,
                ("root_id", rootId),
                ("root_path", originalPath));
        }

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            database.PrepareChangeRootAndMigrateAsync(files.Assets, rootId, changedPath));

        Assert.Contains("library-root snapshot", exception.Message, StringComparison.Ordinal);
        await using var verify = await database.OpenConnectionAsync();
        Assert.False(await ExistsAsync(
            verify,
            "SELECT EXISTS (SELECT 1 FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = @id)",
            MigrationUnderTest));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task RunnerRejectsUnknownMigrationHistoryBeforeApplyingPendingMigrations() {
        await using var database = await PostgresTestDatabase.CreateAsync(PreviousMigration);
        using var files = MigrationFiles.Create();
        const string unknownMigration = "99999999999999_UnknownFixtureMigration";
        await using (var connection = await database.OpenConnectionAsync()) {
            await ExecuteAsync(
                connection,
                "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES (@migration_id, '10.0.8')",
                ("migration_id", unknownMigration));
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.RunMigrationRunnerAsync(files.Assets));

        Assert.Contains("unknown to this Prismedia build", exception.Message, StringComparison.Ordinal);
        await using var verify = await database.OpenConnectionAsync();
        Assert.False(await ExistsAsync(verify, "SELECT EXISTS (SELECT 1 FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = @id)", MigrationUnderTest));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task RunnerRejectsNonPrefixKnownMigrationHistoryBeforeApplyingPendingMigrations() {
        await using var database = await PostgresTestDatabase.CreateAsync(PreviousMigration);
        using var files = MigrationFiles.Create();
        await using (var connection = await database.OpenConnectionAsync()) {
            await ExecuteAsync(
                connection,
                """
                DELETE FROM "__EFMigrationsHistory"
                WHERE "MigrationId" = (
                    SELECT "MigrationId"
                    FROM "__EFMigrationsHistory"
                    ORDER BY "MigrationId"
                    LIMIT 1);
                """);
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.RunMigrationRunnerAsync(files.Assets));

        Assert.Contains("not a prefix", exception.Message, StringComparison.Ordinal);
        await using var verify = await database.OpenConnectionAsync();
        Assert.False(await ExistsAsync(verify, "SELECT EXISTS (SELECT 1 FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = @id)", MigrationUnderTest));
    }

    private static async Task SeedPopulatedLegacyLibraryAsync(PostgresTestDatabase database, MigrationFixture fixture) {
        await using var connection = await database.OpenConnectionAsync();
        await ExecuteAsync(
            connection,
            """
            INSERT INTO users
                (id, username, normalized_username, display_name, allow_nsfw, enabled, role,
                 can_create_libraries, created_at, updated_at)
            VALUES (@user_id, 'migration-user', 'MIGRATION-USER', 'Migration User', TRUE, TRUE,
                    @user_role, TRUE, now(), now());

            INSERT INTO library_roots
                (id, path, label, enabled, recursive, scan_videos, scan_images, scan_audio,
                 scan_books, is_nsfw, auto_identify, created_at, updated_at)
            VALUES (@root_id, @root_path, 'Migration Root', TRUE, TRUE, TRUE, FALSE, FALSE,
                    TRUE, FALSE, FALSE, now(), now());

            INSERT INTO entities
                (id, kind_code, title, parent_entity_id, sort_order, is_nsfw,
                 is_organized, is_wanted, created_at, updated_at)
            VALUES
                (@movie_id, @movie_kind, 'Direct Movie', NULL, NULL, FALSE, TRUE, FALSE, now(), now()),
                (@movie_video_id, @video_kind, 'Legacy Payload', @movie_id, NULL, FALSE, TRUE, FALSE, now(), now()),
                (@series_id, @series_kind, 'Series', NULL, NULL, FALSE, TRUE, FALSE, now(), now()),
                (@season_id, @season_kind, 'Season 1', @series_id, 1, FALSE, TRUE, FALSE, now(), now()),
                (@episode_one_id, @video_kind, 'Episode 5', @season_id, 5, FALSE, TRUE, FALSE, now(), now()),
                (@episode_two_id, @video_kind, 'Episode 6', @season_id, 6, FALSE, TRUE, FALSE, now(), now()),
                (@standalone_id, @video_kind, 'Standalone', NULL, NULL, FALSE, TRUE, FALSE, now(), now()),
                (@person_id, @person_kind, 'Actor', NULL, NULL, FALSE, TRUE, FALSE, now(), now()),
                (@collection_id, @collection_kind, 'Collection', NULL, NULL, FALSE, TRUE, FALSE, now(), now()),
                (@folder_book_id, @book_kind, 'Folder Book', NULL, NULL, FALSE, TRUE, FALSE, now(), now()),
                (@file_book_id, @book_kind, 'File Book', NULL, NULL, FALSE, TRUE, FALSE, now(), now());

            INSERT INTO video_details (entity_id)
            VALUES (@movie_video_id), (@episode_one_id), (@episode_two_id), (@standalone_id);

            INSERT INTO entity_files (id, entity_id, role, path, source, created_at, updated_at)
            VALUES
                (@movie_folder_file_id, @movie_id, @source_role, @movie_folder, @scan_source, now(), now()),
                (@movie_source_file_id, @movie_video_id, @source_role, @movie_source_path, @scan_source, now(), now()),
                (@movie_thumbnail_file_id, @movie_video_id, @thumbnail_role, '/assets/videos/legacy/thumbnail.jpg', @scan_source, now(), now()),
                (@artwork_file_id, @movie_video_id, @poster_role, @artwork_url, @custom_source, now(), now()),
                (@series_folder_file_id, @series_id, @source_role, @series_folder, @scan_source, now(), now()),
                (@season_folder_file_id, @season_id, @source_role, @season_folder, @scan_source, now(), now()),
                (@episode_one_file_id, @episode_one_id, @source_role, @shared_path, @scan_source, now(), now()),
                (@episode_two_file_id, @episode_two_id, @source_role, @shared_path, @scan_source, now(), now()),
                (@standalone_file_id, @standalone_id, @source_role, @standalone_path, @scan_source, now(), now()),
                (@folder_book_file_id, @folder_book_id, @source_role, @book_folder, @scan_source, now(), now()),
                (@file_book_file_id, @file_book_id, @source_role, @file_book_path, @scan_source, now(), now());

            INSERT INTO entity_library_roots (entity_id, library_root_id)
            VALUES (@movie_video_id, @root_id), (@episode_one_id, @root_id), (@episode_two_id, @root_id), (@standalone_id, @root_id);
            INSERT INTO entity_technical (entity_id, duration_seconds, width, height, updated_at)
            VALUES (@movie_video_id, 123, 1920, 1080, now());
            INSERT INTO entity_markers (id, entity_id, title, seconds, created_at, updated_at)
            VALUES (@marker_id, @movie_video_id, 'Opening', 5, now(), now());
            INSERT INTO entity_subtitle_states (entity_id, subtitles_extracted_at, subtitle_sidecar_signature)
            VALUES (@movie_video_id, now(), 'fixture-signature');
            INSERT INTO entity_subtitles
                (id, entity_id, language, format, source, source_key, storage_path, source_format,
                 source_path, is_default, created_at)
            VALUES
                (@subtitle_id, @movie_video_id, 'eng', @subtitle_format, @subtitle_source, 'fixture',
                 @subtitle_path, @subtitle_source_format, @subtitle_source_path, TRUE, now());

            INSERT INTO media_sources
                (id, entity_id, entity_file_id, path, protocol, created_at, updated_at)
            VALUES (@media_source_id, @movie_video_id, @movie_source_file_id, @movie_source_path, 'file', now(), now());
            INSERT INTO media_streams
                (id, media_source_id, entity_id, stream_index, type, hdr10_plus_present_flag,
                 is_default, is_forced, created_at)
            VALUES (@media_stream_id, @media_source_id, @movie_video_id, 0, 'video', FALSE, TRUE, FALSE, now());

            INSERT INTO user_entity_states
                (user_id, entity_id, is_favorite, play_count, skip_count, play_duration_seconds,
                 resume_seconds, rating_value, last_played_at, completed_at,
                 progress_current_entity_id, progress_unit, progress_index, progress_total,
                 progress_mode, progress_location, updated_at)
            VALUES
                (@user_id, @movie_id, FALSE, 7, 1, 90, 5, 9, now() - interval '2 hours', NULL,
                 @movie_video_id, @progress_unit, 5, 123, NULL, 'parent', now() - interval '2 hours'),
                (@user_id, @movie_video_id, TRUE, 2, 0, 120, 30, 3, now() - interval '1 hour', now() - interval '1 hour',
                 @movie_video_id, @progress_unit, 30, 123, NULL, 'child', now());
            INSERT INTO entity_external_ids (id, entity_id, provider, value, created_at, updated_at)
            VALUES (@external_id_id, @movie_video_id, 'fixture-provider', 'movie-1', now(), now());

            INSERT INTO entity_relationship_links
                (entity_id, relationship_code, target_entity_id, label, target_kind_code,
                 sort_order, metadata_json, created_at)
            VALUES
                (@movie_video_id, @cast_relationship, @person_id, 'Actor', @person_kind, 0,
                 @credit_metadata::jsonb,
                 now()),
                (@movie_video_id, @related_relationship, @collection_id, 'Unrelated', @collection_kind, 1,
                 @unrelated_metadata::jsonb,
                 now());

            INSERT INTO collection_details
                (entity_id, mode, rule_tree_json, cover_mode, cover_item_entity_id,
                 is_shared, owner_user_id)
            VALUES
                (@collection_id, @collection_mode,
                 @collection_rule::jsonb,
                 @collection_cover_mode, @movie_video_id, FALSE, @user_id);
            INSERT INTO collection_item_details
                (id, collection_entity_id, item_entity_id, source, sort_order, added_at)
            VALUES (@collection_item_id, @collection_id, @movie_video_id, @collection_item_source, 0, now());

            INSERT INTO app_settings (key, value_json, created_at, updated_at)
            VALUES (@default_providers_key, @default_providers::jsonb, now(), now());

            INSERT INTO job_graphs
                (id, origin, status, display_name, root_run_id, root_entity_kind, root_entity_id,
                 active_key, cancellation_requested, created_at, updated_at)
            VALUES
                (@job_graph_id, @job_origin, @graph_status, 'Fixture', @job_run_id, @video_kind,
                 @movie_video_id_text, @job_active_key, FALSE, now(), now());
            INSERT INTO job_resource_states
                (key, max_concurrency, minimum_start_interval_ms, next_available_at, updated_at)
            VALUES (@job_resource_key, 1, 0, now(), now());
            INSERT INTO job_runs
                (id, type, status, payload_json, attempts, max_attempts, progress,
                 target_entity_kind, target_entity_id, available_at, created_at, graph_id,
                 node_key, resource_key)
            VALUES
                (@job_run_id, @job_type, @run_status, @job_payload::jsonb, 0, 3, 0,
                 @video_kind, @movie_video_id_text, now(), now(), @job_graph_id,
                 @job_active_key, @job_resource_key),
                (@mixed_job_run_id, @job_type, @run_status, @mixed_job_payload::jsonb, 0, 3, 0,
                 NULL, NULL, now(), now(), @job_graph_id, 'mixed-fixture', NULL);

            INSERT INTO acquisitions
                (id, status, title, external_ids_json, source_urls_json, created_at, updated_at,
                 kind, entity_id)
            VALUES (@acquisition_id, @acquisition_status, 'Fixture', '{}'::jsonb, '[]'::jsonb, now(), now(), @video_kind, @movie_video_id);
            INSERT INTO acquisition_history (id, acquisition_id, entity_id, kind, event, title, created_at)
            VALUES (@history_id, @acquisition_id, @movie_video_id, @video_kind, @history_event, 'Fixture', now());
            INSERT INTO acquisition_import_hints
                (id, acquisition_id, source_path, external_ids_json, source_urls_json, consumed,
                 created_at, updated_at, entity_id)
            VALUES (@hint_id, @acquisition_id, @movie_source_path, '{}'::jsonb, '[]'::jsonb,
                    FALSE, now(), now(), @movie_video_id);
            INSERT INTO monitors (id, kind, acquisition_id, status, title, created_at, updated_at, entity_id)
            VALUES (@monitor_id, @video_kind, @acquisition_id, @monitor_status, 'Fixture', now(), now(), @movie_video_id);
            """,
            fixture.Parameters());
    }

    private static async Task<MigrationAuditFixture> SeedMappedAuditStateAsync(
        PostgresTestDatabase database,
        MigrationFixture fixture) {
        var checkpoint = CreateTelevisionCheckpoint(
            fixture.LibraryRootId,
            fixture.SeriesFolder,
            fixture.SharedEpisodePath);
        var checkpointJson = TvImportCheckpointJson.Serialize(checkpoint);
        var episodeResultJson = AcquisitionImportFileLedgerJson.Serialize(
            AcquisitionImportFileLedger.Create(checkpoint, Path.GetDirectoryName(fixture.SeriesFolder)!));
        var movieCheckpoint = CreateMoviePlacementCheckpoint(fixture);
        var movieCheckpointJson = ImportPlacementCheckpointJson.Serialize(movieCheckpoint);
        var movieResultJson = AcquisitionImportFileLedgerJson.Serialize(
            AcquisitionImportFileLedger.Create(movieCheckpoint).Complete());
        var audit = new MigrationAuditFixture(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());
        var doneProposal = $"{{\"ProposalId\":\"movie-audit\",\"TargetKind\":\"{EntityKind.Video.ToCode()}\",\"EntityId\":\"{fixture.LegacyMovieVideoId}\"}}";
        var deletedProposal = $"{{\"ProposalId\":\"episode-audit\",\"TargetKind\":\"{EntityKind.Video.ToCode()}\",\"EntityId\":\"{fixture.FirstEpisodeId}\"}}";

        await using var connection = await database.OpenConnectionAsync();
        await ExecuteAsync(
            connection,
            """
            UPDATE acquisitions
            SET tv_import_checkpoint_json = @movie_checkpoint::jsonb,
                import_result_json = @movie_result::jsonb
            WHERE id = @movie_acquisition_id;
            INSERT INTO acquisitions
                (id, status, title, external_ids_json, source_urls_json, created_at, updated_at,
                 kind, entity_id, tv_import_checkpoint_json, import_result_json)
            VALUES
                (@episode_acquisition_id, @failed_status, 'Interrupted episode import', '{}'::jsonb,
                 '[]'::jsonb, now(), now(), @video_kind, @episode_id,
                 @checkpoint::jsonb, @episode_result::jsonb);
            INSERT INTO identify_queue_items
                (id, entity_id, state, action, proposal_json, created_at, updated_at, completed_at)
            VALUES
                (@done_queue_id, @movie_video_id, @done_state, @identify_action,
                 @done_proposal::jsonb, now(), now(), now()),
                (@deleted_queue_id, @episode_id, @deleted_state, @identify_action,
                 @deleted_proposal::jsonb, now(), now(), now());
            INSERT INTO identify_results
                (id, entity_id, action, status, raw_result_json, proposed_result_json,
                 created_at, updated_at, applied_at)
            VALUES
                (@identify_result_id, @movie_video_id, @identify_action, @applied_status,
                 @done_proposal::jsonb, @done_proposal::jsonb, now(), now(), now());
            """,
            ("movie_checkpoint", movieCheckpointJson),
            ("movie_result", movieResultJson),
            ("movie_acquisition_id", fixture.AcquisitionId),
            ("episode_acquisition_id", audit.EpisodeAcquisitionId),
            ("failed_status", AcquisitionStatus.Failed.ToCode()),
            ("video_kind", EntityKind.Video.ToCode()),
            ("episode_id", fixture.FirstEpisodeId),
            ("checkpoint", checkpointJson),
            ("episode_result", episodeResultJson),
            ("done_queue_id", audit.DoneQueueItemId),
            ("deleted_queue_id", audit.DeletedQueueItemId),
            ("movie_video_id", fixture.LegacyMovieVideoId),
            ("done_state", IdentifyQueueState.Done.ToCode()),
            ("deleted_state", IdentifyQueueState.Deleted.ToCode()),
            ("identify_action", IdentifyAction.Search.ToCode()),
            ("done_proposal", doneProposal),
            ("deleted_proposal", deletedProposal),
            ("identify_result_id", audit.IdentifyResultId),
            ("applied_status", IdentifyResultStatus.Applied.ToCode()));
        return audit;
    }

    private static ImportPlacementCheckpoint CreateMoviePlacementCheckpoint(MigrationFixture fixture) => new(
        EntityKind.Movie,
        fixture.LibraryRootId,
        Path.GetDirectoryName(fixture.MovieFolder)!,
        fixture.MovieFolder,
        ImportMode.Move,
        fixture.MovieSourcePath,
        fixture.MovieSourcePath,
        "Resume the exact Movie placement.",
        [new ImportPlacementCheckpointUnit(
            Path.GetFileName(fixture.MovieSourcePath),
            fixture.MovieSourcePath,
            fixture.MovieSourcePath,
            IsMedia: true,
            FinalPath: fixture.MovieSourcePath)],
        AttemptId: Guid.NewGuid(),
        ClaimJobId: Guid.NewGuid());

    private static TvImportCheckpoint CreateTelevisionCheckpoint(
        Guid libraryRootId,
        string seriesFolder,
        string targetPath) => new(
            libraryRootId,
            seriesFolder,
            ImportMode.Move,
            AllowFormatChange: false,
            SuccessMessage: "Resume the exact legacy placement.",
            PreferSingleFileFinalSource: true,
            Units: [new TvImportCheckpointUnit(
                Path.GetFileName(targetPath),
                targetPath,
                SeasonNumber: 1,
                EpisodeNumber: 1,
                CoveredEpisodeNumbers: [],
                FinalPath: targetPath,
                SourceAbsolutePath: targetPath,
                AdoptedExistingTarget: true)],
            AttemptId: Guid.NewGuid(),
            ClaimJobId: Guid.NewGuid()) {
            LibraryRootPath = Path.GetDirectoryName(seriesFolder)
        };

    private static async Task SeedMinimalMovieAsync(
        PostgresTestDatabase database,
        Guid movieId,
        Guid childId,
        string? movieFolder,
        string? videoPath) {
        await using var connection = await database.OpenConnectionAsync();
        await ExecuteAsync(
            connection,
            """
            INSERT INTO entities
                (id, kind_code, title, parent_entity_id, is_nsfw, is_organized,
                 is_wanted, created_at, updated_at)
            VALUES
                (@movie_id, @movie_kind, 'Movie', NULL, FALSE, FALSE, FALSE, now(), now()),
                (@child_id, @video_kind, 'Payload', @movie_id, FALSE, FALSE, FALSE, now(), now());
            INSERT INTO video_details (entity_id) VALUES (@child_id);
            """,
            ("movie_id", movieId), ("movie_kind", EntityKind.Movie.ToCode()),
            ("child_id", childId), ("video_kind", EntityKind.Video.ToCode()));
        if (movieFolder is null || videoPath is null) return;
        await ExecuteAsync(
            connection,
            """
            INSERT INTO entity_files (id, entity_id, role, path, source, created_at, updated_at)
            VALUES
                (@movie_file_id, @movie_id, @role, @movie_path, @source, now(), now()),
                (@video_file_id, @child_id, @role, @video_path, @source, now(), now());
            """,
            ("movie_file_id", Guid.NewGuid()), ("movie_id", movieId), ("movie_path", movieFolder),
            ("video_file_id", Guid.NewGuid()), ("child_id", childId), ("video_path", videoPath),
            ("role", EntityFileRole.Source.ToCode()), ("source", FileSourceKind.Scan.ToCode()));
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters) {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(
        NpgsqlConnection connection,
        string sql,
        object? id = null,
        params (string Name, object Value)[] additional) {
        await using var command = new NpgsqlCommand(sql, connection);
        if (id is not null) command.Parameters.AddWithValue("id", id);
        foreach (var parameter in additional) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<bool> ExistsAsync(
        NpgsqlConnection connection,
        string sql,
        object? id = null,
        params (string Name, object Value)[] additional) {
        await using var command = new NpgsqlCommand(sql, connection);
        if (id is not null) command.Parameters.AddWithValue("id", id);
        foreach (var parameter in additional) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private sealed class PostgresTestDatabase(
        string databaseName,
        string adminConnectionString,
        string connectionString) : IAsyncDisposable {
        public PrismediaDbContext CreateContext() => new(
            new DbContextOptionsBuilder<PrismediaDbContext>().UseNpgsql(connectionString).Options);

        public async Task<NpgsqlConnection> OpenConnectionAsync() {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            return connection;
        }

        public async Task MigrateAsync(string targetMigration) {
            await using var context = CreateContext();
            await context.GetService<IMigrator>().MigrateAsync(targetMigration);
        }

        public async Task RunMigrationRunnerAsync(AssetPathService assets) {
            var services = new ServiceCollection();
            services.AddDbContext<PrismediaDbContext>(options => options.UseNpgsql(connectionString));
            services.AddSingleton(assets);
            await using var provider = services.BuildServiceProvider();
            await PrismediaMigrationRunner.ApplyPrismediaMigrationsAsync(
                provider,
                new ConfigurationBuilder().Build());
        }

        public async Task PrepareChangeRootAndMigrateAsync(
            AssetPathService assets,
            Guid rootId,
            string changedPath) {
            await using var context = CreateContext();
            await context.Database.OpenConnectionAsync();
            await DirectPlayableMigrationAssetPreparer.PrepareAsync(
                context,
                assets,
                CancellationToken.None);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE library_roots SET path = {changedPath}, updated_at = now() WHERE id = {rootId}");
            await context.GetService<IMigrator>().MigrateAsync(MigrationUnderTest);
        }

        public static async Task<PostgresTestDatabase> CreateAsync(string targetMigration) {
            var configured = Environment.GetEnvironmentVariable("PRISMEDIA_TEST_DATABASE_URL")
                ?? "Host=localhost;Port=5432;Database=postgres;Username=prismedia;Password=prismedia";
            var adminBuilder = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres", Pooling = false };
            try {
                await using var probe = new NpgsqlConnection(adminBuilder.ConnectionString);
                await probe.OpenAsync();
            } catch (Exception exception) when (exception is NpgsqlException or TimeoutException) {
                throw SkipException.ForSkip($"PostgreSQL migration test requires PRISMEDIA_TEST_DATABASE_URL or local PostgreSQL: {exception.Message}");
            }

            var name = $"prismedia_direct_playable_{Guid.NewGuid():N}";
            await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString)) {
                await admin.OpenAsync();
                await new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin).ExecuteNonQueryAsync();
            }
            var testBuilder = new NpgsqlConnectionStringBuilder(adminBuilder.ConnectionString) { Database = name, Pooling = false };
            var database = new PostgresTestDatabase(name, adminBuilder.ConnectionString, testBuilder.ConnectionString);
            try {
                await database.MigrateAsync(targetMigration);
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
            await new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin).ExecuteNonQueryAsync();
        }
    }

    private sealed class MigrationFiles : IDisposable {
        private MigrationFiles(string root) {
            Root = root;
            CacheRoot = Path.Combine(root, "cache");
            Directory.CreateDirectory(CacheRoot);
            Assets = new AssetPathService(root, CacheRoot);
        }

        public string Root { get; }
        public string CacheRoot { get; }
        public AssetPathService Assets { get; }
        public static MigrationFiles Create() => new(Path.Combine(Path.GetTempPath(), $"prismedia-direct-playable-{Guid.NewGuid():N}"));
        public string CreateFolder(string relative) { var path = Path.Combine(Root, relative); Directory.CreateDirectory(path); return path; }
        public string CreateFile(string relative, string contents) { var path = Path.Combine(Root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, contents); return path; }
        public string AssetDiskPath(string url) => Assets.ResolveAssetDiskPath(url)!;
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
    }

    private sealed record MigrationFixture(
        Guid MovieId, Guid LegacyMovieVideoId, Guid SeriesId, Guid SeasonId,
        Guid FirstEpisodeId, Guid SecondEpisodeId, Guid StandaloneVideoId,
        Guid PersonId, Guid CollectionId, Guid FolderBookId, Guid FileBookId,
        Guid UserId, Guid LibraryRootId, Guid JobGraphId, Guid JobRunId,
        Guid MixedJobRunId, Guid AcquisitionId, Guid MonitorId,
        string MovieFolder, string MovieSourcePath, string SeriesFolder, string SeasonFolder,
        string SharedEpisodePath, string StandalonePath, string BookFolder, string FileBookPath,
        string LegacySubtitlePath, string LegacySubtitleSourcePath, string MigratedSubtitlePath,
        string MigratedSubtitleSourcePath, string LegacyArtworkPath, string MigratedArtworkPath,
        string ArtworkUrl) {
        public static MigrationFixture Create(MigrationFiles files) {
            var movieId = Guid.NewGuid();
            var childId = Guid.NewGuid();
            var subtitleName = "fixture.vtt";
            var subtitleSourceName = "fixture.ass";
            var legacySubtitle = files.CreateFile($"cache/videos/{childId}/subtitles/{subtitleName}", "normalized subtitle");
            var legacySubtitleSource = files.CreateFile($"cache/videos/{childId}/subtitles/{subtitleSourceName}", "styled subtitle");
            var artworkUrl = $"/assets/custom/artwork/{childId}/poster.jpg";
            var legacyArtwork = files.AssetDiskPath(artworkUrl);
            Directory.CreateDirectory(Path.GetDirectoryName(legacyArtwork)!);
            File.WriteAllText(legacyArtwork, "custom poster");
            return new(
                movieId, childId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                files.CreateFolder("media/movies/direct-movie"),
                files.CreateFile("media/movies/direct-movie/direct-movie.mkv", "movie"),
                files.CreateFolder("media/series/show"),
                files.CreateFolder("media/series/show/season-1"),
                files.CreateFile("media/series/show/season-1/shared.mkv", "episodes"),
                files.CreateFile("media/standalone.mkv", "standalone"),
                files.CreateFolder("media/books/folder-book"),
                files.CreateFile("media/books/file-book.epub", "book"),
                legacySubtitle,
                legacySubtitleSource,
                Path.Combine(files.CacheRoot, "videos", movieId.ToString(), "subtitles", subtitleName),
                Path.Combine(files.CacheRoot, "videos", movieId.ToString(), "subtitles", subtitleSourceName),
                legacyArtwork,
                files.AssetDiskPath($"/assets/custom/artwork/{movieId}/poster.jpg"),
                artworkUrl);
        }

        public (string Name, object Value)[] Parameters() => [
            ("user_id", UserId), ("root_id", LibraryRootId), ("root_path", Path.GetDirectoryName(MovieFolder)!),
            ("user_role", UserRole.Admin.ToCode()),
            ("movie_id", MovieId), ("movie_video_id", LegacyMovieVideoId), ("series_id", SeriesId),
            ("season_id", SeasonId), ("episode_one_id", FirstEpisodeId), ("episode_two_id", SecondEpisodeId),
            ("standalone_id", StandaloneVideoId), ("person_id", PersonId), ("collection_id", CollectionId),
            ("folder_book_id", FolderBookId), ("file_book_id", FileBookId),
            ("movie_kind", EntityKind.Movie.ToCode()), ("video_kind", EntityKind.Video.ToCode()),
            ("series_kind", EntityKind.VideoSeries.ToCode()), ("season_kind", EntityKind.VideoSeason.ToCode()),
            ("person_kind", EntityKind.Person.ToCode()), ("collection_kind", EntityKind.Collection.ToCode()),
            ("book_kind", EntityKind.Book.ToCode()),
            ("movie_folder_file_id", Guid.NewGuid()), ("movie_source_file_id", Guid.NewGuid()),
            ("movie_thumbnail_file_id", Guid.NewGuid()), ("artwork_file_id", Guid.NewGuid()),
            ("series_folder_file_id", Guid.NewGuid()), ("season_folder_file_id", Guid.NewGuid()),
            ("episode_one_file_id", Guid.NewGuid()), ("episode_two_file_id", Guid.NewGuid()),
            ("standalone_file_id", Guid.NewGuid()), ("folder_book_file_id", Guid.NewGuid()),
            ("file_book_file_id", Guid.NewGuid()), ("source_role", EntityFileRole.Source.ToCode()),
            ("thumbnail_role", EntityFileRole.Thumbnail.ToCode()), ("poster_role", EntityFileRole.Poster.ToCode()),
            ("scan_source", FileSourceKind.Scan.ToCode()), ("custom_source", FileSourceKind.Custom.ToCode()),
            ("movie_folder", MovieFolder), ("movie_source_path", MovieSourcePath), ("series_folder", SeriesFolder),
            ("season_folder", SeasonFolder), ("shared_path", SharedEpisodePath), ("standalone_path", StandalonePath),
            ("book_folder", BookFolder), ("file_book_path", FileBookPath), ("artwork_url", ArtworkUrl),
            ("marker_id", Guid.NewGuid()), ("subtitle_id", Guid.NewGuid()),
            ("subtitle_format", SubtitleFormats.Vtt),
            ("subtitle_source", EntitySubtitleSource.Sidecar.ToCode()),
            ("subtitle_source_format", SubtitleFormats.Ass),
            ("subtitle_path", LegacySubtitlePath),
            ("subtitle_source_path", LegacySubtitleSourcePath), ("media_source_id", Guid.NewGuid()),
            ("media_stream_id", Guid.NewGuid()), ("external_id_id", Guid.NewGuid()), ("collection_item_id", Guid.NewGuid()),
            ("progress_unit", ProgressUnit.Second.ToCode()),
            ("cast_relationship", RelationshipKind.Cast.ToCode()),
            ("related_relationship", RelationshipKind.Related.ToCode()),
            ("credit_metadata", $"{{\"role\":\"{LegacyPerformerRoleCode}\",\"roles\":[\"{LegacyPerformerRoleCode}\",\"{CreditRole.Actor.ToCode()}\",\"{CreditRole.Director.ToCode()}\",\"{LegacyPerformerRoleCode}\"],\"note\":\"performer biography\"}}"),
            ("unrelated_metadata", $"{{\"role\":\"{LegacyPerformerRoleCode}\",\"note\":\"not a credit\"}}"),
            ("collection_mode", CollectionMode.Dynamic.ToCode()),
            ("collection_cover_mode", CollectionCoverMode.Item.ToCode()),
            ("collection_item_source", CollectionItemSource.Dynamic.ToCode()),
            ("collection_rule", $"{{\"condition\":{{\"entityTypes\":[\"{EntityKind.Video.ToCode()}\",\"{EntityKind.Book.ToCode()}\",\"{EntityKind.Video.ToCode()}\"]}}}}"),
            ("default_providers_key", AppSettings.Identify.DefaultProviders.Key),
            ("default_providers", $"{{\"{EntityKind.Video.ToCode()}\":\"fixture-provider\"}}"),
            ("job_graph_id", JobGraphId), ("job_run_id", JobRunId), ("movie_video_id_text", LegacyMovieVideoId.ToString()),
            ("mixed_job_run_id", MixedJobRunId),
            ("job_origin", JobGraphOrigin.Background.ToCode()),
            ("graph_status", JobGraphStatus.Queued.ToCode()),
            ("job_type", JobType.ScanLibrary.ToCode()),
            ("run_status", JobRunStatus.Queued.ToCode()),
            ("job_active_key", $"{JobType.ScanLibrary.ToCode()}:{LegacyMovieVideoId}"),
            ("job_resource_key", JobResourceKeys.Entity(LegacyMovieVideoId.ToString())),
            ("job_payload", $"{{\"EntityId\":\"{LegacyMovieVideoId}\",\"EntityKind\":\"{EntityKind.Video.ToCode()}\",\"Nested\":{{\"EntityKind\":\"{EntityKind.Video.ToCode()}\",\"Data\":{{\"VideoId\":\"{LegacyMovieVideoId}\"}}}},\"Caption\":\"legacy text {LegacyMovieVideoId}\"}}"),
            ("mixed_job_payload", $"{{\"Items\":[{{\"EntityId\":\"{LegacyMovieVideoId}\",\"EntityKind\":\"{EntityKind.Video.ToCode()}\"}},{{\"EntityId\":\"{FirstEpisodeId}\",\"EntityKind\":\"{EntityKind.Video.ToCode()}\"}}]}}"),
            ("acquisition_id", AcquisitionId), ("history_id", Guid.NewGuid()), ("hint_id", Guid.NewGuid()),
            ("monitor_id", MonitorId),
            ("acquisition_status", AcquisitionStatus.Pending.ToCode()),
            ("history_event", AcquisitionHistoryEvent.Grabbed.ToCode()),
            ("monitor_status", MonitorStatus.Active.ToCode())
        ];
    }

    private sealed record MigrationAuditFixture(
        Guid EpisodeAcquisitionId,
        Guid DoneQueueItemId,
        Guid DeletedQueueItemId,
        Guid IdentifyResultId);
}
