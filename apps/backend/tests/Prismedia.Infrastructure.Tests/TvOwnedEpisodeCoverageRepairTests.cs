using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class TvOwnedEpisodeCoverageRepairTests : IDisposable {
    private readonly string root = Directory.CreateTempSubdirectory("prismedia-owned-coverage-").FullName;
    public void Dispose() => Directory.Delete(root, true);

    [Theory]
    [InlineData(1, 3, false)]
    [InlineData(1, 3, true)]
    [InlineData(0, null, false)]
    [InlineData(0, null, true)]
    [InlineData(0, 0, false)]
    [InlineData(0, 0, true)]
    public async Task MissingCoverageUsesCanonicalSeasonNumbersIncludingSpecials(int seasonNumber, int? displayOrder, bool postgres) {
        await using var database = postgres ? await PostgresTestDatabase.CreateAsync() : null;
        await using var db = database?.CreateContext() ?? CreateContext();
        var fixture = await SeedAsync(db);
        fixture.Season.SortOrder = displayOrder;
        db.EntityPositions.Add(new EntityPositionRow { EntityId = fixture.Season.Id, Code = EntityPositionCodes.Season, Value = seasonNumber });
        fixture.Receipt.SeasonNumber = seasonNumber;
        AcquisitionImportFileLedgerJson.TryDeserialize(fixture.Receipt.ImportResultJson, out var ledger);
        fixture.Receipt.ImportResultJson = AcquisitionImportFileLedgerJson.Serialize(ledger! with {
            Files = ledger.Files.Select(entry => entry with {
                SourceRelativePath = entry.SourceRelativePath.Replace("S01", $"S{seasonNumber:00}")
            }).ToArray()
        });
        await db.SaveChangesAsync();

        Assert.Equal(1, await Service(db).RepairAsync(fixture.Monitor.Id, fixture.Season.Id, (_, _) => Task.CompletedTask, default));
        Assert.Equal(fixture.Owner.Id, (await db.EntityFiles.AsNoTracking().SingleAsync(row => row.Id == fixture.Source.Id)).EntityId);
        Assert.Equal(fixture.Source.Path, (await db.EntityFiles.AsNoTracking().SingleAsync(row => row.EntityId == fixture.Missing.Id)).Path);
        Assert.Equal("unchanged paired video", await File.ReadAllTextAsync(fixture.Source.Path));
    }

    [Theory]
    [InlineData(2, null, false)]
    [InlineData(2, null, true)]
    [InlineData(0, null, false)]
    [InlineData(0, null, true)]
    [InlineData(0, 0, false)]
    [InlineData(0, 0, true)]
    public async Task WrongOwnerRepairUsesCanonicalDestinationIncludingSpecials(int seasonNumber, int? displayOrder, bool postgres) {
        await using var database = postgres ? await PostgresTestDatabase.CreateAsync() : null;
        await using var db = database?.CreateContext() ?? CreateContext();
        var (fixture, destination, first) = await SeedWrongOwnerAsync(db, true);
        destination.SortOrder = displayOrder;
        db.EntityPositions.Add(new EntityPositionRow { EntityId = destination.Id, Code = EntityPositionCodes.Season, Value = seasonNumber });
        await db.SaveChangesAsync();

        Assert.Equal(2, await Service(db).RepairAsync(fixture.Monitor.Id, destination.Id, (_, _) => Task.CompletedTask, default));
        Assert.Equal(first.Id, (await db.EntityFiles.AsNoTracking().SingleAsync(row => row.Id == fixture.Source.Id)).EntityId);
        Assert.False(await db.Entities.AnyAsync(row => row.Id == fixture.Owner.Id));
        Assert.Equal("unchanged paired video", await File.ReadAllTextAsync(fixture.Source.Path));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DuplicateCanonicalSeasonOwnershipHoldsRepairForReview(bool reassign, bool postgres) {
        await using var database = postgres ? await PostgresTestDatabase.CreateAsync() : null;
        await using var db = database?.CreateContext() ?? CreateContext();
        Fixture fixture;
        EntityRow destination;
        if (reassign) (fixture, destination, _) = await SeedWrongOwnerAsync(db, true);
        else { fixture = await SeedAsync(db); destination = fixture.Season; }
        // An empty duplicate season is absent from the episode catalog, but still conflicts with
        // the physical ownership layout. No repair may elect one of those season identities.
        db.Entities.Add(new EntityRow {
            Id = Guid.NewGuid(), KindCode = EntityKind.VideoSeason.ToCode(),
            ParentEntityId = destination.ParentEntityId, SortOrder = destination.SortOrder,
            Title = "Conflicting season", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        Assert.Equal(0, await Service(db).RepairAsync(fixture.Monitor.Id, destination.Id,
            (_, _) => Task.CompletedTask, default));
        Assert.Equal(fixture.Owner.Id, (await db.EntityFiles.AsNoTracking().SingleAsync(row => row.Id == fixture.Source.Id)).EntityId);
        Assert.True((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == fixture.Missing.Id)).IsWanted);
        Assert.Equal("unchanged paired video", await File.ReadAllTextAsync(fixture.Source.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrongScannerOwnerIsRetiredOnlyAfterItsStableSourceIsBoundToTheProvenWantedPair(bool foreign) {
        await using var db = CreateContext();
        var (fixture, destination, first) = await SeedWrongOwnerAsync(db, foreign);
        db.EntityTechnical.Add(new EntityTechnicalRow { EntityId = first.Id, DurationSeconds = 10, ProbeFailedAt = DateTimeOffset.UtcNow });
        db.UserEntityStates.Add(new UserEntityStateRow { UserId = Guid.NewGuid(), EntityId = first.Id, IsFavorite = true, ResumeSeconds = 12 });
        await db.SaveChangesAsync();
        var queued = new List<Guid>();

        Assert.Equal(2, await Service(db).RepairAsync(fixture.Monitor.Id, destination.Id,
            (id, _) => { queued.Add(id); return Task.CompletedTask; }, default));
        Assert.Equal(0, await Service(db).RepairAsync(fixture.Monitor.Id, destination.Id,
            (_, _) => throw new InvalidOperationException("A completed repair must not repeat"), default));

        Assert.Equal(new[] { first.Id, fixture.Missing.Id }.Order(), queued.Order());
        var source = await db.EntityFiles.AsNoTracking().SingleAsync(row => row.Id == fixture.Source.Id);
        Assert.Equal(first.Id, source.EntityId);
        Assert.Equal(fixture.Source.Path, source.Path);
        Assert.False(await db.Entities.AnyAsync(row => row.Id == fixture.Owner.Id));
        Assert.False((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == first.Id)).IsWanted);
        Assert.False((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == fixture.Missing.Id)).IsWanted);
        Assert.False(await db.EntityTechnical.AnyAsync(row => row.EntityId == first.Id));
        var retainedState = await db.UserEntityStates.SingleAsync(row => row.EntityId == first.Id);
        Assert.True(retainedState.IsFavorite);
        Assert.Equal(12, retainedState.ResumeSeconds);
        Assert.Equal(2, await db.EntityFiles.CountAsync(row => row.Role == EntityFileRole.Source && row.Path == source.Path));
        Assert.Equal("unchanged paired video", await File.ReadAllTextAsync(source.Path));
        var history = await db.AcquisitionHistory.SingleAsync();
        Assert.Equal(AcquisitionHistoryEvent.MappingRepaired, history.Event);
        Assert.Contains(fixture.Owner.Title, history.Message);
        if (foreign) Assert.False(await db.EntitySources.AnyAsync(row => row.EntityId == destination.Id));
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("manual-receipt")]
    [InlineData("manual-name")]
    [InlineData("provider-identity")]
    [InlineData("playback-history")]
    [InlineData("user-state")]
    [InlineData("changed-file")]
    [InlineData("changed-source")]
    [InlineData("occupied-target")]
    [InlineData("extra-owner")]
    [InlineData("newer-manual-receipt")]
    [InlineData("active-source-job")]
    [InlineData("position-disagrees")]
    public async Task WrongOwnerWithProtectedOrSupersededEvidenceRemainsAvailableForReview(string change) {
        await using var db = CreateContext();
        var (fixture, destination, first) = await SeedWrongOwnerAsync(db, true);
        switch (change) {
            case "paused": fixture.Monitor.Status = MonitorStatus.Paused; break;
            case "manual-receipt": fixture.Receipt.ImportManualReview = true; break;
            case "manual-name": fixture.Owner.Title = "My reviewed episode"; break;
            case "provider-identity": db.EntityExternalIds.Add(new EntityExternalIdRow {
                EntityId = fixture.Owner.Id, Provider = "test-provider", Value = "reviewed-episode"
            }); break;
            case "playback-history": db.EntityConsumptionEvents.Add(new EntityConsumptionEventRow {
                Id = Guid.NewGuid(), EntityId = fixture.Owner.Id, Kind = ConsumptionEventKind.Completed,
                OccurredAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
            }); break;
            case "user-state": db.UserEntityStates.Add(new UserEntityStateRow {
                UserId = Guid.NewGuid(), EntityId = fixture.Owner.Id, IsFavorite = true
            }); break;
            case "changed-file": await File.AppendAllTextAsync(fixture.Source.Path, "changed"); break;
            case "changed-source": fixture.Source.UpdatedAt = fixture.Receipt.UpdatedAt.AddMinutes(1); break;
            case "occupied-target": db.EntityFiles.Add(Source(first.Id, Path.Combine(root, "other.mkv"), DateTimeOffset.UtcNow)); break;
            case "extra-owner":
                var other = Episode(fixture.Season.Id, 50, "Another Story", false);
                db.Entities.Add(other); db.EntityFiles.Add(Source(other.Id, fixture.Source.Path, fixture.Source.CreatedAt)); break;
            case "newer-manual-receipt": db.Acquisitions.Add(new AcquisitionRow {
                Id = Guid.NewGuid(), EntityId = fixture.Season.Id, Kind = EntityKind.VideoSeason,
                Status = AcquisitionStatus.Imported, ImportManualReview = true, ImportResultJson = fixture.Receipt.ImportResultJson,
                UpdatedAt = fixture.Receipt.UpdatedAt.AddMinutes(1)
            }); break;
            case "active-source-job": db.JobRuns.Add(new JobRunRow {
                Id = Guid.NewGuid(), Type = JobType.ProbeVideo, Status = JobRunStatus.Running,
                TargetEntityId = fixture.Owner.Id.ToString()
            }); break;
            case "position-disagrees": db.EntityPositions.Add(new EntityPositionRow {
                EntityId = fixture.Owner.Id, Code = EntityPositionCodes.Episode, Value = 50
            }); break;
        }
        await db.SaveChangesAsync();

        Assert.Equal(0, await Service(db).RepairAsync(fixture.Monitor.Id, destination.Id,
            (_, _) => throw new InvalidOperationException("Protected owner must remain unchanged"), default));
        Assert.Equal(fixture.Owner.Id, (await db.EntityFiles.AsNoTracking().SingleAsync(row => row.Id == fixture.Source.Id)).EntityId);
        Assert.True((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == first.Id)).IsWanted);
        Assert.Empty(await db.AcquisitionHistory.ToArrayAsync());
    }

    [Fact]
    public async Task RemappingQueueFailureRollsBackTheStableSourceRetirementAndHistory() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        Fixture fixture; EntityRow destination; EntityRow first;
        await using (var setup = database.CreateContext()) (fixture, destination, first) = await SeedWrongOwnerAsync(setup, true);
        await using (var db = database.CreateContext()) {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).RepairAsync(fixture.Monitor.Id,
                destination.Id, (_, _) => throw new InvalidOperationException("queue unavailable"), default));
        }
        await using (var db = database.CreateContext()) {
            Assert.Equal(fixture.Owner.Id, (await db.EntityFiles.SingleAsync(row => row.Id == fixture.Source.Id)).EntityId);
            Assert.True(await db.Entities.AnyAsync(row => row.Id == fixture.Owner.Id));
            Assert.True((await db.Entities.SingleAsync(row => row.Id == first.Id)).IsWanted);
            Assert.Empty(await db.AcquisitionHistory.ToArrayAsync());
            Assert.Equal(2, await Service(db).RepairAsync(fixture.Monitor.Id, destination.Id, (_, _) => Task.CompletedTask, default));
            Assert.Equal(first.Id, (await db.EntityFiles.AsNoTracking().SingleAsync(row => row.Id == fixture.Source.Id)).EntityId);
            Assert.Equal(AcquisitionHistoryEvent.MappingRepaired, (await db.AcquisitionHistory.SingleAsync()).Event);
        }
    }

    [Fact]
    public async Task PausingTheDestinationWhileRemappingWaitsPreservesTheOriginalOwner() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (fixture, destination, first) = await SeedWrongOwnerAsync(db, true);
        var lease = new BeforeLease(new EfEntityLifecycleMutationLease(db, new EfEntityHierarchyReader(db)), async () => {
            await using var concurrent = database.CreateContext();
            await concurrent.Monitors.Where(row => row.Id == fixture.Monitor.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, MonitorStatus.Paused));
        });
        Assert.Equal(0, await Service(db, lease).RepairAsync(fixture.Monitor.Id, destination.Id,
            (_, _) => throw new InvalidOperationException("Paused destination must not enqueue"), default));
        Assert.Equal(fixture.Owner.Id, (await db.EntityFiles.AsNoTracking().SingleAsync(row => row.Id == fixture.Source.Id)).EntityId);
        Assert.True((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == first.Id)).IsWanted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProviderOrPlaybackEvidenceAddedWhileRemappingWaitsInvalidatesItsSnapshot(bool providerChanges) {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (fixture, destination, first) = await SeedWrongOwnerAsync(db, true);
        var lease = new BeforeLease(new EfEntityLifecycleMutationLease(db, new EfEntityHierarchyReader(db)), async () => {
            await using var concurrent = database.CreateContext();
            if (providerChanges) concurrent.EntityExternalIds.Add(new EntityExternalIdRow {
                EntityId = first.Id, Provider = "test-provider", Value = "new-identity"
            });
            else concurrent.EntityConsumptionEvents.Add(new EntityConsumptionEventRow {
                Id = Guid.NewGuid(), EntityId = fixture.Owner.Id, Kind = ConsumptionEventKind.Completed,
                CreatedAt = DateTimeOffset.UtcNow, OccurredAt = DateTimeOffset.UtcNow
            });
            await concurrent.SaveChangesAsync();
        });
        Assert.Equal(0, await Service(db, lease).RepairAsync(fixture.Monitor.Id, destination.Id,
            (_, _) => throw new InvalidOperationException("Changed evidence must not enqueue"), default));
        Assert.Equal(fixture.Owner.Id, (await db.EntityFiles.AsNoTracking().SingleAsync(row => row.Id == fixture.Source.Id)).EntityId);
        Assert.True((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == first.Id)).IsWanted);
    }

    [Theory]
    [InlineData("automatic", 2)]
    [InlineData("manual-origin", 0)]
    [InlineData("manual-between", 0)]
    [InlineData("missing-origin", 0)]
    [InlineData("changed-original-path", 0)]
    public async Task AdoptionRetriesNeedAnUnbrokenAutomaticReceiptChain(string change, int expected) {
        await using var db = CreateContext();
        var (fixture, destination, first) = await SeedWrongOwnerAsync(db, true);
        AcquisitionImportFileLedgerJson.TryDeserialize(fixture.Receipt.ImportResultJson, out var ledger);
        var adopted = ledger! with { Files = ledger.Files.Select(entry => entry with {
            Decision = AcquisitionImportDecision.AdoptExisting
        }).ToArray() };
        AcquisitionRow Adoption(int seconds) => new() {
            Id = Guid.NewGuid(), EntityId = fixture.Season.Id, Kind = EntityKind.VideoSeason, Status = AcquisitionStatus.Imported,
            TargetLibraryRootId = fixture.Receipt.TargetLibraryRootId, FinalSourcePath = fixture.Receipt.FinalSourcePath,
            SelectedReleaseJson = fixture.Receipt.SelectedReleaseJson, ImportResultJson = AcquisitionImportFileLedgerJson.Serialize(adopted),
            CreatedAt = fixture.Receipt.UpdatedAt.AddSeconds(seconds), UpdatedAt = fixture.Receipt.UpdatedAt.AddSeconds(seconds + 1)
        };
        var middle = Adoption(10);
        var latest = Adoption(20);
        db.Acquisitions.AddRange(middle, latest);
        if (change == "manual-origin") fixture.Receipt.ImportManualReview = true;
        if (change == "manual-between") middle.ImportManualReview = true;
        if (change == "missing-origin") db.Acquisitions.Remove(fixture.Receipt);
        if (change == "changed-original-path") latest.ImportResultJson = AcquisitionImportFileLedgerJson.Serialize(adopted with {
            Files = adopted.Files.Select(entry => entry with { SourceRelativePath = "another-payload/" + entry.SourceRelativePath }).ToArray()
        });
        await db.SaveChangesAsync();

        Assert.Equal(expected, await Service(db).RepairAsync(fixture.Monitor.Id, destination.Id, (_, _) => Task.CompletedTask, default));
        var owner = (await db.EntityFiles.AsNoTracking().SingleAsync(row => row.Id == fixture.Source.Id)).EntityId;
        Assert.Equal(expected == 0 ? fixture.Owner.Id : first.Id, owner);
        Assert.Equal(expected == 0, (await db.Entities.AsNoTracking().SingleAsync(row => row.Id == first.Id)).IsWanted);
    }

    private async Task<(Fixture Fixture, EntityRow Destination, EntityRow First)> SeedWrongOwnerAsync(
        PrismediaDbContext db, bool foreign) {
        var fixture = await SeedAsync(db);
        var oldPath = fixture.Source.Path;
        fixture.Source.Path = Path.Combine(Path.GetDirectoryName(oldPath)!, "Show - S01E49.mkv");
        File.Move(oldPath, fixture.Source.Path);
        AcquisitionImportFileLedgerJson.TryDeserialize(fixture.Receipt.ImportResultJson, out var ledger);
        fixture.Receipt.ImportResultJson = AcquisitionImportFileLedgerJson.Serialize(ledger! with {
            Files = ledger.Files.Select(entry => entry with { DestinationRelativePath = Path.GetRelativePath(root, fixture.Source.Path) }).ToArray()
        });
        fixture.Owner.Title = Path.GetFileNameWithoutExtension(fixture.Source.Path);
        fixture.Owner.SortOrder = 49;
        var destination = fixture.Season;
        if (foreign) {
            destination = new EntityRow { Id = Guid.NewGuid(), KindCode = EntityKind.VideoSeason.ToCode(),
                Title = "Season 2", ParentEntityId = fixture.Season.ParentEntityId, SortOrder = 2,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
            db.Entities.Add(destination);
            await db.SaveChangesAsync();
            fixture.Missing.ParentEntityId = destination.Id;
            fixture.Monitor.EntityId = destination.Id;
            fixture.Monitor.AcquisitionId = null;
        }
        var first = Episode(destination.Id, 1, "Hidden Garden", true);
        db.Entities.Add(first);
        await db.SaveChangesAsync();
        return (fixture, destination, first);
    }

    [Fact]
    public async Task ImportedPairRestoresOnlyMissingLinkAndPreservesExistingSourceAndFile() {
        await using var db = CreateContext();
        var fixture = await SeedAsync(db);
        var queued = new List<Guid>();
        var service = Service(db);

        Assert.Equal(1, await service.RepairAsync(fixture.Monitor.Id, fixture.Season.Id,
            (id, _) => { queued.Add(id); return Task.CompletedTask; }, default));
        Assert.Equal(0, await service.RepairAsync(fixture.Monitor.Id, fixture.Season.Id,
            (id, _) => { queued.Add(id); return Task.CompletedTask; }, default));

        Assert.Equal([fixture.Missing.Id], queued);
        Assert.False((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == fixture.Missing.Id)).IsWanted);
        var sources = await db.EntityFiles.AsNoTracking().Where(row => row.Role == EntityFileRole.Source).ToArrayAsync();
        Assert.Equal(2, sources.Length);
        Assert.Contains(sources, source => source.Id == fixture.Source.Id && source.EntityId == fixture.Owner.Id);
        Assert.All(sources, source => Assert.Equal(fixture.Source.Path, source.Path));
        Assert.Equal("unchanged paired video", await File.ReadAllTextAsync(fixture.Source.Path));
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("manual")]
    [InlineData("changed-size")]
    [InlineData("changed-time")]
    [InlineData("missing-file")]
    [InlineData("other-owner")]
    [InlineData("owned-target")]
    [InlineData("replacement")]
    [InlineData("newer-manual-receipt")]
    [InlineData("duplicate-position")]
    [InlineData("missing-root")]
    [InlineData("disabled-root")]
    [InlineData("changed-root")]
    public async Task ChangedOrAmbiguousEvidenceDoesNotAlterCoverage(string change) {
        await using var db = CreateContext();
        var fixture = await SeedAsync(db);
        switch (change) {
            case "paused": fixture.Monitor.Status = MonitorStatus.Paused; break;
            case "manual": fixture.Receipt.ImportManualReview = true; break;
            case "changed-size": await File.AppendAllTextAsync(fixture.Source.Path, " changed"); break;
            case "changed-time": File.SetLastWriteTimeUtc(fixture.Source.Path, DateTime.UtcNow.AddHours(1)); break;
            case "missing-file": File.Delete(fixture.Source.Path); break;
            case "other-owner":
                var unrelated = Episode(fixture.Season.Id, 3, "Ocean Adventure", false);
                db.Entities.Add(unrelated);
                db.EntityFiles.Add(Source(unrelated.Id, fixture.Source.Path, fixture.Source.CreatedAt));
                break;
            case "owned-target": db.EntityFiles.Add(Source(fixture.Missing.Id, Path.Combine(root, "other.mkv"), fixture.Source.CreatedAt)); break;
            case "replacement":
                db.Acquisitions.Add(new AcquisitionRow { Id = Guid.NewGuid(), EntityId = fixture.Owner.Id,
                    Kind = EntityKind.VideoEpisode, Status = AcquisitionStatus.Downloading, UpdatedAt = DateTimeOffset.UtcNow });
                break;
            case "newer-manual-receipt":
                db.Acquisitions.Add(new AcquisitionRow { Id = Guid.NewGuid(), EntityId = fixture.Season.Id,
                    Kind = EntityKind.VideoSeason, Status = AcquisitionStatus.Imported, ImportManualReview = true,
                    FinalSourcePath = fixture.Receipt.FinalSourcePath, ImportResultJson = fixture.Receipt.ImportResultJson,
                    UpdatedAt = fixture.Receipt.UpdatedAt.AddSeconds(1) });
                break;
            case "duplicate-position": db.Entities.Add(Episode(fixture.Season.Id, 2, "Mountain Journey", true)); break;
            case "missing-root":
                db.EntityLibraryRoots.RemoveRange(await db.EntityLibraryRoots.ToArrayAsync());
                fixture.Receipt.TargetLibraryRootId = null;
                break;
            case "disabled-root": (await db.LibraryRoots.SingleAsync()).Enabled = false; break;
            case "changed-root": (await db.LibraryRoots.SingleAsync()).Path = Path.Combine(root, "other-library"); break;
        }
        await db.SaveChangesAsync();

        Assert.Equal(0, await Service(db).RepairAsync(fixture.Monitor.Id, fixture.Season.Id,
            (_, _) => throw new InvalidOperationException("Rejected evidence must not enqueue"), default));
        Assert.True((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == fixture.Missing.Id)).IsWanted);
    }

    [Fact]
    public async Task ImportedReceiptSuppliesItsCapturedRootWhenAnOlderSeriesHasNoRootAssociation() {
        await using var db = CreateContext();
        var fixture = await SeedAsync(db);
        db.EntityLibraryRoots.RemoveRange(await db.EntityLibraryRoots.ToArrayAsync());
        await db.SaveChangesAsync();

        Assert.Equal(1, await Service(db).RepairAsync(fixture.Monitor.Id, fixture.Season.Id,
            (_, _) => Task.CompletedTask, default));
    }

    [Fact]
    public async Task MonitorPausedAfterPlanningWinsOverTrackedActiveState() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedAsync(db);
        var lease = new BeforeLease(new EfEntityLifecycleMutationLease(db, new EfEntityHierarchyReader(db)), async () => {
            await using var concurrent = database.CreateContext();
            await concurrent.Monitors.Where(row => row.Id == fixture.Monitor.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, MonitorStatus.Paused));
        });

        Assert.Equal(0, await Service(db, lease).RepairAsync(fixture.Monitor.Id, fixture.Season.Id,
            (_, _) => throw new InvalidOperationException("Paused monitor must not enqueue"), default));
        Assert.True((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == fixture.Missing.Id)).IsWanted);
    }

    [Fact]
    public async Task QueueFailureRollsBackBindingsAndFreshRetryRestoresCoverage() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        Fixture fixture;
        await using (var setup = database.CreateContext()) fixture = await SeedAsync(setup);
        await using (var db = database.CreateContext()) {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).RepairAsync(
                fixture.Monitor.Id, fixture.Season.Id, async (_, token) => {
                    Assert.NotNull(db.Database.CurrentTransaction);
                    Assert.False((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == fixture.Missing.Id, token)).IsWanted);
                    throw new InvalidOperationException("queue unavailable");
                }, default));
        }
        await using (var db = database.CreateContext()) {
            Assert.True((await db.Entities.SingleAsync(row => row.Id == fixture.Missing.Id)).IsWanted);
            Assert.Single(await db.EntityFiles.ToArrayAsync());
            Assert.Equal(1, await Service(db).RepairAsync(fixture.Monitor.Id, fixture.Season.Id,
                (_, _) => Task.CompletedTask, default));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MetadataOrOwnershipChangedWhileWaitingInvalidatesThePlannedEpisode(bool metadataChanges) {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedAsync(db);
        var lease = new BeforeLease(new EfEntityLifecycleMutationLease(db, new EfEntityHierarchyReader(db)), async () => {
            await using var concurrent = database.CreateContext();
            if (metadataChanges) {
                await concurrent.Entities.Where(row => row.Id == fixture.Missing.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Title, "Ocean Adventure"));
            } else {
                concurrent.EntityFiles.Add(Source(fixture.Missing.Id, Path.Combine(root, "other.mkv"), DateTimeOffset.UtcNow));
                await concurrent.SaveChangesAsync();
            }
        });

        Assert.Equal(0, await Service(db, lease).RepairAsync(fixture.Monitor.Id, fixture.Season.Id,
            (_, _) => throw new InvalidOperationException("Changed mapping must not enqueue"), default));
        Assert.True((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == fixture.Missing.Id)).IsWanted);
    }

    private EfTvOwnedEpisodeCoverageRepair Service(PrismediaDbContext db, IEntityLifecycleMutationLease? lease = null) =>
        new(db, new EfImportTargetIndex(db), lease ?? new EfEntityLifecycleMutationLease(db, new EfEntityHierarchyReader(db)));

    private async Task<Fixture> SeedAsync(PrismediaDbContext db) {
        var now = DateTimeOffset.UtcNow;
        var series = new EntityRow { Id = Guid.NewGuid(), KindCode = EntityKind.VideoSeries.ToCode(), Title = "Show", CreatedAt = now, UpdatedAt = now };
        var season = new EntityRow { Id = Guid.NewGuid(), ParentEntityId = series.Id, KindCode = EntityKind.VideoSeason.ToCode(),
            Title = "Season 1", SortOrder = 1, CreatedAt = now, UpdatedAt = now };
        var owner = Episode(season.Id, 1, "Hidden Garden", false);
        var missing = Episode(season.Id, 2, "Mountain Journey", true);
        var rootId = Guid.NewGuid();
        db.LibraryRoots.Add(new LibraryRootRow { Id = rootId, Path = root, Label = "Test", CreatedAt = now, UpdatedAt = now });
        db.Entities.Add(series);
        await db.SaveChangesAsync();
        db.Entities.Add(season);
        await db.SaveChangesAsync();
        db.Entities.AddRange(owner, missing);
        var folder = Directory.CreateDirectory(Path.Combine(root, "Show", "Season 01")).FullName;
        var source = Source(owner.Id, Path.Combine(folder, "Show - S01E01.mkv"), now);
        await File.WriteAllTextAsync(source.Path, "unchanged paired video");
        File.SetLastWriteTimeUtc(source.Path, now.AddMinutes(-1).UtcDateTime);
        db.EntityFiles.Add(source);
        db.EntityLibraryRoots.Add(new EntityLibraryRootRow { EntityId = series.Id, LibraryRootId = rootId });
        db.EntitySources.AddRange(
            new EntitySourceRow { EntityId = series.Id, Code = EntitySourceCode.Folder.ToCode(), Value = Path.GetDirectoryName(folder)! },
            new EntitySourceRow { EntityId = season.Id, Code = EntitySourceCode.Folder.ToCode(), Value = folder });
        var receipt = new AcquisitionRow { Id = Guid.NewGuid(), EntityId = season.Id, Kind = EntityKind.VideoSeason,
            Status = AcquisitionStatus.Imported, SeasonNumber = 1, Title = "Show", TargetLibraryRootId = rootId,
            FinalSourcePath = folder, SelectedReleaseJson = JsonSerializer.Serialize(new SelectedRelease("Show S01", "Indexer", null)),
            CreatedAt = now.AddMinutes(-1), UpdatedAt = now.AddSeconds(1),
            ImportResultJson = AcquisitionImportFileLedgerJson.Serialize(new(AcquisitionImportPhase.Imported, [
                new("pair", "Show.S01E01-E02.Hidden.Garden.&.Mountain.Journey.mkv", source.SizeBytes!.Value,
                    "Show.S01E01-E02.Hidden.Garden.&.Mountain.Journey.mkv", Path.GetRelativePath(root, source.Path),
                    AcquisitionImportFileRole.Media, AcquisitionImportContentKind.Video,
                    AcquisitionImportFileStatus.Imported, AcquisitionImportDecision.PlaceNew, null)
            ])) };
        var monitor = new MonitorRow { Id = Guid.NewGuid(), EntityId = season.Id, AcquisitionId = receipt.Id,
            Kind = EntityKind.VideoSeason, Title = season.Title, Status = MonitorStatus.Active, CreatedAt = now, UpdatedAt = now };
        db.Acquisitions.Add(receipt);
        db.Monitors.Add(monitor);
        await db.SaveChangesAsync();
        return new(season, owner, missing, source, receipt, monitor);
    }

    private static EntityRow Episode(Guid seasonId, int number, string title, bool wanted) => new() {
        Id = Guid.NewGuid(), ParentEntityId = seasonId, KindCode = EntityKind.VideoEpisode.ToCode(), Title = title,
        SortOrder = number, IsWanted = wanted, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
    };
    private static EntityFileRow Source(Guid entityId, string path, DateTimeOffset now) => new() {
        Id = Guid.NewGuid(), EntityId = entityId, Role = EntityFileRole.Source, Path = path, SizeBytes = 22,
        MimeType = "video/x-matroska", CreatedAt = now, UpdatedAt = now
    };
    private static PrismediaDbContext CreateContext() => new(new DbContextOptionsBuilder<PrismediaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed record Fixture(EntityRow Season, EntityRow Owner, EntityRow Missing, EntityFileRow Source,
        AcquisitionRow Receipt, MonitorRow Monitor);
    private sealed class BeforeLease(IEntityLifecycleMutationLease inner, Func<Task> before) : IEntityLifecycleMutationLease {
        public Task<bool> ExecuteAsync(Guid entityId, Func<CancellationToken, Task> mutation, CancellationToken cancellationToken) =>
            ExecuteManyAsync([entityId], mutation, cancellationToken);
        public async Task<bool> ExecuteManyAsync(IReadOnlyCollection<Guid> ids, Func<CancellationToken, Task> mutation, CancellationToken token) {
            await before();
            return await inner.ExecuteManyAsync(ids, mutation, token);
        }
    }
}
