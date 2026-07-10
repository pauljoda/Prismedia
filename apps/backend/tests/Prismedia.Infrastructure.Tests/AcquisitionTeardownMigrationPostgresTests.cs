using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Xunit.Sdk;

namespace Prismedia.Infrastructure.Tests;

/// <summary>PostgreSQL data-migration coverage for generic legacy acquisition and monitor identity repair.</summary>
public sealed class AcquisitionTeardownMigrationPostgresTests {
    private const string PreviousMigration = "20260710031735_AddEntityProviderIdentity";
    private const string MigrationUnderTest = "20260710063451_AddAcquisitionTeardownClaims";
    private const string CorrectiveMigration = "20260710081715_PromoteGenericMonitorEntityIds";
    private const string IdentityCorrectiveMigration = "20260710082937_BackfillLegacyAcquisitionIdentityLinks";
    private const string EntityLifecycleMigration = "20260710092229_AddEntityLifecycleClaimsAndNormalizeMonitors";

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task CanonicalizationPreservesDuplicateIntentTransfersAndDowngradeAliases() {
        await using var database = await PostgresTestDatabase.CreateAsync(PreviousMigration);
        var fixture = MigrationFixture.Create();
        await SeedLegacyRowsAsync(database, fixture);

        await database.MigrateAsync(MigrationUnderTest);

        await using (var connection = await database.OpenConnectionAsync()) {
            var monitors = await ReadMonitorsAsync(connection);
            Assert.Equal(4, monitors.Count);

            Assert.Equal(
                new MonitorSnapshot(
                    fixture.CanonicalEntityId,
                    fixture.CanonicalAcquisitionId,
                    MonitorStatus.Paused.ToCode(),
                    fixture.CanonicalTargetRootId,
                    fixture.CanonicalProfileId,
                    1,
                    2,
                    fixture.CanonicalUpgradeId),
                monitors[fixture.CanonicalMonitorId]);
            Assert.Equal(
                new MonitorSnapshot(
                    null,
                    fixture.LegacyAcquisitionId,
                    MonitorStatus.Active.ToCode(),
                    fixture.LegacyTargetRootId,
                    fixture.LegacyProfileId,
                    7,
                    8,
                    fixture.LegacyUpgradeId),
                monitors[fixture.LegacyMonitorId]);
            Assert.Equal(
                new MonitorSnapshot(
                    null,
                    null,
                    MonitorStatus.Fulfilled.ToCode(),
                    fixture.UpgradeOnlyTargetRootId,
                    fixture.UpgradeOnlyProfileId,
                    9,
                    10,
                    fixture.UpgradeOnlyAcquisitionId),
                monitors[fixture.UpgradeOnlyMonitorId]);
            Assert.Equal(fixture.NonConflictingEntityId, monitors[fixture.NonConflictingMonitorId].EntityId);

            var acquisitionEntities = await ReadAcquisitionEntityIdsAsync(connection);
            Assert.Equal(fixture.CanonicalEntityId, acquisitionEntities[fixture.CanonicalAcquisitionId]);
            Assert.Equal(fixture.CanonicalEntityId, acquisitionEntities[fixture.LegacyAcquisitionId]);
            Assert.Equal(fixture.CanonicalEntityId, acquisitionEntities[fixture.LegacyUpgradeId]);
            Assert.Equal(fixture.CanonicalEntityId, acquisitionEntities[fixture.UpgradeOnlyAcquisitionId]);
            Assert.Equal(fixture.NonConflictingEntityId, acquisitionEntities[fixture.NonConflictingAcquisitionId]);

            Assert.Equal(
                fixture.TransferIds.Order(),
                (await ReadTransferIdsAsync(connection)).Order());
            Assert.False(await ColumnExistsAsync(connection, "monitors", "book_entity_id"));
        }

        await database.MigrateAsync(PreviousMigration);

        await using (var connection = await database.OpenConnectionAsync()) {
            var aliases = await ReadLegacyAliasesAsync(connection);
            Assert.Equal(fixture.CanonicalEntityId, aliases[fixture.CanonicalMonitorId]);
            Assert.Equal(fixture.CanonicalEntityId, aliases[fixture.LegacyMonitorId]);
            Assert.Equal(fixture.CanonicalEntityId, aliases[fixture.UpgradeOnlyMonitorId]);
            Assert.Equal(fixture.NonConflictingEntityId, aliases[fixture.NonConflictingMonitorId]);
            Assert.Equal(
                fixture.TransferIds.Order(),
                (await ReadTransferIdsAsync(connection)).Order());
        }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task FreshMigrationPromotesNonBookPrimaryAndUpgradeAcquisitionTargets() {
        await using var database = await PostgresTestDatabase.CreateAsync(PreviousMigration);
        var movieEntityId = Guid.NewGuid();
        var albumEntityId = Guid.NewGuid();
        var movieAcquisitionId = Guid.NewGuid();
        var albumUpgradeId = Guid.NewGuid();
        var movieMonitorId = Guid.NewGuid();
        var albumMonitorId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using (var connection = await database.OpenConnectionAsync()) {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO acquisitions
                    (id, entity_id, kind, status, title, external_ids_json, source_urls_json, created_at, updated_at)
                VALUES
                    (@movie_acquisition, @movie_entity, @movie_kind, @downloading, 'Movie', '{}', '[]', @now, @now),
                    (@album_upgrade, @album_entity, @album_kind, @downloading, 'Album', '{}', '[]', @now, @now);

                INSERT INTO monitors
                    (id, kind, acquisition_id, entity_id, book_entity_id, status, title,
                     upgrade_child_acquisition_id, created_at, updated_at)
                VALUES
                    (@movie_monitor, @movie_kind, @movie_acquisition, NULL, NULL, @active, 'Movie monitor', NULL, @now, @now),
                    (@album_monitor, @album_kind, NULL, NULL, NULL, @active, 'Album upgrade monitor', @album_upgrade, @now, @now);
                """,
                connection);
            command.Parameters.AddWithValue("movie_acquisition", movieAcquisitionId);
            command.Parameters.AddWithValue("album_upgrade", albumUpgradeId);
            command.Parameters.AddWithValue("movie_entity", movieEntityId);
            command.Parameters.AddWithValue("album_entity", albumEntityId);
            command.Parameters.AddWithValue("movie_kind", EntityKind.Movie.ToCode());
            command.Parameters.AddWithValue("album_kind", EntityKind.AudioLibrary.ToCode());
            command.Parameters.AddWithValue("downloading", AcquisitionStatus.Downloading.ToCode());
            command.Parameters.AddWithValue("active", MonitorStatus.Active.ToCode());
            command.Parameters.AddWithValue("movie_monitor", movieMonitorId);
            command.Parameters.AddWithValue("album_monitor", albumMonitorId);
            command.Parameters.AddWithValue("now", now);
            await command.ExecuteNonQueryAsync();
        }

        await database.MigrateAsync(MigrationUnderTest);

        await using var verification = await database.OpenConnectionAsync();
        var monitors = await ReadMonitorsAsync(verification);
        Assert.Equal(movieEntityId, monitors[movieMonitorId].EntityId);
        Assert.Equal(albumEntityId, monitors[albumMonitorId].EntityId);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task CorrectiveMigrationPromotesGenericPrimaryAndUpgradeTargetsWithoutCollapsingDuplicates() {
        await using var database = await PostgresTestDatabase.CreateAsync(MigrationUnderTest);
        var movieEntityId = Guid.NewGuid();
        var seasonEntityId = Guid.NewGuid();
        var albumEntityId = Guid.NewGuid();
        var movieAcquisitionId = Guid.NewGuid();
        var seasonAcquisitionId = Guid.NewGuid();
        var seasonUpgradeId = Guid.NewGuid();
        var albumUpgradeId = Guid.NewGuid();
        var movieMonitorId = Guid.NewGuid();
        var canonicalSeasonMonitorId = Guid.NewGuid();
        var duplicateSeasonMonitorId = Guid.NewGuid();
        var albumUpgradeMonitorId = Guid.NewGuid();
        var transferIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var canonicalRootId = Guid.NewGuid();
        var duplicateRootId = Guid.NewGuid();
        var duplicateProfileId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.AddHours(-1);

        await using (var connection = await database.OpenConnectionAsync()) {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO acquisitions
                    (id, entity_id, kind, status, title, external_ids_json, source_urls_json, created_at, updated_at)
                VALUES
                    (@movie_acquisition, @movie_entity, @movie_kind, @downloading, 'Movie transfer', '{}', '[]', @now, @now),
                    (@season_acquisition, @season_entity, @season_kind, @downloading, 'Season transfer', '{}', '[]', @now, @now),
                    (@season_upgrade, @season_entity, @episode_kind, @downloading, 'Season upgrade', '{}', '[]', @now, @now),
                    (@album_upgrade, @album_entity, @album_kind, @downloading, 'Album upgrade', '{}', '[]', @now, @now);

                INSERT INTO monitors
                    (id, kind, acquisition_id, entity_id, status, title,
                     target_library_root_id, profile_id, upgrade_attempts, barren_searches,
                     upgrade_child_acquisition_id, created_at, updated_at)
                VALUES
                    (@movie_monitor, @movie_kind, @movie_acquisition, NULL, @active, 'Movie monitor',
                     NULL, NULL, 1, 2, NULL, @now, @now),
                    (@canonical_season_monitor, @season_kind, NULL, @season_entity, @paused, 'Canonical season',
                     @canonical_root, NULL, 3, 4, NULL, @now, @now),
                    (@duplicate_season_monitor, @season_kind, @season_acquisition, NULL, @active, 'Legacy season transfer',
                     @duplicate_root, @duplicate_profile, 7, 8, @season_upgrade, @now, @now),
                    (@album_upgrade_monitor, @album_kind, NULL, NULL, @fulfilled, 'Album upgrade-only monitor',
                     NULL, NULL, 9, 10, @album_upgrade, @now, @now);

                INSERT INTO download_transfers
                    (id, acquisition_id, client_item_id, progress, state, created_at, updated_at)
                VALUES
                    (@movie_transfer, @movie_acquisition, 'movie-transfer', 0.25, 'downloading', @now, @now),
                    (@season_transfer, @season_acquisition, 'season-transfer', 0.50, 'downloading', @now, @now),
                    (@season_upgrade_transfer, @season_upgrade, 'season-upgrade-transfer', 0.75, 'downloading', @now, @now),
                    (@album_upgrade_transfer, @album_upgrade, 'album-upgrade-transfer', 0.90, 'seeding', @now, @now);
                """,
                connection);
            command.Parameters.AddWithValue("movie_acquisition", movieAcquisitionId);
            command.Parameters.AddWithValue("season_acquisition", seasonAcquisitionId);
            command.Parameters.AddWithValue("season_upgrade", seasonUpgradeId);
            command.Parameters.AddWithValue("album_upgrade", albumUpgradeId);
            command.Parameters.AddWithValue("movie_entity", movieEntityId);
            command.Parameters.AddWithValue("season_entity", seasonEntityId);
            command.Parameters.AddWithValue("album_entity", albumEntityId);
            command.Parameters.AddWithValue("movie_kind", EntityKind.Movie.ToCode());
            command.Parameters.AddWithValue("season_kind", EntityKind.VideoSeason.ToCode());
            command.Parameters.AddWithValue("episode_kind", EntityKind.Video.ToCode());
            command.Parameters.AddWithValue("album_kind", EntityKind.AudioLibrary.ToCode());
            command.Parameters.AddWithValue("downloading", AcquisitionStatus.Downloading.ToCode());
            command.Parameters.AddWithValue("active", MonitorStatus.Active.ToCode());
            command.Parameters.AddWithValue("paused", MonitorStatus.Paused.ToCode());
            command.Parameters.AddWithValue("fulfilled", MonitorStatus.Fulfilled.ToCode());
            command.Parameters.AddWithValue("movie_monitor", movieMonitorId);
            command.Parameters.AddWithValue("canonical_season_monitor", canonicalSeasonMonitorId);
            command.Parameters.AddWithValue("duplicate_season_monitor", duplicateSeasonMonitorId);
            command.Parameters.AddWithValue("album_upgrade_monitor", albumUpgradeMonitorId);
            command.Parameters.AddWithValue("canonical_root", canonicalRootId);
            command.Parameters.AddWithValue("duplicate_root", duplicateRootId);
            command.Parameters.AddWithValue("duplicate_profile", duplicateProfileId);
            command.Parameters.AddWithValue("movie_transfer", transferIds[0]);
            command.Parameters.AddWithValue("season_transfer", transferIds[1]);
            command.Parameters.AddWithValue("season_upgrade_transfer", transferIds[2]);
            command.Parameters.AddWithValue("album_upgrade_transfer", transferIds[3]);
            command.Parameters.AddWithValue("now", now);
            await command.ExecuteNonQueryAsync();
        }

        await database.MigrateAsync(CorrectiveMigration);

        await using (var connection = await database.OpenConnectionAsync()) {
            var monitors = await ReadMonitorsAsync(connection);
            Assert.Equal(4, monitors.Count);
            Assert.Equal(movieEntityId, monitors[movieMonitorId].EntityId);
            Assert.Equal(albumEntityId, monitors[albumUpgradeMonitorId].EntityId);
            Assert.Equal(seasonEntityId, monitors[canonicalSeasonMonitorId].EntityId);
            Assert.Null(monitors[duplicateSeasonMonitorId].EntityId);
            Assert.Equal(seasonAcquisitionId, monitors[duplicateSeasonMonitorId].AcquisitionId);
            Assert.Equal(MonitorStatus.Active.ToCode(), monitors[duplicateSeasonMonitorId].Status);
            Assert.Equal(duplicateRootId, monitors[duplicateSeasonMonitorId].TargetLibraryRootId);
            Assert.Equal(duplicateProfileId, monitors[duplicateSeasonMonitorId].ProfileId);
            Assert.Equal(7, monitors[duplicateSeasonMonitorId].UpgradeAttempts);
            Assert.Equal(8, monitors[duplicateSeasonMonitorId].BarrenSearches);
            Assert.Equal(seasonUpgradeId, monitors[duplicateSeasonMonitorId].UpgradeChildAcquisitionId);
            Assert.Equal(transferIds.Order(), (await ReadTransferIdsAsync(connection)).Order());
        }

        // Downgrade intentionally preserves corrected data because it cannot distinguish these promoted
        // links from ordinary application-written stable monitor identities.
        await database.MigrateAsync(MigrationUnderTest);
        await using (var connection = await database.OpenConnectionAsync()) {
            var monitors = await ReadMonitorsAsync(connection);
            Assert.Equal(movieEntityId, monitors[movieMonitorId].EntityId);
            Assert.Equal(albumEntityId, monitors[albumUpgradeMonitorId].EntityId);
            Assert.Equal(seasonEntityId, monitors[canonicalSeasonMonitorId].EntityId);
            Assert.Null(monitors[duplicateSeasonMonitorId].EntityId);
            Assert.Equal(transferIds.Order(), (await ReadTransferIdsAsync(connection)).Order());
        }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task IdentityCorrectionRepairsEveryUniqueCompatibleAcquisitionAndFailsClosedOtherwise() {
        await using var database = await PostgresTestDatabase.CreateAsync(CorrectiveMigration);
        var bookEntityId = Guid.NewGuid();
        var firstAmbiguousMovieEntityId = Guid.NewGuid();
        var secondAmbiguousMovieEntityId = Guid.NewGuid();
        var exactAcquisitionIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var ambiguousAcquisitionId = Guid.NewGuid();
        var unmatchedAcquisitionId = Guid.NewGuid();
        var incompatibleAcquisitionId = Guid.NewGuid();
        var exactMonitorId = Guid.NewGuid();
        var exactTransferId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.AddHours(-1);

        await using (var connection = await database.OpenConnectionAsync()) {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO entities (id, kind_code, title, created_at, updated_at)
                VALUES
                    (@book_entity, @book_kind, 'Project Hail Mary', @now, @now),
                    (@first_movie_entity, @movie_kind, 'Ambiguous movie one', @now, @now),
                    (@second_movie_entity, @movie_kind, 'Ambiguous movie two', @now, @now);

                INSERT INTO entity_external_ids
                    (id, entity_id, provider, value, created_at, updated_at)
                VALUES
                    (@book_external_id, @book_entity, 'openlibrary', 'OL37575384W', @now, @now),
                    (@first_movie_external_id, @first_movie_entity, 'tmdb', 'ambiguous-movie', @now, @now),
                    (@second_movie_external_id, @second_movie_entity, 'tmdb', 'ambiguous-movie', @now, @now);

                INSERT INTO acquisitions
                    (id, entity_id, kind, status, title, identity_namespace, identity_value,
                     external_ids_json, source_urls_json, created_at, updated_at)
                VALUES
                    (@imported_acquisition, NULL, @book_kind, @imported, 'Imported exact match',
                     'openlibrary', 'OL37575384W', '{}', '[]', @now, @now),
                    (@cancelled_acquisition_one, NULL, @book_kind, @cancelled, 'Cancelled exact match one',
                     'openlibrary', 'OL37575384W', '{}', '[]', @now, @now),
                    (@cancelled_acquisition_two, NULL, @book_kind, @cancelled, 'Cancelled exact match two',
                     'openlibrary', 'OL37575384W', '{}', '[]', @now, @now),
                    (@cancelled_acquisition_three, NULL, @book_kind, @cancelled, 'Cancelled exact match three',
                     'openlibrary', 'OL37575384W', '{}', '[]', @now, @now),
                    (@ambiguous_acquisition, NULL, @movie_kind, @pending, 'Ambiguous identity',
                     'tmdb', 'ambiguous-movie', '{}', '[]', @now, @now),
                    (@unmatched_acquisition, NULL, @album_kind, @pending, 'Unmatched identity',
                     'musicbrainz', 'missing-album', '{}', '[]', @now, @now),
                    (@incompatible_acquisition, NULL, @movie_kind, @pending, 'Incompatible identity kind',
                     'openlibrary', 'OL37575384W', '{}', '[]', @now, @now);

                INSERT INTO monitors
                    (id, kind, acquisition_id, entity_id, status, title, created_at, updated_at)
                VALUES
                    (@exact_monitor, @book_kind, @imported_acquisition, NULL, @active,
                     'Legacy exact-match monitor', @now, @now);

                INSERT INTO download_transfers
                    (id, acquisition_id, client_item_id, progress, state, created_at, updated_at)
                VALUES
                    (@exact_transfer, @imported_acquisition, 'exact-transfer', 0.75, 'seeding', @now, @now);
                """,
                connection);
            command.Parameters.AddWithValue("book_entity", bookEntityId);
            command.Parameters.AddWithValue("first_movie_entity", firstAmbiguousMovieEntityId);
            command.Parameters.AddWithValue("second_movie_entity", secondAmbiguousMovieEntityId);
            command.Parameters.AddWithValue("book_external_id", Guid.NewGuid());
            command.Parameters.AddWithValue("first_movie_external_id", Guid.NewGuid());
            command.Parameters.AddWithValue("second_movie_external_id", Guid.NewGuid());
            command.Parameters.AddWithValue("imported_acquisition", exactAcquisitionIds[0]);
            command.Parameters.AddWithValue("cancelled_acquisition_one", exactAcquisitionIds[1]);
            command.Parameters.AddWithValue("cancelled_acquisition_two", exactAcquisitionIds[2]);
            command.Parameters.AddWithValue("cancelled_acquisition_three", exactAcquisitionIds[3]);
            command.Parameters.AddWithValue("ambiguous_acquisition", ambiguousAcquisitionId);
            command.Parameters.AddWithValue("unmatched_acquisition", unmatchedAcquisitionId);
            command.Parameters.AddWithValue("incompatible_acquisition", incompatibleAcquisitionId);
            command.Parameters.AddWithValue("exact_monitor", exactMonitorId);
            command.Parameters.AddWithValue("exact_transfer", exactTransferId);
            command.Parameters.AddWithValue("book_kind", EntityKind.Book.ToCode());
            command.Parameters.AddWithValue("movie_kind", EntityKind.Movie.ToCode());
            command.Parameters.AddWithValue("album_kind", EntityKind.AudioLibrary.ToCode());
            command.Parameters.AddWithValue("imported", AcquisitionStatus.Imported.ToCode());
            command.Parameters.AddWithValue("cancelled", AcquisitionStatus.Cancelled.ToCode());
            command.Parameters.AddWithValue("pending", AcquisitionStatus.Pending.ToCode());
            command.Parameters.AddWithValue("active", MonitorStatus.Active.ToCode());
            command.Parameters.AddWithValue("now", now);
            await command.ExecuteNonQueryAsync();
        }

        await database.MigrateAsync(IdentityCorrectiveMigration);

        await using (var connection = await database.OpenConnectionAsync()) {
            var acquisitionEntities = await ReadAcquisitionEntityIdsAsync(connection);
            Assert.All(exactAcquisitionIds, id => Assert.Equal(bookEntityId, acquisitionEntities[id]));
            Assert.Null(acquisitionEntities[ambiguousAcquisitionId]);
            Assert.Null(acquisitionEntities[unmatchedAcquisitionId]);
            Assert.Null(acquisitionEntities[incompatibleAcquisitionId]);

            var monitors = await ReadMonitorsAsync(connection);
            Assert.Equal(bookEntityId, monitors[exactMonitorId].EntityId);
            Assert.Equal([exactTransferId], await ReadTransferIdsAsync(connection));
        }

        // Downgrade is intentionally data-preserving: repaired links have become stable identity data.
        await database.MigrateAsync(CorrectiveMigration);
        await using (var connection = await database.OpenConnectionAsync()) {
            var acquisitionEntities = await ReadAcquisitionEntityIdsAsync(connection);
            Assert.All(exactAcquisitionIds, id => Assert.Equal(bookEntityId, acquisitionEntities[id]));
            Assert.Null(acquisitionEntities[ambiguousAcquisitionId]);
            Assert.Null(acquisitionEntities[unmatchedAcquisitionId]);
            Assert.Null(acquisitionEntities[incompatibleAcquisitionId]);
            Assert.Equal(bookEntityId, (await ReadMonitorsAsync(connection))[exactMonitorId].EntityId);
            Assert.Equal([exactTransferId], await ReadTransferIdsAsync(connection));
        }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task EntityLifecycleMigrationReactivatesOnlyStableEntityMonitorsAndEnforcesCompleteClaims() {
        await using var database = await PostgresTestDatabase.CreateAsync(IdentityCorrectiveMigration);
        var fulfilledEntityId = Guid.NewGuid();
        var pausedEntityId = Guid.NewGuid();
        var fulfilledMonitorId = Guid.NewGuid();
        var pausedMonitorId = Guid.NewGuid();
        var legacyFulfilledMonitorId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.AddHours(-1);
        await using (var connection = await database.OpenConnectionAsync()) {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO entities (id, kind_code, title, created_at, updated_at)
                VALUES
                    (@fulfilled_entity, @book_kind, 'Stable fulfilled book', @now, @now),
                    (@paused_entity, @book_kind, 'Stable paused book', @now, @now);

                INSERT INTO monitors
                    (id, kind, entity_id, status, title, monitor_preset, created_at, updated_at)
                VALUES
                    (@fulfilled_monitor, @book_kind, @fulfilled_entity, @fulfilled,
                     'Stable fulfilled monitor', 'first-season', @now, @now),
                    (@paused_monitor, @book_kind, @paused_entity, @paused,
                     'Stable paused monitor', 'missing', @now, @now),
                    (@legacy_fulfilled_monitor, @book_kind, NULL, @fulfilled,
                     'Legacy acquisition-only monitor', 'pilot', @now, @now);
                """,
                connection);
            command.Parameters.AddWithValue("fulfilled_entity", fulfilledEntityId);
            command.Parameters.AddWithValue("paused_entity", pausedEntityId);
            command.Parameters.AddWithValue("fulfilled_monitor", fulfilledMonitorId);
            command.Parameters.AddWithValue("paused_monitor", pausedMonitorId);
            command.Parameters.AddWithValue("legacy_fulfilled_monitor", legacyFulfilledMonitorId);
            command.Parameters.AddWithValue("book_kind", EntityKind.Book.ToCode());
            command.Parameters.AddWithValue("fulfilled", MonitorStatus.Fulfilled.ToCode());
            command.Parameters.AddWithValue("paused", MonitorStatus.Paused.ToCode());
            command.Parameters.AddWithValue("now", now);
            await command.ExecuteNonQueryAsync();
        }

        await database.MigrateAsync(EntityLifecycleMigration);

        await using (var connection = await database.OpenConnectionAsync()) {
            var monitors = await ReadMonitorsAsync(connection);
            Assert.Equal(MonitorStatus.Active.ToCode(), monitors[fulfilledMonitorId].Status);
            Assert.Equal(MonitorStatus.Paused.ToCode(), monitors[pausedMonitorId].Status);
            Assert.Equal(MonitorStatus.Fulfilled.ToCode(), monitors[legacyFulfilledMonitorId].Status);
            var presets = await ReadMonitorPresetsAsync(connection);
            Assert.Equal(MonitorPreset.None.ToCode(), presets[fulfilledMonitorId]);
            Assert.Equal(MonitorPreset.Missing.ToCode(), presets[pausedMonitorId]);
            Assert.Equal(MonitorPreset.None.ToCode(), presets[legacyFulfilledMonitorId]);
            Assert.True(await ColumnExistsAsync(connection, "entities", "lifecycle_claim_kind"));
            Assert.True(await ColumnExistsAsync(connection, "entities", "lifecycle_claim_id"));
            Assert.True(await ColumnExistsAsync(connection, "entities", "lifecycle_claimed_at"));

            await using var incomplete = new NpgsqlCommand(
                "UPDATE entities SET lifecycle_claim_kind = @kind WHERE id = @entity_id",
                connection);
            incomplete.Parameters.AddWithValue("kind", EntityLifecycleClaimKind.DeletingFiles.ToCode());
            incomplete.Parameters.AddWithValue("entity_id", fulfilledEntityId);
            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => incomplete.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);

            await using var complete = new NpgsqlCommand(
                """
                UPDATE entities
                SET lifecycle_claim_kind = @kind,
                    lifecycle_claim_id = @claim_id,
                    lifecycle_claimed_at = @claimed_at
                WHERE id = @entity_id
                """,
                connection);
            complete.Parameters.AddWithValue("kind", EntityLifecycleClaimKind.DeletingFiles.ToCode());
            complete.Parameters.AddWithValue("claim_id", Guid.NewGuid());
            complete.Parameters.AddWithValue("claimed_at", DateTimeOffset.UtcNow);
            complete.Parameters.AddWithValue("entity_id", fulfilledEntityId);
            Assert.Equal(1, await complete.ExecuteNonQueryAsync());
        }

        await database.MigrateAsync(IdentityCorrectiveMigration);
        await using (var connection = await database.OpenConnectionAsync()) {
            Assert.False(await ColumnExistsAsync(connection, "entities", "lifecycle_claim_kind"));
            Assert.Equal(
                MonitorStatus.Active.ToCode(),
                (await ReadMonitorsAsync(connection))[fulfilledMonitorId].Status);
        }
    }

    private static async Task SeedLegacyRowsAsync(
        PostgresTestDatabase database,
        MigrationFixture fixture) {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO acquisitions
                (id, entity_id, status, title, external_ids_json, source_urls_json, created_at, updated_at)
            VALUES
                (@canonical_acquisition, @canonical_entity, @downloading, 'Canonical acquisition', '{}', '[]', @now, @now),
                (@legacy_acquisition, NULL, @downloading, 'Legacy acquisition', '{}', '[]', @now, @now),
                (@canonical_upgrade, @canonical_entity, @downloading, 'Canonical upgrade', '{}', '[]', @now, @now),
                (@legacy_upgrade, NULL, @downloading, 'Legacy upgrade', '{}', '[]', @now, @now),
                (@upgrade_only_acquisition, NULL, @downloading, 'Upgrade-only transfer', '{}', '[]', @now, @now),
                (@non_conflicting_acquisition, NULL, @pending, 'Non-conflicting acquisition', '{}', '[]', @now, @now);

            INSERT INTO monitors
                (id, acquisition_id, entity_id, book_entity_id, status, title,
                 target_library_root_id, profile_id, upgrade_attempts, barren_searches,
                 upgrade_child_acquisition_id, created_at, updated_at)
            VALUES
                (@canonical_monitor, @canonical_acquisition, @canonical_entity, NULL, @paused, 'Canonical monitor',
                 @canonical_root, @canonical_profile, 1, 2, @canonical_upgrade, @canonical_created, @now),
                (@legacy_monitor, @legacy_acquisition, NULL, @canonical_entity, @active, 'Legacy active monitor',
                 @legacy_root, @legacy_profile, 7, 8, @legacy_upgrade, @legacy_created, @now),
                (@upgrade_only_monitor, NULL, NULL, @canonical_entity, @fulfilled, 'Legacy upgrade-only monitor',
                 @upgrade_only_root, @upgrade_only_profile, 9, 10, @upgrade_only_acquisition, @upgrade_only_created, @now),
                (@non_conflicting_monitor, @non_conflicting_acquisition, NULL, @non_conflicting_entity, @active, 'Non-conflicting monitor',
                 @non_conflicting_root, @non_conflicting_profile, 0, 0, NULL, @non_conflicting_created, @now);

            INSERT INTO download_transfers
                (id, acquisition_id, client_item_id, progress, state, created_at, updated_at)
            VALUES
                (@canonical_transfer, @canonical_acquisition, 'canonical-transfer', 0.25, 'downloading', @now, @now),
                (@legacy_transfer, @legacy_acquisition, 'legacy-transfer', 0.50, 'downloading', @now, @now),
                (@legacy_upgrade_transfer, @legacy_upgrade, 'legacy-upgrade-transfer', 0.75, 'downloading', @now, @now),
                (@upgrade_only_transfer, @upgrade_only_acquisition, 'upgrade-only-transfer', 0.90, 'seeding', @now, @now);
            """,
            connection);

        command.Parameters.AddWithValue("canonical_acquisition", fixture.CanonicalAcquisitionId);
        command.Parameters.AddWithValue("legacy_acquisition", fixture.LegacyAcquisitionId);
        command.Parameters.AddWithValue("canonical_upgrade", fixture.CanonicalUpgradeId);
        command.Parameters.AddWithValue("legacy_upgrade", fixture.LegacyUpgradeId);
        command.Parameters.AddWithValue("upgrade_only_acquisition", fixture.UpgradeOnlyAcquisitionId);
        command.Parameters.AddWithValue("non_conflicting_acquisition", fixture.NonConflictingAcquisitionId);
        command.Parameters.AddWithValue("canonical_entity", fixture.CanonicalEntityId);
        command.Parameters.AddWithValue("non_conflicting_entity", fixture.NonConflictingEntityId);
        command.Parameters.AddWithValue("downloading", AcquisitionStatus.Downloading.ToCode());
        command.Parameters.AddWithValue("pending", AcquisitionStatus.Pending.ToCode());
        command.Parameters.AddWithValue("canonical_monitor", fixture.CanonicalMonitorId);
        command.Parameters.AddWithValue("legacy_monitor", fixture.LegacyMonitorId);
        command.Parameters.AddWithValue("upgrade_only_monitor", fixture.UpgradeOnlyMonitorId);
        command.Parameters.AddWithValue("non_conflicting_monitor", fixture.NonConflictingMonitorId);
        command.Parameters.AddWithValue("paused", MonitorStatus.Paused.ToCode());
        command.Parameters.AddWithValue("active", MonitorStatus.Active.ToCode());
        command.Parameters.AddWithValue("fulfilled", MonitorStatus.Fulfilled.ToCode());
        command.Parameters.AddWithValue("canonical_root", fixture.CanonicalTargetRootId);
        command.Parameters.AddWithValue("legacy_root", fixture.LegacyTargetRootId);
        command.Parameters.AddWithValue("upgrade_only_root", fixture.UpgradeOnlyTargetRootId);
        command.Parameters.AddWithValue("non_conflicting_root", fixture.NonConflictingTargetRootId);
        command.Parameters.AddWithValue("canonical_profile", fixture.CanonicalProfileId);
        command.Parameters.AddWithValue("legacy_profile", fixture.LegacyProfileId);
        command.Parameters.AddWithValue("upgrade_only_profile", fixture.UpgradeOnlyProfileId);
        command.Parameters.AddWithValue("non_conflicting_profile", fixture.NonConflictingProfileId);
        command.Parameters.AddWithValue("canonical_created", fixture.CreatedAt);
        command.Parameters.AddWithValue("legacy_created", fixture.CreatedAt.AddMinutes(1));
        command.Parameters.AddWithValue("upgrade_only_created", fixture.CreatedAt.AddMinutes(2));
        command.Parameters.AddWithValue("non_conflicting_created", fixture.CreatedAt.AddMinutes(3));
        command.Parameters.AddWithValue("now", fixture.CreatedAt.AddMinutes(4));
        command.Parameters.AddWithValue("canonical_transfer", fixture.TransferIds[0]);
        command.Parameters.AddWithValue("legacy_transfer", fixture.TransferIds[1]);
        command.Parameters.AddWithValue("legacy_upgrade_transfer", fixture.TransferIds[2]);
        command.Parameters.AddWithValue("upgrade_only_transfer", fixture.TransferIds[3]);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Dictionary<Guid, MonitorSnapshot>> ReadMonitorsAsync(NpgsqlConnection connection) {
        await using var command = new NpgsqlCommand(
            """
            SELECT id, entity_id, acquisition_id, status, target_library_root_id, profile_id,
                   upgrade_attempts, barren_searches, upgrade_child_acquisition_id
            FROM monitors
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new Dictionary<Guid, MonitorSnapshot>();
        while (await reader.ReadAsync()) {
            result.Add(
                reader.GetGuid(0),
                new MonitorSnapshot(
                    NullableGuid(reader, 1),
                    NullableGuid(reader, 2),
                    reader.GetString(3),
                    NullableGuid(reader, 4),
                    NullableGuid(reader, 5),
                    reader.GetInt32(6),
                    reader.GetInt32(7),
                    NullableGuid(reader, 8)));
        }
        return result;
    }

    private static async Task<Dictionary<Guid, Guid?>> ReadAcquisitionEntityIdsAsync(NpgsqlConnection connection) {
        await using var command = new NpgsqlCommand("SELECT id, entity_id FROM acquisitions", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new Dictionary<Guid, Guid?>();
        while (await reader.ReadAsync()) {
            result.Add(reader.GetGuid(0), NullableGuid(reader, 1));
        }
        return result;
    }

    private static async Task<IReadOnlyList<Guid>> ReadTransferIdsAsync(NpgsqlConnection connection) {
        await using var command = new NpgsqlCommand("SELECT id FROM download_transfers", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<Guid>();
        while (await reader.ReadAsync()) {
            result.Add(reader.GetGuid(0));
        }
        return result;
    }

    private static async Task<Dictionary<Guid, string>> ReadMonitorPresetsAsync(NpgsqlConnection connection) {
        await using var command = new NpgsqlCommand("SELECT id, monitor_preset FROM monitors", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new Dictionary<Guid, string>();
        while (await reader.ReadAsync()) {
            result.Add(reader.GetGuid(0), reader.GetString(1));
        }
        return result;
    }

    private static async Task<Dictionary<Guid, Guid?>> ReadLegacyAliasesAsync(NpgsqlConnection connection) {
        await using var command = new NpgsqlCommand("SELECT id, book_entity_id FROM monitors", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new Dictionary<Guid, Guid?>();
        while (await reader.ReadAsync()) {
            result.Add(reader.GetGuid(0), NullableGuid(reader, 1));
        }
        return result;
    }

    private static async Task<bool> ColumnExistsAsync(
        NpgsqlConnection connection,
        string table,
        string column) {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = @table
                  AND column_name = @column)
            """,
            connection);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("column", column);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static Guid? NullableGuid(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);

    private sealed record MonitorSnapshot(
        Guid? EntityId,
        Guid? AcquisitionId,
        string Status,
        Guid? TargetLibraryRootId,
        Guid? ProfileId,
        int UpgradeAttempts,
        int BarrenSearches,
        Guid? UpgradeChildAcquisitionId);

    private sealed record MigrationFixture(
        Guid CanonicalEntityId,
        Guid NonConflictingEntityId,
        Guid CanonicalMonitorId,
        Guid LegacyMonitorId,
        Guid UpgradeOnlyMonitorId,
        Guid NonConflictingMonitorId,
        Guid CanonicalAcquisitionId,
        Guid LegacyAcquisitionId,
        Guid CanonicalUpgradeId,
        Guid LegacyUpgradeId,
        Guid UpgradeOnlyAcquisitionId,
        Guid NonConflictingAcquisitionId,
        Guid CanonicalTargetRootId,
        Guid LegacyTargetRootId,
        Guid UpgradeOnlyTargetRootId,
        Guid NonConflictingTargetRootId,
        Guid CanonicalProfileId,
        Guid LegacyProfileId,
        Guid UpgradeOnlyProfileId,
        Guid NonConflictingProfileId,
        IReadOnlyList<Guid> TransferIds,
        DateTimeOffset CreatedAt) {
        public static MigrationFixture Create() => new(
            Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()],
            DateTimeOffset.UtcNow.AddHours(-1));
    }

    private sealed class PostgresTestDatabase(
        string databaseName,
        string adminConnectionString,
        string connectionString) : IAsyncDisposable {
        public async Task<NpgsqlConnection> OpenConnectionAsync() {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            return connection;
        }

        public async Task MigrateAsync(string targetMigration) {
            await using var context = new PrismediaDbContext(
                new DbContextOptionsBuilder<PrismediaDbContext>()
                    .UseNpgsql(connectionString)
                    .Options);
            await context.GetService<IMigrator>().MigrateAsync(targetMigration);
        }

        public static async Task<PostgresTestDatabase> CreateAsync(string targetMigration) {
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
                    $"PostgreSQL migration test requires PRISMEDIA_TEST_DATABASE_URL or the local dev database: {exception.Message}");
            }

            var name = $"prismedia_migration_{Guid.NewGuid():N}";
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
            await using var drop = new NpgsqlCommand(
                $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)",
                admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
