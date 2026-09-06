using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class TvPayloadAdmissionTests {
    [Fact]
    public async Task PairedTitleOnlyFilesAreReconsideredWhenAnOwnedHalfGoesMissing() {
        await using var db = CreateContext();
        using var fixture = await TvPayloadAdmissionFixture.CreateAsync(db);
        ImportCandidateFile[] files = [new("Show - First Story & Second Story.mkv", 1000)];
        Assert.True(await fixture.Service.HasNoBenefitAsync(fixture.Input, files, default));
        await fixture.Service.RememberAsync(fixture.Input.Id, fixture.Selected.Identity, files, default);
        Assert.Contains(fixture.Selected.Identity, await fixture.Service.GetExcludedAsync(fixture.Input, default));
        db.EntityFiles.Remove(await db.EntityFiles.SingleAsync(file => file.EntityId == fixture.SecondEpisode.Id));
        await db.SaveChangesAsync();
        Assert.Empty(await fixture.Service.GetExcludedAsync(fixture.Input, default));
    }

    [Fact]
    public async Task FormalParentNumbersDoNotHideAlreadyOwnedAbsoluteCoverage() {
        await using var db = CreateContext();
        using var fixture = await TvPayloadAdmissionFixture.CreateAsync(db);
        var first = await db.Entities.SingleAsync(entity => entity.ParentEntityId == fixture.Input.EntityId && entity.SortOrder == 1);
        db.EntityPositions.AddRange(
            new EntityPositionRow { EntityId = first.Id, Code = EntityPositionCodes.AbsoluteEpisode, Value = 54 },
            new EntityPositionRow { EntityId = fixture.SecondEpisode.Id, Code = EntityPositionCodes.AbsoluteEpisode, Value = 104 });
        await db.SaveChangesAsync();
        var input = fixture.Input with { AlternativeWorkTitles = ["Room 104"] };
        ImportCandidateFile[] files = [new("Room.104 - 54.mkv", 1000)];
        Assert.False(await fixture.Service.HasNoBenefitAsync(fixture.Input, files, default));
        Assert.True(await fixture.Service.HasNoBenefitAsync(input, files, default));
        await fixture.Service.RememberAsync(input.Id, fixture.Selected.Identity, files, default);
        Assert.Contains(fixture.Selected.Identity, await fixture.Service.GetExcludedAsync(input, default));
    }

    [Fact]
    public async Task ForeignTitleEvidenceCannotBeRejectedAsAnAlreadyOwnedRequestedEpisode() {
        await using var db = CreateContext();
        using var fixture = await TvPayloadAdmissionFixture.CreateAsync(db);
        var requestedSeason = await db.Entities.SingleAsync(entity => entity.Id == fixture.Input.EntityId);
        var foreignSeason = new EntityRow { Id = Guid.NewGuid(), KindCode = EntityKind.VideoSeason.ToCode(),
            ParentEntityId = requestedSeason.ParentEntityId, SortOrder = 2, Title = "Season 2" };
        db.Entities.AddRange(foreignSeason, new EntityRow { Id = Guid.NewGuid(), KindCode = EntityKind.VideoEpisode.ToCode(),
            ParentEntityId = foreignSeason.Id, SortOrder = 3, Title = "Hidden Garden", IsWanted = true });
        await db.SaveChangesAsync();
        ImportCandidateFile[] files = [new("Show.S01E01.Hidden.Garden.mkv", 1000)];

        Assert.False(await fixture.Service.HasNoBenefitAsync(fixture.Input, files, default));
        await fixture.Service.RememberAsync(fixture.Input.Id, fixture.Selected.Identity, files, default);
        Assert.Empty(await fixture.Service.GetExcludedAsync(fixture.Input, default));
    }

    [Fact]
    public async Task APartialSeasonPackMustActuallyReachTheMissingEpisode() {
        await using var db = CreateContext();
        using var fixture = await TvPayloadAdmissionFixture.CreateAsync(db);
        db.Entities.Add(new EntityRow { Id = Guid.NewGuid(), KindCode = EntityKind.VideoEpisode.ToCode(),
            ParentEntityId = fixture.Input.EntityId, SortOrder = 3, Title = "Third Story", IsWanted = true });
        await db.SaveChangesAsync();

        Assert.True(await fixture.Service.HasNoBenefitAsync(fixture.Input, fixture.Files, CancellationToken.None));
        Assert.False(await fixture.Service.HasNoBenefitAsync(fixture.Input,
            [.. fixture.Files, new("Show.S01E03.Third.Story.mkv", 1000)], CancellationToken.None));
    }

    [Fact]
    public async Task EvidenceIsReevaluatedAfterMissingLinksFilesMappingsAndProfileChanges() {
        await using var db = CreateContext();
        using var fixture = await TvPayloadAdmissionFixture.CreateAsync(db);
        Assert.True(await fixture.Service.HasNoBenefitAsync(fixture.Input, fixture.Files, CancellationToken.None));
        await fixture.Service.RememberAsync(fixture.Input.Id, fixture.Selected.Identity, fixture.Files, CancellationToken.None);
        Assert.Contains(fixture.Selected.Identity, await fixture.Service.GetExcludedAsync(fixture.Input, CancellationToken.None));

        fixture.Profile.UpgradeUntilCutoff = true;
        await db.SaveChangesAsync();
        Assert.Empty(await fixture.Service.GetExcludedAsync(fixture.Input, CancellationToken.None));
        fixture.Profile.UpgradeUntilCutoff = false;
        db.EntityFiles.Remove(await db.EntityFiles.SingleAsync(file => file.EntityId == fixture.SecondEpisode.Id));
        await db.SaveChangesAsync();
        Assert.Empty(await fixture.Service.GetExcludedAsync(fixture.Input, CancellationToken.None));

        db.EntityFiles.Add(new EntityFileRow { Id = Guid.NewGuid(), EntityId = fixture.SecondEpisode.Id, Role = EntityFileRole.Source, Path = fixture.OwnedPath });
        fixture.SecondEpisode.SortOrder = null;
        await db.SaveChangesAsync();
        Assert.Empty(await fixture.Service.GetExcludedAsync(fixture.Input, CancellationToken.None));
        fixture.SecondEpisode.SortOrder = 2;
        await db.SaveChangesAsync();
        File.Delete(fixture.OwnedPath);
        Assert.Empty(await fixture.Service.GetExcludedAsync(fixture.Input, CancellationToken.None));
        Assert.Empty(await db.AcquisitionBlocklist.ToArrayAsync());
    }

    [Theory]
    [InlineData("unknown.mkv")]
    [InlineData("Show.S02E01.mkv")]
    [InlineData("Show.S01E03.mkv")]
    [InlineData("more.rar")]
    [InlineData("sample.zip")]
    [InlineData("obfuscated.bin")]
    public async Task UnexplainedPayloadMembersRemainEligible(string unknownFile) {
        await using var db = CreateContext();
        using var fixture = await TvPayloadAdmissionFixture.CreateAsync(db);
        Assert.False(await fixture.Service.HasNoBenefitAsync(fixture.Input,
            [.. fixture.Files, new(unknownFile, 500)], CancellationToken.None));
    }

    [Fact]
    public async Task SamplesAndSubtitlesDoNotMakeAnAlreadyOwnedPackUseful() {
        await using var db = CreateContext();
        using var fixture = await TvPayloadAdmissionFixture.CreateAsync(db);
        Assert.True(await fixture.Service.HasNoBenefitAsync(fixture.Input,
            [.. fixture.Files, new("Show.sample.mkv", 100), new("Show.srt", 50)], CancellationToken.None));
    }

    [Fact]
    public async Task ALongCandidateListDoesNotEvictEarlierEvidenceAndCreateARetryLoop() {
        await using var db = CreateContext();
        using var fixture = await TvPayloadAdmissionFixture.CreateAsync(db);
        for (var index = 0; index < 70; index++) {
            var selected = new SelectedRelease("Show S01 720p WEB", "Indexer", $"release-{index}");
            await AcquisitionTestFactory.Store(db).SetSelectedReleaseAsync(fixture.Input.Id, selected, CancellationToken.None);
            await fixture.Service.RememberAsync(fixture.Input.Id, selected.Identity, fixture.Files, CancellationToken.None);
        }
        Assert.Equal(70, (await fixture.Service.GetExcludedAsync(fixture.Input, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task ObservationsAreBoundedToTheirEntityAndExpireWithoutBecomingGlobalBlocks() {
        await using var db = CreateContext();
        using var fixture = await TvPayloadAdmissionFixture.CreateAsync(db);
        var store = new EfTvPayloadObservationStore(db);
        await store.RecordAsync(fixture.Input.Id, new(fixture.Selected.Identity, DateTimeOffset.UtcNow.AddDays(-8), fixture.Files), CancellationToken.None);
        Assert.Empty(await store.ListAsync(fixture.Input, CancellationToken.None));
        await fixture.Service.RememberAsync(fixture.Input.Id, fixture.Selected.Identity, fixture.Files, CancellationToken.None);
        Assert.Single(await store.ListAsync(fixture.Input with { Id = Guid.NewGuid() }, CancellationToken.None));
        Assert.Empty(await store.ListAsync(fixture.Input with { Id = Guid.NewGuid(), EntityId = Guid.NewGuid() }, CancellationToken.None));
    }

    private static PrismediaDbContext CreateContext() => new(new DbContextOptionsBuilder<PrismediaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}

internal sealed class TvPayloadAdmissionFixture : IDisposable {
    private readonly string root = Directory.CreateTempSubdirectory("prismedia-payload-benefit-").FullName;
    public required TvPayloadAdmission Service { get; init; }
    public required AcquisitionSearchInput Input { get; init; }
    public required SelectedRelease Selected { get; init; }
    public required BookAcquisitionProfileRow Profile { get; init; }
    public required EntityRow SecondEpisode { get; init; }
    public string OwnedPath => Path.Combine(root, "Show.S01E01E02.mkv");
    public IReadOnlyList<ImportCandidateFile> Files { get; } = [new("Show.S01E01E02.First.Story.Second.Story.mkv", 1000)];
    public void Dispose() => Directory.Delete(root, true);

    public static async Task<TvPayloadAdmissionFixture> CreateAsync(PrismediaDbContext db, AcquisitionRow? acquisition = null) {
        var series = new EntityRow { Id = Guid.NewGuid(), KindCode = EntityKind.VideoSeries.ToCode(), Title = "Show" };
        var season = new EntityRow { Id = Guid.NewGuid(), KindCode = EntityKind.VideoSeason.ToCode(), Title = "Season 1", ParentEntityId = series.Id, SortOrder = 1 };
        var first = new EntityRow { Id = Guid.NewGuid(), KindCode = EntityKind.VideoEpisode.ToCode(), Title = "First Story", ParentEntityId = season.Id, SortOrder = 1, IsWanted = true };
        var second = new EntityRow { Id = Guid.NewGuid(), KindCode = EntityKind.VideoEpisode.ToCode(), Title = "Second Story", ParentEntityId = season.Id, SortOrder = 2, IsWanted = true };
        var profile = new BookAcquisitionProfileRow { Id = Guid.NewGuid(), Kind = EntityKind.VideoSeries, AutoRedownload = true, IsDefault = true };
        if (acquisition is null) { acquisition = new AcquisitionRow { Id = Guid.NewGuid() }; db.Acquisitions.Add(acquisition); }
        acquisition.Kind = EntityKind.VideoSeason;
        acquisition.EntityId = season.Id;
        acquisition.Title = season.Title;
        acquisition.Series = series.Title;
        acquisition.SeasonNumber = 1;
        acquisition.ProfileId = profile.Id;
        acquisition.Status = AcquisitionStatus.Downloading;
        db.Entities.AddRange(series, season, first, second);
        db.BookAcquisitionProfiles.Add(profile);
        var fixture = new TvPayloadAdmissionFixture {
            Service = new(new EfTvPayloadObservationStore(db), new EfImportTargetIndex(db), new EfBookAcquisitionProfileStore(db)),
            Input = new(acquisition.Id, season.Title, null, EntityKind.VideoSeason, season.Id, ProfileId: profile.Id, Series: series.Title, SeasonNumber: 1),
            Selected = new("Show S01 720p WEB", "Indexer", "payload-hash"),
            Profile = profile, SecondEpisode = second
        };
        await File.WriteAllTextAsync(fixture.OwnedPath, "owned paired episode bytes");
        db.EntitySources.AddRange(new EntitySourceRow { EntityId = series.Id, Code = EntitySourceCode.Folder.ToCode(), Value = fixture.root },
            new EntitySourceRow { EntityId = season.Id, Code = EntitySourceCode.Folder.ToCode(), Value = fixture.root });
        foreach (var entity in new[] { first, second }) db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(), EntityId = entity.Id, Role = EntityFileRole.Source, Path = fixture.OwnedPath
        });
        db.Monitors.Add(new MonitorRow { Id = Guid.NewGuid(), EntityId = season.Id, AcquisitionId = acquisition.Id, Kind = EntityKind.VideoSeason, Status = MonitorStatus.Active });
        await db.SaveChangesAsync();
        await AcquisitionTestFactory.Store(db).SetSelectedReleaseAsync(acquisition.Id, fixture.Selected, CancellationToken.None);
        return fixture;
    }
}
