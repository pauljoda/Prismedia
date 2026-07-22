using Microsoft.EntityFrameworkCore;
using Npgsql;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Xunit.Sdk;

namespace Prismedia.Infrastructure.Tests;

/// <summary>PostgreSQL constraint coverage for merging an enriched scanner-created audio duplicate.</summary>
public sealed class AudioTrackReconciliationPostgresTests {
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task EnrichedDuplicateMovesDependentsBeforePostgresCascadesItsEntity() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var albumId = Guid.NewGuid();
        var wantedId = Guid.NewGuid();
        var duplicateId = Guid.NewGuid();
        var sourceFileId = Guid.NewGuid();
        var sourcePath = "/media/Divide Music/WAR/01 - WAR.mp3";
        var now = DateTimeOffset.UtcNow;

        await using (var setup = database.CreateContext()) {
            setup.Entities.AddRange(
                new EntityRow {
                    Id = albumId,
                    KindCode = EntityKindRegistry.AudioLibrary.Code,
                    Title = "WAR",
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new EntityRow {
                    Id = wantedId,
                    KindCode = EntityKindRegistry.AudioTrack.Code,
                    Title = "WAR",
                    ParentEntityId = albumId,
                    IsWanted = true,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new EntityRow {
                    Id = duplicateId,
                    KindCode = EntityKindRegistry.AudioTrack.Code,
                    Title = "01 - WAR",
                    ParentEntityId = albumId,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            setup.EntityFiles.AddRange(
                new EntityFileRow {
                    Id = sourceFileId,
                    EntityId = duplicateId,
                    Role = EntityFileRole.Source,
                    Path = sourcePath,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new EntityFileRow {
                    Id = Guid.NewGuid(),
                    EntityId = duplicateId,
                    Role = EntityFileRole.Waveform,
                    Path = AssetPathService.AudioWaveformUrl(duplicateId),
                    MimeType = "application/json",
                    CreatedAt = now,
                    UpdatedAt = now
                });
            setup.EntityTechnical.Add(new EntityTechnicalRow {
                EntityId = duplicateId,
                DurationSeconds = 206.04,
                Codec = MediaCodecs.Mp3,
                UpdatedAt = now
            });
            setup.EntityFileFingerprints.Add(new EntityFileFingerprintRow {
                Id = Guid.NewGuid(),
                EntityId = duplicateId,
                EntityFileId = sourceFileId,
                Algorithm = FingerprintAlgorithm.Md5,
                Value = "war-md5",
                CreatedAt = now
            });
            setup.AudioTrackDetails.Add(new AudioTrackDetailRow {
                EntityId = duplicateId,
                EmbeddedArtist = "Divide Music",
                EmbeddedAlbum = "WAR"
            });
            await setup.SaveChangesAsync();
        }

        await using (var context = database.CreateContext()) {
            var result = await new AcquisitionHintApplier(context).ReconcileWantedAudioTrackAsync(
                albumId,
                sourcePath,
                "01 - WAR",
                0,
                CancellationToken.None);

            Assert.Equal(wantedId, result?.EntityId);
            Assert.True(result?.NeedsWaveformRegeneration);
        }

        await using (var verify = database.CreateContext()) {
            Assert.False(await verify.Entities.AnyAsync(row => row.Id == duplicateId));
            Assert.False((await verify.Entities.SingleAsync(row => row.Id == wantedId)).IsWanted);
            Assert.Equal(sourceFileId, (await verify.EntityFiles.SingleAsync(row =>
                row.EntityId == wantedId && row.Role == EntityFileRole.Source)).Id);
            Assert.False(await verify.EntityFiles.AnyAsync(row =>
                row.EntityId == wantedId && row.Role == EntityFileRole.Waveform));
            Assert.Equal(206.04, (await verify.EntityTechnical.SingleAsync(row => row.EntityId == wantedId)).DurationSeconds);
            var fingerprint = await verify.EntityFileFingerprints.SingleAsync(row => row.EntityId == wantedId);
            Assert.Equal(sourceFileId, fingerprint.EntityFileId);
            Assert.Equal("war-md5", fingerprint.Value);
            var detail = await verify.AudioTrackDetails.SingleAsync(row => row.EntityId == wantedId);
            Assert.Equal("Divide Music", detail.EmbeddedArtist);
            Assert.Equal("WAR", detail.EmbeddedAlbum);
        }
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
                    $"PostgreSQL audio reconciliation test requires PRISMEDIA_TEST_DATABASE_URL or the local dev database: {exception.Message}");
            }

            var name = $"prismedia_audio_reconcile_{Guid.NewGuid():N}";
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
