using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Plugins;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Tests;

public sealed class EntityAlternativeTitleTests {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchAndImportStopUsingTitlesWhenTheNativeBindingChanges(bool postgres) {
        await using var database = postgres ? await PostgresTestDatabase.CreateAsync() : null;
        await using var db = database?.CreateContext() ?? CreateContext();
        var id = await SeedAsync(db, EntityKind.Movie);
        await new EntityMetadataApplyService(db, new(Path.GetTempPath())).ApplyAsync(id,
            Proposal(EntityKind.Movie, ["Translated Title"]), [MetadataPatchField.Title.ToCode()], null, default);
        var store = AcquisitionTestFactory.Store(db);
        var acquisition = await store.CreateAsync(new AcquisitionMetadata("Formal Name", null, null, null, null, null,
            EntityKind.Movie, EntityId: id), default);
        Assert.Contains("Translated Title", (await store.GetSearchInputAsync(acquisition.Id, default))!.AlternativeWorkTitles);
        Assert.Contains("Translated Title", (await store.GetImportContextAsync(acquisition.Id, default))!.AlternativeWorkTitles);

        var identity = await db.EntityProviderIdentities.SingleAsync();
        identity.IdentityValue = "changed-work";
        await db.SaveChangesAsync();
        Assert.Empty((await store.GetSearchInputAsync(acquisition.Id, default))!.AlternativeWorkTitles);
        Assert.Empty((await store.GetImportContextAsync(acquisition.Id, default))!.AlternativeWorkTitles);
        identity.IdentityValue = "work-id";
        db.EntityExternalIds.RemoveRange(await db.EntityExternalIds.ToArrayAsync());
        await db.SaveChangesAsync();
        Assert.Empty((await store.GetSearchInputAsync(acquisition.Id, default))!.AlternativeWorkTitles);
    }

    [Fact]
    public async Task AcceptedWorkTitlesRefreshWithoutRetainingRetiredAlternatives() {
        await using var db = CreateContext();
        var id = await SeedAsync(db, EntityKind.VideoSeries);
        var service = new EntityMetadataApplyService(db, new(Path.GetTempPath()));
        var proposal = Proposal(EntityKind.VideoSeries, ["Formal Name", "Formal.Name", "Romanized Name"]);
        await service.ApplyAsync(id, proposal, [MetadataPatchField.Title.ToCode()], null, default);
        Assert.Equal(2, await db.EntityAlternativeTitles.CountAsync());
        Assert.All(await db.EntityAlternativeTitles.ToArrayAsync(), row => Assert.Equal("work-id", row.IdentityValue));

        await service.ApplyAsync(id, proposal with { Patch = proposal.Patch with { AlternativeTitles = null } },
            [MetadataPatchField.Title.ToCode()], null, default);
        Assert.Equal(2, await db.EntityAlternativeTitles.CountAsync());
        await service.ApplyAsync(id, proposal with { Patch = proposal.Patch with { AlternativeTitles = [] } },
            [MetadataPatchField.Title.ToCode()], null, default);
        Assert.Equal("Formal Name", (await db.EntityAlternativeTitles.SingleAsync()).Title);
    }

    [Theory]
    [InlineData(EntityKind.VideoSeason, "provider", "work-id", true)]
    [InlineData(EntityKind.VideoSeries, "another-provider", "work-id", true)]
    [InlineData(EntityKind.VideoSeries, "provider", "another-work", true)]
    [InlineData(EntityKind.VideoSeries, "provider", "work-id", false)]
    public async Task UnselectedOrUnqualifiedTitlesCannotBecomeWorkAliases(EntityKind kind, string provider, string identity, bool selected) {
        await using var db = CreateContext();
        var id = await SeedAsync(db, kind);
        var proposal = Proposal(kind, ["Alternate Name"]) with { Provider = provider };
        proposal = proposal with { Patch = proposal.Patch with { ExternalIds = new Dictionary<string, string> { ["provider"] = identity } } };
        await new EntityMetadataApplyService(db, new(Path.GetTempPath())).ApplyAsync(id, proposal,
            selected ? [MetadataPatchField.Title.ToCode()] : [], null, default);
        Assert.Empty(await db.EntityAlternativeTitles.ToArrayAsync());
    }

    private static async Task<Guid> SeedAsync(PrismediaDbContext db, EntityKind kind) {
        var id = Guid.NewGuid();
        db.Entities.Add(new EntityRow { Id = id, KindCode = kind.ToCode(), Title = "Formal Name" });
        db.EntityProviderIdentities.Add(new EntityProviderIdentityRow {
            EntityId = id, PluginId = "provider", IdentityNamespace = "provider", IdentityValue = "work-id"
        });
        db.EntityExternalIds.Add(new EntityExternalIdRow {
            Id = Guid.NewGuid(), EntityId = id, Provider = "provider", Value = "work-id"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static EntityMetadataProposal Proposal(EntityKind kind, IReadOnlyList<string> titles) =>
        new("provider:work-id", "provider", kind, 1, "external-id",
            new EntityMetadataPatch("Formal Name", null, new Dictionary<string, string> { ["provider"] = "work-id" },
                [], [], null, [], new Dictionary<string, string>(), new Dictionary<string, int>(), new Dictionary<string, int>(), null) {
                AlternativeTitles = titles
            }, [], [], []);

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
