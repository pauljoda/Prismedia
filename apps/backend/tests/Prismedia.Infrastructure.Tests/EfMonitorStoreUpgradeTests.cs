using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Covers the upgrade-until-cutoff due-logic and the upgrade-child lifecycle in <see cref="EfMonitorStore"/>:
/// when an imported book is due for an upgrade re-search, when it fulfills instead (cutoff met or upgrade
/// off), the one-in-flight interlock, durable intent across repeated misses, and success/failure counters.
/// </summary>
public sealed class EfMonitorStoreUpgradeTests {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "PostgreSQL")]
    public async Task BaselineRestorationRechecksTheMonitorUnderItsLifecycleLease(bool pauseWhileWaiting) {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var root = Directory.CreateTempSubdirectory("prismedia-baseline-postgres-").FullName;
        try {
            var now = DateTimeOffset.UtcNow;
            var rootId = Guid.NewGuid(); var entityId = Guid.NewGuid(); var profileId = Guid.NewGuid();
            var monitorId = Guid.NewGuid(); var acquisitionId = Guid.NewGuid();
            var path = Path.Combine(root, "owned.mkv");
            await File.WriteAllTextAsync(path, "owned video bytes");
            db.LibraryRoots.Add(new LibraryRootRow { Id = rootId, Path = root, Label = "Validation", Enabled = true,
                CreatedAt = now, UpdatedAt = now });
            db.Entities.Add(new EntityRow { Id = entityId, KindCode = EntityKind.Movie.ToCode(), Title = "Movie",
                CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
            db.BookAcquisitionProfiles.Add(new BookAcquisitionProfileRow {
                Id = profileId, Kind = EntityKind.Movie, DisplayName = "HD", IsDefault = true, TargetLibraryRootId = rootId,
                AutoPick = true, UpgradeUntilCutoff = true, CutoffQuality = VideoQuality.Webdl1080p.ToCode(), CreatedAt = now, UpdatedAt = now
            });
            db.EntityFiles.Add(new EntityFileRow { Id = Guid.NewGuid(), EntityId = entityId, Role = EntityFileRole.Source,
                Path = path, SizeBytes = new FileInfo(path).Length, Source = FileSourceKind.Scan.ToCode(), CreatedAt = now, UpdatedAt = now });
            db.EntitySubtitleStates.Add(new EntitySubtitleStateRow { EntityId = entityId, SubtitlesExtractedAt = now });
            db.Acquisitions.Add(new AcquisitionRow { Id = acquisitionId, EntityId = entityId, Kind = EntityKind.Movie,
                Title = "Movie", Status = AcquisitionStatus.Imported, FinalSourcePath = path, UpgradeQualityCaptured = true,
                OwnedMediaQuality = VideoQuality.Webdl720p.ToCode(), CreatedAt = now, UpdatedAt = now });
            db.Monitors.Add(new MonitorRow { Id = monitorId, EntityId = entityId, Kind = EntityKind.Movie,
                Title = "Movie", Status = MonitorStatus.Active, ProfileId = profileId, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
            var lease = new BeforeBaselineLease(new EfEntityLifecycleMutationLease(db, new EfEntityHierarchyReader(db)), async () => {
                if (!pauseWhileWaiting) return;
                await using var other = database.CreateContext();
                await other.Monitors.Where(row => row.Id == monitorId).ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, MonitorStatus.Paused));
            });

            var due = await new EfMonitorStore(db, lifecycleLease: lease).ListImmediateForMonitorAsync(monitorId, default);

            var persisted = await db.Monitors.AsNoTracking().SingleAsync();
            if (pauseWhileWaiting) {
                Assert.Empty(due);
                Assert.Equal(MonitorStatus.Paused, persisted.Status);
                Assert.Null(persisted.AcquisitionId);
            } else {
                Assert.True(Assert.Single(due).IsUpgrade);
                Assert.Equal(acquisitionId, persisted.AcquisitionId);
                Assert.Equal(profileId, (await db.Acquisitions.AsNoTracking().SingleAsync()).ProfileId);
            }
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class BeforeBaselineLease(IEntityLifecycleMutationLease inner, Func<Task> before) : IEntityLifecycleMutationLease {
        public async Task<bool> ExecuteAsync(Guid entityId, Func<CancellationToken, Task> mutation, CancellationToken cancellationToken) {
            await before();
            return await inner.ExecuteAsync(entityId, mutation, cancellationToken);
        }
    }

    [Theory]
    [InlineData(EntityKind.Movie, false, false)]
    [InlineData(EntityKind.Movie, true, false)]
    [InlineData(EntityKind.VideoEpisode, false, false)]
    [InlineData(EntityKind.Movie, false, true)]
    public async Task AnEntityOnlyMonitorRestoresItsVerifiedImportBaselineBeforeEvaluatingUpgrades(EntityKind kind, bool folderReceipt, bool explicitDefaultReset) {
        var root = Directory.CreateTempSubdirectory("prismedia-baseline-restore-").FullName;
        try {
            await using var db = CreateContext();
            var store = await SeedMediaUpgradeMonitorAsync(db, kind,
                VideoQuality.Webdl720p.ToCode(), VideoQuality.Webdl1080p.ToCode(),
                attachEntity: true, subtitleStatusKnown: true, hasSubtitles: true);
            var baseline = await db.Acquisitions.SingleAsync();
            var monitor = await db.Monitors.SingleAsync();
            var path = Path.Combine(root, "owned.mkv");
            await File.WriteAllTextAsync(path, "owned video bytes");
            var sourceId = Guid.NewGuid();
            db.EntityFiles.Add(new EntityFileRow {
                Id = sourceId, EntityId = baseline.EntityId!.Value, Role = EntityFileRole.Source,
                Path = path, SizeBytes = new FileInfo(path).Length, Source = FileSourceKind.Scan.ToCode(),
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            });
            baseline.FinalSourcePath = folderReceipt ? root : path;
            monitor.AcquisitionId = null;
            monitor.ProfileId = (await db.BookAcquisitionProfiles.SingleAsync()).Id;
            if (explicitDefaultReset) {
                var oldProfileId = Guid.NewGuid();
                db.BookAcquisitionProfiles.Add(new BookAcquisitionProfileRow {
                    Id = oldProfileId, Kind = kind, DisplayName = "Keep current copy", UpgradeUntilCutoff = false,
                    CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
                });
                monitor.ProfileId = baseline.ProfileId = oldProfileId;
            }
            await db.SaveChangesAsync();
            if (explicitDefaultReset) {
                await store.StartForEntityAsync(baseline.EntityId!.Value, kind, baseline.Title,
                    new AcquisitionTargeting(null, null), null, default);
            }

            var due = Assert.Single(await store.ListImmediateForMonitorAsync(monitor.Id, default));

            Assert.True(due.IsUpgrade);
            Assert.Equal(baseline.Id, due.AcquisitionId);
            Assert.Equal(baseline.Id, (await db.Monitors.AsNoTracking().SingleAsync()).AcquisitionId);
            Assert.Equal(monitor.ProfileId, (await db.Acquisitions.AsNoTracking().SingleAsync()).ProfileId);
            Assert.Equal(sourceId, (await db.EntityFiles.AsNoTracking().SingleAsync()).Id);
            Assert.Equal("owned video bytes", await File.ReadAllTextAsync(path));
            Assert.Single(await db.Acquisitions.ToArrayAsync());
            Assert.True(Assert.Single(await store.ListImmediateForMonitorAsync(monitor.Id, default)).IsUpgrade);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("missing file")]
    [InlineData("different file")]
    [InlineData("shared file")]
    [InlineData("multiple sources")]
    [InlineData("competing receipt")]
    [InlineData("working upgrade")]
    [InlineData("changed bytes")]
    [InlineData("uncaptured quality")]
    public async Task BaselineRecoveryLeavesUnprovenOrBusyOwnershipAlone(string scenario) {
        var root = Directory.CreateTempSubdirectory("prismedia-baseline-guard-").FullName;
        try {
            await using var db = CreateContext();
            var store = await SeedMediaUpgradeMonitorAsync(db, EntityKind.Movie,
                VideoQuality.Webdl720p.ToCode(), VideoQuality.Webdl1080p.ToCode(),
                attachEntity: true, subtitleStatusKnown: true, hasSubtitles: true);
            var baseline = await db.Acquisitions.SingleAsync();
            var monitor = await db.Monitors.SingleAsync();
            var path = Path.Combine(root, "owned.mkv");
            await File.WriteAllTextAsync(path, "owned bytes");
            var source = new EntityFileRow {
                Id = Guid.NewGuid(), EntityId = baseline.EntityId!.Value, Role = EntityFileRole.Source,
                Path = path, SizeBytes = new FileInfo(path).Length, Source = FileSourceKind.Scan.ToCode(),
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            };
            db.EntityFiles.Add(source);
            baseline.FinalSourcePath = path;
            monitor.AcquisitionId = null;
            switch (scenario) {
                case "paused": monitor.Status = MonitorStatus.Paused; break;
                case "missing file": File.Delete(path); break;
                case "different file":
                    baseline.FinalSourcePath = Path.Combine(root, "different.mkv");
                    await File.WriteAllTextAsync(baseline.FinalSourcePath, "another movie");
                    break;
                case "shared file":
                case "multiple sources":
                    db.EntityFiles.Add(new EntityFileRow {
                        Id = Guid.NewGuid(), EntityId = scenario == "shared file" ? Guid.NewGuid() : baseline.EntityId.Value,
                        Role = EntityFileRole.Source, Path = path, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
                    });
                    break;
                case "competing receipt":
                case "working upgrade":
                    db.Acquisitions.Add(new AcquisitionRow {
                        Id = Guid.NewGuid(), EntityId = scenario == "competing receipt" ? baseline.EntityId : null,
                        Kind = EntityKind.Movie, Title = "Another receipt",
                        Status = scenario == "competing receipt" ? AcquisitionStatus.Imported : AcquisitionStatus.Downloading,
                        UpgradeOfAcquisitionId = scenario == "working upgrade" ? baseline.Id : null,
                        FinalSourcePath = path, UpgradeQualityCaptured = true,
                        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
                    });
                    break;
                case "changed bytes": source.SizeBytes++; break;
                case "uncaptured quality": baseline.UpgradeQualityCaptured = false; break;
            }
            await db.SaveChangesAsync();

            var due = await store.ListImmediateForMonitorAsync(monitor.Id, default);

            Assert.All(due, item => Assert.False(item.IsUpgrade));
            Assert.Null((await db.Monitors.AsNoTracking().SingleAsync()).AcquisitionId);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("Some Book Vol. 4 epub", ReleaseRejectionReason.WrongVolume)]
    [InlineData("Some Book Vol. 3 mp3", ReleaseRejectionReason.UnsupportedFormat)]
    public async Task BookUpgradesRetainVolumeAndRenditionReleaseGates(string title, ReleaseRejectionReason expected) {
        await using var db = CreateContext();
        var monitors = await SeedUpgradeMonitorAsync(db,
            owned: new(BookSourceTier.Web, BookFormatTier.Reflowable),
            cutoff: new(BookSourceTier.Retail, BookFormatTier.Reflowable), bookRendition: BookRendition.Ebook);
        var parent = await db.Acquisitions.SingleAsync();
        parent.VolumeNumber = 3;
        await db.SaveChangesAsync();
        var monitorId = (await monitors.ListAsync(default))[0].Id;
        var childId = await monitors.CreateUpgradeChildAsync(monitorId, default);
        var input = await AcquisitionTestFactory.Store(db).GetSearchInputAsync(childId!.Value, default);
        var rules = AcquisitionRuleContext.Apply(BookAcquisitionRules.Default, input!, null,
            ProperDownloadPolicy.PreferAndUpgrade, [DownloadProtocol.Torrent]);
        var release = new IndexerRelease(title, 1_000_000, 10, 2, DownloadProtocol.Torrent,
            "https://indexer.test/download", null, null, null, null, null);

        var candidate = Assert.Single(new BookReleaseDecisionEngine().Evaluate([(release, null, "Indexer")], rules));
        Assert.False(candidate.Accepted);
        Assert.Contains(expected, candidate.Rejections);
        Assert.True(Assert.Single(new BookReleaseDecisionEngine().Evaluate(
            [(release with { Title = "Some Book Vol. 3 epub" }, null, "Indexer")], rules)).Accepted);
    }

    [Fact]
    public async Task EditedProfileReopensAnUpgradeBeforeTheOldBarrenSearchCooldown() {
        await using var db = CreateContext();
        var store = await SeedMediaUpgradeMonitorAsync(db, EntityKind.Movie,
            VideoQuality.Bluray720p.ToCode(), VideoQuality.Bluray1080p.ToCode(),
            attachEntity: true, subtitleStatusKnown: true, hasSubtitles: true);
        var monitor = await db.Monitors.SingleAsync();
        monitor.LastSearchedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        monitor.BarrenSearches = 5;
        var profile = await db.BookAcquisitionProfiles.SingleAsync();
        profile.CreatedAt = DateTimeOffset.UtcNow.AddDays(-10);
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        Assert.Null(Assert.Single((await store.ListCutoffUnmetAsync(1, 20, EntityKind.Movie, CancellationToken.None)).Items).NextSearchAt);
        Assert.True(Assert.Single(await store.ListDueMonitorsAsync(360, CancellationToken.None)).IsUpgrade);
        Assert.Equal(0, monitor.BarrenSearches);

        await store.MarkSearchedAsync(monitor.Id, CancellationToken.None);
        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
    }

    [Fact]
    public async Task EditingAnotherProfileDoesNotResetAnAssignedProfilesCooldown() {
        await using var db = CreateContext();
        var store = await SeedMediaUpgradeMonitorAsync(db, EntityKind.Movie,
            VideoQuality.Bluray720p.ToCode(), VideoQuality.Bluray1080p.ToCode(),
            attachEntity: true, subtitleStatusKnown: true, hasSubtitles: true);
        var profile = await db.BookAcquisitionProfiles.SingleAsync();
        profile.CreatedAt = profile.UpdatedAt = DateTimeOffset.UtcNow.AddDays(-10);
        (await db.Acquisitions.SingleAsync()).ProfileId = profile.Id;
        var monitor = await db.Monitors.SingleAsync();
        monitor.LastSearchedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        monitor.BarrenSearches = 5;
        db.BookAcquisitionProfiles.Add(new BookAcquisitionProfileRow {
            Id = Guid.NewGuid(), Kind = EntityKind.Movie, DisplayName = "Other HD", AutoPick = true,
            UpgradeUntilCutoff = true, CutoffQuality = VideoQuality.Bluray2160p.ToCode(),
            TargetLibraryRootId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(5, monitor.BarrenSearches);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyAnExplicitDefaultChoiceClearsAnOwnedProfile(bool resetToDefault) {
        await using var db = CreateContext();
        var store = await SeedMediaUpgradeMonitorAsync(
            db, EntityKind.Movie, VideoQuality.Dvd.ToCode(), VideoQuality.Bluray1080p.ToCode(),
            attachEntity: true, subtitleStatusKnown: true, hasSubtitles: true);
        var acquisition = await db.Acquisitions.SingleAsync();
        var low = new BookAcquisitionProfileRow {
            Id = Guid.NewGuid(), Kind = EntityKind.Movie, DisplayName = "Keep DVD",
            TargetLibraryRootId = Guid.NewGuid(), UpgradeUntilCutoff = false,
            CutoffQuality = VideoQuality.Dvd.ToCode(),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        db.BookAcquisitionProfiles.Add(low);
        acquisition.ProfileId = low.Id;
        (await db.Monitors.SingleAsync()).ProfileId = low.Id;
        await db.SaveChangesAsync();
        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));

        await store.StartForEntityAsync(acquisition.EntityId!.Value, EntityKind.Movie, acquisition.Title,
            resetToDefault ? new AcquisitionTargeting(null, null) : null, null, CancellationToken.None);

        var due = await store.ListDueMonitorsAsync(360, CancellationToken.None);
        if (resetToDefault) {
            Assert.True(Assert.Single(due).IsUpgrade);
            Assert.Null(acquisition.ProfileId);
        } else {
            Assert.Empty(due);
            Assert.Equal(low.Id, acquisition.ProfileId);
        }
    }

    [Theory]
    [InlineData(EntityKind.Movie)]
    [InlineData(EntityKind.VideoEpisode)]
    public async Task SelectingAHigherProfileForAnOwnedEntityControlsItsNextUpgrade(EntityKind kind) {
        await using var db = CreateContext();
        var store = await SeedMediaUpgradeMonitorAsync(
            db, kind, VideoQuality.Dvd.ToCode(), VideoQuality.Dvd.ToCode(),
            upgradeOn: false, attachEntity: true, subtitleStatusKnown: true, hasSubtitles: true);
        var acquisition = await db.Acquisitions.SingleAsync();
        acquisition.ProfileId = (await db.BookAcquisitionProfiles.SingleAsync()).Id;
        var high = new BookAcquisitionProfileRow {
            Id = Guid.NewGuid(), Kind = AcquisitionProfileKinds.For(kind), DisplayName = "Upgrade to HD",
            TargetLibraryRootId = Guid.NewGuid(), AutoPick = true, UpgradeUntilCutoff = true,
            CutoffQuality = VideoQuality.Bluray1080p.ToCode(),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        db.BookAcquisitionProfiles.Add(high);
        await db.SaveChangesAsync();
        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));

        await store.StartForEntityAsync(acquisition.EntityId!.Value, kind, acquisition.Title,
            new AcquisitionTargeting(null, high.Id), null, CancellationToken.None);

        var due = Assert.Single(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.True(due.IsUpgrade);
        Assert.Equal(high.Id, due.ProfileId);
        Assert.Single((await store.ListCutoffUnmetAsync(1, 20, kind, CancellationToken.None)).Items);
        var childId = await store.CreateUpgradeChildAsync(due.MonitorId, CancellationToken.None);
        var child = await db.Acquisitions.SingleAsync(row => row.Id == childId);
        Assert.Equal(high.Id, child.ProfileId);
        child.Status = AcquisitionStatus.Downloading;
        await db.SaveChangesAsync();

        await store.StartForEntityAsync(acquisition.EntityId.Value, kind, acquisition.Title,
            new AcquisitionTargeting(null, null), null, CancellationToken.None);
        Assert.Equal(high.Id, child.ProfileId);
        Assert.Null(acquisition.ProfileId);
        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
    }

    [Theory]
    [InlineData(EntityKind.Movie, false)]
    [InlineData(EntityKind.Movie, true)]
    [InlineData(EntityKind.VideoEpisode, false)]
    [InlineData(EntityKind.VideoEpisode, true)]
    public async Task RaisingProfileCutoffAfterImportReopensUpgradeSearch(EntityKind kind, bool initiallyEnabled) {
        await using var db = CreateContext();
        var store = await SeedMediaUpgradeMonitorAsync(
            db, kind, VideoQuality.Dvd.ToCode(), VideoQuality.Dvd.ToCode(),
            upgradeOn: initiallyEnabled, attachEntity: true, subtitleStatusKnown: true, hasSubtitles: true);
        var acquisitionId = (await db.Acquisitions.SingleAsync()).Id;
        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));

        var profile = await db.BookAcquisitionProfiles.SingleAsync();
        profile.UpgradeUntilCutoff = true;
        profile.CutoffQuality = VideoQuality.Bluray1080p.ToCode();
        await db.SaveChangesAsync();

        var due = Assert.Single(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.True(due.IsUpgrade);
        Assert.Equal(acquisitionId, due.AcquisitionId);
        Assert.Single((await store.ListCutoffUnmetAsync(1, 20, kind, CancellationToken.None)).Items);

        (await db.Monitors.SingleAsync()).Status = MonitorStatus.Paused;
        await db.SaveChangesAsync();
        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
    }

    [Fact]
    public async Task RaisingEbookCutoffAfterImportReopensUpgradeSearch() {
        await using var db = CreateContext();
        var store = await SeedUpgradeMonitorAsync(db, BookQualityRank.Floor, BookQualityRank.Floor);
        var acquisition = await db.Acquisitions.SingleAsync();
        var entityId = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = entityId, KindCode = EntityKind.Book.ToCode(), Title = "An owned ebook",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        acquisition.EntityId = entityId;
        (await db.Monitors.SingleAsync()).EntityId = entityId;
        await db.SaveChangesAsync();
        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));

        var profile = await db.BookAcquisitionProfiles.SingleAsync();
        profile.CutoffSourceTier = BookSourceTier.Retail;
        profile.CutoffFormatTier = BookFormatTier.Reflowable;
        await db.SaveChangesAsync();

        var due = Assert.Single(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.True(due.IsUpgrade);
        Assert.Equal(acquisition.Id, due.AcquisitionId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnusableAssignedProfileFallsBackToTheKindDefault(bool wrongKind) {
        await using var db = CreateContext();
        var store = await SeedMediaUpgradeMonitorAsync(
            db, EntityKind.VideoEpisode, VideoQuality.Dvd.ToCode(), VideoQuality.Bluray1080p.ToCode());
        var assignedId = Guid.NewGuid();
        if (wrongKind) {
            db.BookAcquisitionProfiles.Add(new BookAcquisitionProfileRow {
                Id = assignedId, Kind = EntityKind.Movie, DisplayName = "Movie profile",
                TargetLibraryRootId = Guid.NewGuid(), UpgradeUntilCutoff = false,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        (await db.Acquisitions.SingleAsync()).ProfileId = assignedId;
        await db.SaveChangesAsync();

        var wanted = Assert.Single((await store.ListCutoffUnmetAsync(1, 20, EntityKind.VideoEpisode, CancellationToken.None)).Items);
        Assert.Equal(VideoQuality.Bluray1080p.ToCode(), wanted.CutoffQuality);
        Assert.True(Assert.Single(await store.ListDueMonitorsAsync(360, CancellationToken.None)).IsUpgrade);
    }

    [Theory]
    [InlineData(EntityKind.Movie)]
    [InlineData(EntityKind.VideoEpisode)]
    public async Task AssignedProfileControlsUpgradeSchedulingAndWantedCutoff(EntityKind kind) {
        await using var db = CreateContext();
        var store = await SeedMediaUpgradeMonitorAsync(
            db, kind, VideoQuality.Dvd.ToCode(), VideoQuality.Dvd.ToCode(), upgradeOn: false);
        var assigned = new BookAcquisitionProfileRow {
            Id = Guid.NewGuid(), Kind = AcquisitionProfileKinds.For(kind), DisplayName = "High quality",
            TargetLibraryRootId = Guid.NewGuid(), AutoPick = true, UpgradeUntilCutoff = true,
            CutoffQuality = VideoQuality.Bluray1080p.ToCode(),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        db.BookAcquisitionProfiles.Add(assigned);
        (await db.Acquisitions.SingleAsync()).ProfileId = assigned.Id;
        await db.SaveChangesAsync();

        var wanted = Assert.Single((await store.ListCutoffUnmetAsync(1, 20, kind, CancellationToken.None)).Items);
        Assert.Equal(assigned.CutoffQuality, wanted.CutoffQuality);
        var due = Assert.Single(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.True(due.IsUpgrade);
        Assert.Equal(assigned.Id, due.ProfileId);
    }

    [Fact]
    public async Task AssignedProfileCanDisableUpgradesEnabledByDefaultProfile() {
        await using var db = CreateContext();
        var store = await SeedMediaUpgradeMonitorAsync(
            db, EntityKind.Movie, VideoQuality.Dvd.ToCode(), VideoQuality.Bluray1080p.ToCode());
        var assigned = new BookAcquisitionProfileRow {
            Id = Guid.NewGuid(), Kind = EntityKind.Movie, DisplayName = "Keep current copy",
            TargetLibraryRootId = Guid.NewGuid(), AutoPick = true, UpgradeUntilCutoff = false,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        db.BookAcquisitionProfiles.Add(assigned);
        (await db.Acquisitions.SingleAsync()).ProfileId = assigned.Id;
        await db.SaveChangesAsync();

        Assert.Empty((await store.ListCutoffUnmetAsync(1, 20, EntityKind.Movie, CancellationToken.None)).Items);
        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Fulfilled, (await db.Monitors.SingleAsync()).Status);
    }

    [Fact]
    public async Task ImportedAudiobookFulfillsWithoutEnteringEbookUpgradeLoop() {
        await using var db = CreateContext();
        var store = await SeedUpgradeMonitorAsync(
            db,
            new BookQualityRank(BookSourceTier.Unknown, BookFormatTier.Unknown),
            new BookQualityRank(BookSourceTier.Retail, BookFormatTier.Reflowable),
            bookRendition: BookRendition.Audiobook);

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        var monitor = Assert.Single(await store.ListAsync(CancellationToken.None));
        Assert.Equal(MonitorStatus.Fulfilled, monitor.Status);
        Assert.Equal(BookRendition.Audiobook, monitor.BookRendition);
    }

    [Fact]
    public async Task ImportedBelowCutoffWithUpgradeOnIsDueAsUpgrade() {
        await using var db = CreateContext();
        var store = await SeedUpgradeMonitorAsync(db, owned: new(BookSourceTier.Web, BookFormatTier.Reflowable), cutoff: new(BookSourceTier.Retail, BookFormatTier.Reflowable));

        var due = await store.ListDueMonitorsAsync(360, CancellationToken.None);

        var monitor = Assert.Single(due);
        Assert.True(monitor.IsUpgrade);
    }

    [Fact]
    public async Task ImmediateEntityWorkIgnoresTheIntervalAndPreservesUpgradeSemantics() {
        await using var db = CreateContext();
        var store = await SeedUpgradeMonitorAsync(
            db,
            owned: new(BookSourceTier.Web, BookFormatTier.Reflowable),
            cutoff: new(BookSourceTier.Retail, BookFormatTier.Reflowable));
        var entityId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = entityId,
            KindCode = EntityKind.Book.ToCode(),
            Title = "Some Book",
            CreatedAt = now,
            UpdatedAt = now
        });
        var monitor = await db.Monitors.SingleAsync();
        monitor.EntityId = entityId;
        await db.SaveChangesAsync();
        await store.MarkSearchedAsync(monitor.Id, CancellationToken.None);
        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));

        var immediate = await store.ListImmediateForEntityAsync(entityId, CancellationToken.None);

        Assert.True(Assert.Single(immediate).IsUpgrade);
    }

    [Fact]
    public async Task DurableMonitorJobReResolvesItsExactUpgradeAfterPublicationStampedTheCadence() {
        await using var db = CreateContext();
        var store = await SeedUpgradeMonitorAsync(
            db,
            owned: new(BookSourceTier.Web, BookFormatTier.Reflowable),
            cutoff: new(BookSourceTier.Retail, BookFormatTier.Reflowable));
        var monitorId = Assert.Single(await store.ListAsync(CancellationToken.None)).Id;
        await store.MarkSearchedAsync(monitorId, CancellationToken.None);
        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));

        var exact = await store.ListImmediateForMonitorAsync(monitorId, CancellationToken.None);

        Assert.True(Assert.Single(exact).IsUpgrade);
    }

    [Fact]
    public async Task ImportedAtCutoffFulfills() {
        await using var db = CreateContext();
        var store = await SeedUpgradeMonitorAsync(db, owned: new(BookSourceTier.Retail, BookFormatTier.Reflowable), cutoff: new(BookSourceTier.Retail, BookFormatTier.Reflowable));

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Fulfilled, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task ImportedWithUpgradeOffFulfills() {
        await using var db = CreateContext();
        // Profile exists but upgrade is off → an imported book is simply fulfilled (the original behavior).
        var store = await SeedUpgradeMonitorAsync(db, owned: new(BookSourceTier.Web, BookFormatTier.Reflowable), cutoff: new(BookSourceTier.Retail, BookFormatTier.Reflowable), upgradeOn: false);

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Fulfilled, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task RepeatedUpgradeAttemptsRemainDueUntilTheConfiguredCutoffIsMet() {
        await using var db = CreateContext();
        var store = await SeedUpgradeMonitorAsync(db, owned: new(BookSourceTier.Web, BookFormatTier.Reflowable), cutoff: new(BookSourceTier.Retail, BookFormatTier.Reflowable), upgradeAttempts: 3);

        Assert.True(Assert.Single(await store.ListDueMonitorsAsync(360, CancellationToken.None)).IsUpgrade);
        Assert.Equal(MonitorStatus.Active, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task NotYetCapturedImportedStaysActiveAndNotDue() {
        await using var db = CreateContext();
        var store = await SeedUpgradeMonitorAsync(db, owned: new(BookSourceTier.Web, BookFormatTier.Reflowable), cutoff: new(BookSourceTier.Retail, BookFormatTier.Reflowable), captured: false);

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Active, (await store.ListAsync(CancellationToken.None))[0].Status); // waits for capture, does not fulfill
    }

    [Fact]
    public async Task InFlightUpgradeChildBlocksAnotherUpgradeSearch() {
        await using var db = CreateContext();
        var store = await SeedUpgradeMonitorAsync(db, owned: new(BookSourceTier.Web, BookFormatTier.Reflowable), cutoff: new(BookSourceTier.Retail, BookFormatTier.Reflowable));
        var childId = await store.CreateUpgradeChildAsync((await store.ListAsync(CancellationToken.None))[0].Id, CancellationToken.None);
        // The child is mid-download (in flight).
        var child = await db.Acquisitions.FirstAsync(a => a.Id == childId);
        child.Status = AcquisitionStatus.Downloading;
        await db.SaveChangesAsync();

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None)); // interlock holds — no second upgrade
    }

    [Fact]
    public async Task SettledUpgradeChildReleasesTheInterlockAndCountsBarren() {
        await using var db = CreateContext();
        var store = await SeedUpgradeMonitorAsync(db, owned: new(BookSourceTier.Web, BookFormatTier.Reflowable), cutoff: new(BookSourceTier.Retail, BookFormatTier.Reflowable));
        var monitorId = (await store.ListAsync(CancellationToken.None))[0].Id;
        var childId = await store.CreateUpgradeChildAsync(monitorId, CancellationToken.None);
        // The child's search found nothing better and failed without ever reaching the replace handler.
        (await db.Acquisitions.FirstAsync(a => a.Id == childId)).Status = AcquisitionStatus.Failed;
        await db.SaveChangesAsync();

        // The sweep reconciles: clears the interlock and counts a barren search. Not due this pass (cooldown).
        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        var monitor = await db.Monitors.AsNoTracking().FirstAsync(m => m.Id == monitorId);
        Assert.Null(monitor.UpgradeChildAcquisitionId);
        Assert.Equal(1, monitor.BarrenSearches);
    }

    [Theory]
    [InlineData(AcquisitionStatus.Downloaded)]
    [InlineData(AcquisitionStatus.Importing)]
    public async Task CompletedTransferKeepsItsUpgradeInterlockUntilReplacementResolves(AcquisitionStatus status) {
        await using var db = CreateContext();
        var store = await SeedUpgradeMonitorAsync(db, owned: new(BookSourceTier.Web, BookFormatTier.Reflowable), cutoff: new(BookSourceTier.Retail, BookFormatTier.Reflowable));
        var monitorId = (await store.ListAsync(CancellationToken.None))[0].Id;
        var childId = await store.CreateUpgradeChildAsync(monitorId, CancellationToken.None);
        (await db.Acquisitions.FirstAsync(a => a.Id == childId)).Status = status;
        await db.SaveChangesAsync();

        // Completion-ticket recovery and durable job retries own unfinished replacements. A sweep
        // must not permit another grab while those already-downloaded bytes can still be applied.
        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Null(await store.CreateUpgradeChildAsync(monitorId, CancellationToken.None));
        var monitor = await db.Monitors.AsNoTracking().SingleAsync(m => m.Id == monitorId);
        Assert.Equal(childId, monitor.UpgradeChildAcquisitionId);
        Assert.Equal(0, monitor.BarrenSearches);
    }

    [Fact]
    public async Task CreateUpgradeChildCopiesParentAndIsClaimedOnce() {
        await using var db = CreateContext();
        var store = await SeedUpgradeMonitorAsync(db, owned: new(BookSourceTier.Web, BookFormatTier.Reflowable), cutoff: new(BookSourceTier.Retail, BookFormatTier.Reflowable));
        var monitorId = (await store.ListAsync(CancellationToken.None))[0].Id;

        var childId = await store.CreateUpgradeChildAsync(monitorId, CancellationToken.None);
        Assert.NotNull(childId);
        var child = await db.Acquisitions.AsNoTracking().FirstAsync(a => a.Id == childId);
        Assert.Equal("Some Book", child.Title);
        Assert.Equal(AcquisitionStatus.Pending, child.Status);
        Assert.NotNull(child.UpgradeOfAcquisitionId);

        // A second claim while the first is in flight returns null (one upgrade at a time).
        Assert.Null(await store.CreateUpgradeChildAsync(monitorId, CancellationToken.None));
    }

    [Fact]
    public async Task ResolveUpgradeChildSuccessCountsAttemptAndResetsBarren() {
        await using var db = CreateContext();
        var store = await SeedUpgradeMonitorAsync(db, owned: new(BookSourceTier.Web, BookFormatTier.Reflowable), cutoff: new(BookSourceTier.Retail, BookFormatTier.Reflowable), barrenSearches: 2);
        var monitorId = (await store.ListAsync(CancellationToken.None))[0].Id;
        var childId = await store.CreateUpgradeChildAsync(monitorId, CancellationToken.None);

        await store.ResolveUpgradeChildAsync(childId!.Value, succeeded: true, CancellationToken.None);

        var monitor = await db.Monitors.AsNoTracking().FirstAsync(m => m.Id == monitorId);
        Assert.Null(monitor.UpgradeChildAcquisitionId);
        Assert.Equal(1, monitor.UpgradeAttempts);
        Assert.Equal(0, monitor.BarrenSearches); // a success resets the fruitless streak
    }

    [Fact]
    public async Task ImportedMovieBelowCutoffWithUpgradeOnIsDueAsUpgrade() {
        await using var db = CreateContext();
        var store = await SeedMediaUpgradeMonitorAsync(db, EntityKind.Movie, owned: "webdl-720p", cutoff: "bluray-1080p");

        var due = await store.ListDueMonitorsAsync(360, CancellationToken.None);

        var monitor = Assert.Single(due);
        Assert.True(monitor.IsUpgrade);
    }

    [Fact]
    public async Task ImportedMovieAtOrAboveCutoffFulfills() {
        await using var db = CreateContext();
        // Owned exactly at cutoff → fulfilled.
        var atCutoff = await SeedMediaUpgradeMonitorAsync(db, EntityKind.Movie, owned: "bluray-1080p", cutoff: "bluray-1080p");
        Assert.Empty(await atCutoff.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Fulfilled, (await atCutoff.ListAsync(CancellationToken.None))[0].Status);

        // Owned above cutoff → also fulfilled.
        await using var db2 = CreateContext();
        var aboveCutoff = await SeedMediaUpgradeMonitorAsync(db2, EntityKind.Movie, owned: "remux-2160p", cutoff: "bluray-1080p");
        Assert.Empty(await aboveCutoff.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Fulfilled, (await aboveCutoff.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task EntityBackedMovieAtQualityCutoffWithoutSubtitlesStaysDueForSubtitleUpgrade() {
        await using var db = CreateContext();
        var store = await SeedMediaUpgradeMonitorAsync(
            db,
            EntityKind.Movie,
            owned: "bluray-1080p",
            cutoff: "bluray-1080p",
            attachEntity: true,
            subtitleStatusKnown: true,
            hasSubtitles: false);

        Assert.True(Assert.Single(await store.ListDueMonitorsAsync(360, CancellationToken.None)).IsUpgrade);
    }

    [Fact]
    public async Task EntityBackedMovieAtQualityCutoffRetainsBaselineWithoutSearching() {
        await using var db = CreateContext();
        var store = await SeedMediaUpgradeMonitorAsync(
            db,
            EntityKind.Movie,
            owned: "bluray-1080p",
            cutoff: "bluray-1080p",
            attachEntity: true,
            subtitleStatusKnown: true,
            hasSubtitles: true);

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        var monitor = Assert.Single(await store.ListAsync(CancellationToken.None));
        Assert.Equal(MonitorStatus.Active, monitor.Status);
        Assert.Equal((await db.Acquisitions.SingleAsync()).Id, monitor.AcquisitionId);
    }

    [Fact]
    public async Task EntityBackedMovieWaitsForSubtitleExtractionBeforeJudgingCutoff() {
        await using var db = CreateContext();
        var store = await SeedMediaUpgradeMonitorAsync(
            db,
            EntityKind.Movie,
            owned: "bluray-1080p",
            cutoff: "bluray-1080p",
            attachEntity: true,
            subtitleStatusKnown: false);

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Active, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task ImportedMovieWithUpgradeOffFulfills() {
        await using var db = CreateContext();
        var store = await SeedMediaUpgradeMonitorAsync(db, EntityKind.Movie, owned: "webdl-720p", cutoff: "bluray-1080p", upgradeOn: false);

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Fulfilled, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task ImportedSeasonPackAlwaysFulfillsEvenBelowCutoff() {
        await using var db = CreateContext();
        // A season pack is multi-file — it captures owned quality but never upgrades; it fulfills on import.
        var store = await SeedMediaUpgradeMonitorAsync(db, EntityKind.VideoSeason, owned: "webdl-720p", cutoff: "bluray-1080p");

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Fulfilled, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task ImportedAlbumAlwaysFulfillsEvenBelowCutoff() {
        await using var db = CreateContext();
        var store = await SeedMediaUpgradeMonitorAsync(db, EntityKind.AudioLibrary, owned: "lossy", cutoff: "lossless");

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Fulfilled, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task ImportedMovieAtLadderCutoffButBelowFormatScoreCutoffStaysDue() {
        await using var db = CreateContext();
        // Ladder cutoff is met (owned == cutoff quality) but the format-score cutoff (500) is not — the
        // monitor keeps chasing a better-scoring release at the same quality instead of fulfilling.
        var store = await SeedMediaUpgradeMonitorAsync(
            db, EntityKind.Movie, owned: "bluray-1080p", cutoff: "bluray-1080p", cutoffFormatScore: 500, ownedFormatScore: 0);

        var due = await store.ListDueMonitorsAsync(360, CancellationToken.None);

        var monitor = Assert.Single(due);
        Assert.True(monitor.IsUpgrade);
    }

    [Fact]
    public async Task ImportedMovieAtBothCutoffsFulfills() {
        await using var db = CreateContext();
        // Both the ladder cutoff AND the format-score cutoff are met → fulfilled.
        var store = await SeedMediaUpgradeMonitorAsync(
            db, EntityKind.Movie, owned: "bluray-1080p", cutoff: "bluray-1080p", cutoffFormatScore: 500, ownedFormatScore: 500);

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Fulfilled, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task ImportedMovieWithNoFormatCutoffFulfillsAtLadderCutoff() {
        await using var db = CreateContext();
        // No format-score cutoff configured (null) → the format score imposes no requirement; the ladder
        // cutoff alone decides, so an owned copy at the ladder cutoff fulfills even with a 0 format score.
        var store = await SeedMediaUpgradeMonitorAsync(
            db, EntityKind.Movie, owned: "bluray-1080p", cutoff: "bluray-1080p", cutoffFormatScore: null, ownedFormatScore: 0);

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Fulfilled, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task ImportedMovieWithNoOwnedQualityCapturedStaysActive() {
        await using var db = CreateContext();
        // The captured flag is set but the ladder code was never recorded (no selected release) → the loop
        // can't judge the owned copy yet, so it waits (Active) rather than fulfilling too early.
        var store = await SeedMediaUpgradeMonitorAsync(db, EntityKind.Movie, owned: null, cutoff: "bluray-1080p");

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Active, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    /// <summary>
    /// Seeds an imported media (movie/TV/music) monitor: a governing profile for the kind's profile family
    /// with the ladder cutoff-quality code, and an imported acquisition carrying the owned ladder code.
    /// </summary>
    private static async Task<EfMonitorStore> SeedMediaUpgradeMonitorAsync(
        PrismediaDbContext db,
        EntityKind kind,
        string? owned,
        string cutoff,
        bool upgradeOn = true,
        int? cutoffFormatScore = null,
        int ownedFormatScore = 0,
        bool attachEntity = false,
        bool subtitleStatusKnown = false,
        bool hasSubtitles = false) {
        var now = DateTimeOffset.UtcNow;
        db.BookAcquisitionProfiles.Add(new BookAcquisitionProfileRow {
            Id = Guid.NewGuid(), Kind = AcquisitionProfileKinds.For(kind), DisplayName = "Default", IsDefault = true,
            TargetLibraryRootId = Guid.NewGuid(), AutoPick = true, UpgradeUntilCutoff = upgradeOn, CutoffQuality = cutoff,
            CutoffFormatScore = cutoffFormatScore, CreatedAt = now, UpdatedAt = now
        });
        var acquisitionId = Guid.NewGuid();
        Guid? entityId = null;
        if (attachEntity) {
            entityId = Guid.NewGuid();
            db.Entities.Add(new EntityRow {
                Id = entityId.Value,
                KindCode = kind.ToCode(),
                Title = "Some Media",
                CreatedAt = now,
                UpdatedAt = now
            });
            if (subtitleStatusKnown) {
                db.EntitySubtitleStates.Add(new EntitySubtitleStateRow {
                    EntityId = entityId.Value,
                    SubtitlesExtractedAt = now
                });
            }
            if (hasSubtitles) {
                db.EntitySubtitles.Add(new EntitySubtitleRow {
                    Id = Guid.NewGuid(),
                    EntityId = entityId.Value,
                    Language = "eng",
                    Format = "vtt",
                    Source = EntitySubtitleSource.Embedded,
                    SourceKey = "stream:2",
                    StoragePath = "/data/subtitles/movie/2.vtt",
                    SourceFormat = "subrip",
                    CreatedAt = now
                });
            }
        }
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId, EntityId = entityId, Kind = kind, Status = AcquisitionStatus.Imported, Title = "Some Media", ExternalIdsJson = "{}", SourceUrlsJson = "[]",
            OwnedMediaQuality = owned, OwnedFormatScore = ownedFormatScore, UpgradeQualityCaptured = true, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
        var store = new EfMonitorStore(db);
        await store.StartAsync(acquisitionId, kind, "Some Media", null, CancellationToken.None);
        return store;
    }

    private static async Task<EfMonitorStore> SeedUpgradeMonitorAsync(
        PrismediaDbContext db,
        BookQualityRank owned,
        BookQualityRank cutoff,
        bool upgradeOn = true,
        bool captured = true,
        int upgradeAttempts = 0,
        int barrenSearches = 0,
        BookRendition? bookRendition = null) {
        var now = DateTimeOffset.UtcNow;
        db.BookAcquisitionProfiles.Add(new BookAcquisitionProfileRow {
            Id = Guid.NewGuid(), DisplayName = "Default", IsDefault = true, TargetLibraryRootId = Guid.NewGuid(),
            AutoPick = true, UpgradeUntilCutoff = upgradeOn, CutoffSourceTier = cutoff.Source, CutoffFormatTier = cutoff.Format,
            CreatedAt = now, UpdatedAt = now
        });
        var acquisitionId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId, Status = AcquisitionStatus.Imported, Title = "Some Book", ExternalIdsJson = "{}", SourceUrlsJson = "[]",
            BookRendition = bookRendition,
            OwnedSourceTier = owned.Source, OwnedFormatTier = owned.Format, UpgradeQualityCaptured = captured, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
        var store = new EfMonitorStore(db);
        await store.StartAsync(acquisitionId, EntityKind.Book, "Some Book", "Author", CancellationToken.None);
        var monitor = await db.Monitors.FirstAsync(m => m.AcquisitionId == acquisitionId);
        monitor.UpgradeAttempts = upgradeAttempts;
        monitor.BarrenSearches = barrenSearches;
        await db.SaveChangesAsync();
        return store;
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
