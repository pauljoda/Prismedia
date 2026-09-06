using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfAcquisitionStoreCheckpointCompatibilityTests {
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task CompatibleCheckpointsCanResumeOrClearWhenAnOptionalFieldWasAbsent(bool television, bool postgres, bool clear) {
        await using var database = postgres ? await PostgresTestDatabase.CreateAsync() : null;
        await using var db = database?.CreateContext() ?? MemoryContext();
        var fixture = await SeedAsync(db, television);
        var store = AcquisitionTestFactory.Store(db);
        var retryJob = Guid.NewGuid();

        var succeeded = clear
            ? television ? await store.TryClearTvImportCheckpointAsync(fixture.Id, fixture.Tv!, default)
                : await store.TryClearImportPlacementCheckpointAsync(fixture.Id, fixture.Placement!, default)
            : television ? await store.TryClaimTvImportCheckpointAsync(fixture.Id, fixture.Tv!, retryJob, default)
                : await store.TryClaimImportPlacementCheckpointAsync(fixture.Id, fixture.Placement!, retryJob, default);

        Assert.True(succeeded);
        var row = await db.Acquisitions.AsNoTracking().SingleAsync();
        if (clear) Assert.Null(row.ImportCheckpointJson);
        else {
            Assert.Equal(AcquisitionStatus.Importing, row.Status);
            Assert.Equal(retryJob, row.ImportClaimJobId);
            Assert.True(JsonNode.Parse(row.ImportCheckpointJson!)!.AsObject().ContainsKey(nameof(TvImportCheckpoint.ImportFileLedger)));
            // An older snapshot must not steal the now-active claim.
            Assert.False(television ? await store.TryClaimTvImportCheckpointAsync(fixture.Id, fixture.Tv!, Guid.NewGuid(), default)
                : await store.TryClaimImportPlacementCheckpointAsync(fixture.Id, fixture.Placement!, Guid.NewGuid(), default));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CompatibilityDoesNotAcceptAChangedSnapshotOrDiscardUnknownFields(bool television, bool unknownField) {
        await using var db = MemoryContext();
        var fixture = await SeedAsync(db, television);
        var row = await db.Acquisitions.SingleAsync();
        var json = JsonNode.Parse(row.ImportCheckpointJson!)!;
        if (unknownField) json["FutureRecoveryPolicy"] = true;
        else json[nameof(TvImportCheckpoint.SuccessMessage)] = "A different attempt snapshot";
        row.ImportCheckpointJson = json.ToJsonString();
        await db.SaveChangesAsync();
        var original = row.ImportCheckpointJson;
        var store = AcquisitionTestFactory.Store(db);

        Assert.False(television ? await store.TryClaimTvImportCheckpointAsync(fixture.Id, fixture.Tv!, Guid.NewGuid(), default)
            : await store.TryClaimImportPlacementCheckpointAsync(fixture.Id, fixture.Placement!, Guid.NewGuid(), default));
        Assert.Equal(original, (await db.Acquisitions.AsNoTracking().SingleAsync()).ImportCheckpointJson);
    }

    private static async Task<Fixture> SeedAsync(PrismediaDbContext db, bool television) {
        var id = Guid.NewGuid(); var attempt = Guid.NewGuid(); var claim = Guid.NewGuid();
        TvImportCheckpoint? tv = television ? new(Guid.NewGuid(), "/library/Show", ImportMode.Move, false, "Imported", false,
            [new("Show.S01E01.mkv", "/library/Show/Season 01/Show.S01E01.mkv", 1, 1, [],
                SourceAbsolutePath: "/downloads/Show.S01E01.mkv")], AttemptId: attempt, ClaimJobId: claim) : null;
        ImportPlacementCheckpoint? placement = television ? null : new(EntityKind.Movie, Guid.NewGuid(), "/library", "/downloads",
            ImportMode.Move, "/library/Movie", "/library/Movie/movie.mkv", "Imported",
            [new("movie.mkv", "/downloads/movie.mkv", "/library/Movie/movie.mkv", true)], AttemptId: attempt, ClaimJobId: claim);
        var json = JsonNode.Parse(television ? TvImportCheckpointJson.Serialize(tv!) : ImportPlacementCheckpointJson.Serialize(placement!))!.AsObject();
        Assert.True(json.Remove(nameof(TvImportCheckpoint.ImportFileLedger)));
        db.Acquisitions.Add(new AcquisitionRow { Id = id, Kind = television ? EntityKind.VideoSeason : EntityKind.Movie,
            Title = "Recovery fixture", Status = AcquisitionStatus.ManualImportRequired, ImportCheckpointJson = json.ToJsonString(),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return new(id, tv, placement);
    }

    private static PrismediaDbContext MemoryContext() => new(new DbContextOptionsBuilder<PrismediaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed record Fixture(Guid Id, TvImportCheckpoint? Tv, ImportPlacementCheckpoint? Placement);
}
