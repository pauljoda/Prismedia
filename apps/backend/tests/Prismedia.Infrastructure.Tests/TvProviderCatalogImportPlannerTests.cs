using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class TvProviderCatalogImportPlannerTests {
    [Fact]
    public async Task ProviderTitlesRetainAMislabeledPairWhenItsActualSeasonDoesNotExistLocally() {
        await using var db = CreateContext();
        var import = await SeedAsync(db);
        var provider = new EvidenceSource();
        var planner = new TvAcquisitionImportPlanner(new EfImportTargetIndex(db), new EfMonitorStore(db), provider);
        var payload = new DownloadPayload("/downloads", [new("Show.S02E01.mkv", 100),
            new("Show.S02E49-E50.Hidden.Garden.&.Mountain.Journey.mkv", 100)]);

        var result = await planner.PlanAsync(import, payload, null, null, default);

        Assert.False(result.Plan.Blocked);
        Assert.Equal("Show.S02E01.mkv", Assert.Single(result.Plan.Units).SourceRelativePath);
        Assert.Equal(1, provider.Calls);
        var foreign = Assert.Single(result.Catalog, season => season.SeasonNumber == 3);
        Assert.Null(foreign.SeasonEntityId);
        Assert.NotNull(foreign.ProviderIdentity);
        Assert.Empty(result.MonitoredExtras);
        Assert.Equal(1, await db.Entities.CountAsync(row => row.KindCode == EntityKind.VideoSeason.ToCode()));
    }

    [Fact]
    public async Task ConfidentLocalEpisodeMappingDoesNotReadAProviderCatalog() {
        await using var db = CreateContext();
        var import = await SeedAsync(db);
        var provider = new EvidenceSource();
        var planner = new TvAcquisitionImportPlanner(new EfImportTargetIndex(db), new EfMonitorStore(db), provider);

        var result = await planner.PlanAsync(import, new("/downloads", [new("Show.S02E01.mkv", 100)]), null, null, default);

        Assert.Equal(1, Assert.Single(result.Plan.Units).Episode);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task AConfidentUnmonitoredExtraDoesNotRepeatProviderLookups() {
        await using var db = CreateContext();
        var import = await SeedAsync(db);
        var requestedSeason = await db.Entities.SingleAsync(entity => entity.Id == import.EntityId);
        var foreignSeason = new EntityRow { Id = Guid.NewGuid(), ParentEntityId = requestedSeason.ParentEntityId,
            KindCode = EntityKind.VideoSeason.ToCode(), Title = "Season 3", SortOrder = 3 };
        db.Entities.AddRange(foreignSeason, new EntityRow { Id = Guid.NewGuid(), ParentEntityId = foreignSeason.Id,
            KindCode = EntityKind.VideoEpisode.ToCode(), Title = "Hidden Garden", SortOrder = 56, IsWanted = true });
        await db.SaveChangesAsync();
        var provider = new EvidenceSource();
        var planner = new TvAcquisitionImportPlanner(new EfImportTargetIndex(db), new EfMonitorStore(db), provider);

        var result = await planner.PlanAsync(import, new("/downloads", [new("Show.S03E56.Hidden.Garden.mkv", 100)]), null, null, default);

        Assert.True(result.Plan.Blocked);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task ConfidentLocalTitleAlignmentKeepsCurrentPositionsWithoutProviderRenumbering() {
        await using var db = CreateContext();
        var import = await SeedAsync(db);
        db.Entities.AddRange(
            new EntityRow { Id = Guid.NewGuid(), ParentEntityId = import.EntityId, KindCode = EntityKind.VideoEpisode.ToCode(),
                Title = "Hidden Garden", SortOrder = 300, IsWanted = true },
            new EntityRow { Id = Guid.NewGuid(), ParentEntityId = import.EntityId, KindCode = EntityKind.VideoEpisode.ToCode(),
                Title = "Mountain Journey", SortOrder = 301, IsWanted = true });
        await db.SaveChangesAsync();
        var provider = new EvidenceSource();
        var planner = new TvAcquisitionImportPlanner(new EfImportTargetIndex(db), new EfMonitorStore(db), provider);

        var result = await planner.PlanAsync(import,
            new("/downloads", [new("Show.S02E49-E50.Hidden.Garden.&.Mountain.Journey.mkv", 100)]), null, null, default);

        var unit = Assert.Single(result.Plan.Units);
        Assert.Equal(300, unit.Episode);
        Assert.Equal([301], unit.ExtraEpisodes);
        Assert.Equal(0, provider.Calls);
    }

    private static async Task<AcquisitionImportContext> SeedAsync(PrismediaDbContext db) {
        var seriesId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        db.Entities.AddRange(
            new EntityRow { Id = seriesId, KindCode = EntityKind.VideoSeries.ToCode(), Title = "Show" },
            new EntityRow { Id = seasonId, ParentEntityId = seriesId, KindCode = EntityKind.VideoSeason.ToCode(), Title = "Season 2", SortOrder = 2 },
            new EntityRow { Id = Guid.NewGuid(), ParentEntityId = seasonId, KindCode = EntityKind.VideoEpisode.ToCode(), Title = "Known Story", SortOrder = 1, IsWanted = true },
            new EntityRow { Id = Guid.NewGuid(), ParentEntityId = seasonId, KindCode = EntityKind.VideoEpisode.ToCode(), Title = "Show - S02E49", SortOrder = 49, IsWanted = true });
        await db.SaveChangesAsync();
        return new(Guid.NewGuid(), "Season 2", null, "Show", null, null, null, null, "/downloads", null, null,
            EntityKind.VideoSeason, SeasonNumber: 2, EntityId: seasonId);
    }

    private static PrismediaDbContext CreateContext() => new(new DbContextOptionsBuilder<PrismediaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class EvidenceSource : ITvEpisodeCatalogEvidenceSource {
        public int Calls { get; private set; }
        public Task<IReadOnlyList<TvSeasonEpisodeCatalog>> ReadAsync(Guid linkedEntityId, int requestedSeason,
            IReadOnlyList<ImportCandidateFile> files, CancellationToken cancellationToken) {
            Calls++;
            return Task.FromResult<IReadOnlyList<TvSeasonEpisodeCatalog>>([
                new(null, 3, [Episode(56, "Hidden Garden"), Episode(57, "Mountain Journey")]) {
                    ProviderIdentity = new ExternalIdentity("test-season", "season-three")
                }
            ]);
        }

        private static TvEpisodeTitle Episode(int number, string title) => new(number, title) {
            ProviderIdentity = new ExternalIdentity("test-episode", number.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
    }
}
