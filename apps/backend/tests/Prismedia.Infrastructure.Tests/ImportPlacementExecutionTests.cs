using Microsoft.EntityFrameworkCore;
using System.Text.Json.Nodes;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>Crash-window coverage for the shared non-TV exact-placement checkpoint.</summary>
public sealed class ImportPlacementExecutionTests : IDisposable {
    private readonly string _workRoot = Directory.CreateTempSubdirectory("prismedia-placement-checkpoint-").FullName;

    public void Dispose() {
        try {
            Directory.Delete(_workRoot, recursive: true);
        } catch {
            // best-effort test cleanup
        }
    }

    [Theory]
    [InlineData(ImportMode.Move)]
    [InlineData(ImportMode.Copy)]
    [InlineData(ImportMode.Hardlink)]
    public async Task RetryAdoptsExactPublishedTargetWithoutDuplicateSuffix(ImportMode mode) {
        await using var db = CreateContext();
        var payloadRoot = Directory.CreateDirectory(Path.Combine(_workRoot, $"payload-{mode}")).FullName;
        var libraryRoot = Directory.CreateDirectory(Path.Combine(_workRoot, $"library-{mode}")).FullName;
        var source = Path.Combine(payloadRoot, "Novel.epub");
        var target = Path.Combine(libraryRoot, "Author", "Novel.epub");
        await File.WriteAllTextAsync(source, $"bytes-{mode}");

        var acquisitionId = Guid.NewGuid();
        var claimJobId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            Kind = EntityKind.Book,
            Status = AcquisitionStatus.Importing,
            Title = "Novel",
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            ImportClaimJobId = claimJobId,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var checkpoint = new ImportPlacementCheckpoint(
            EntityKind.Book,
            Guid.NewGuid(),
            Path.GetFullPath(libraryRoot),
            Path.GetFullPath(payloadRoot),
            mode,
            Path.GetDirectoryName(target)!,
            Path.GetDirectoryName(target)!,
            "Imported into the library.",
            [new ImportPlacementCheckpointUnit(
                "Novel.epub",
                Path.GetFullPath(source),
                Path.GetFullPath(target),
                IsMedia: true)],
            AttemptId: Guid.NewGuid(),
            ClaimJobId: claimJobId);
        var store = AcquisitionTestFactory.Store(db);
        Assert.True(await store.TryCreateImportPlacementCheckpointAsync(
            acquisitionId,
            checkpoint,
            CancellationToken.None));

        await Assert.ThrowsAsync<SyntheticPlacementCrashException>(() =>
            ImportPlacementExecution.ExecuteAsync(
                store,
                new CrashAfterExactPlacementMover(new ImportFileMover()),
                acquisitionId,
                checkpoint,
                CancellationToken.None));

        Assert.True(File.Exists(target));
        Assert.Equal(mode != ImportMode.Move, File.Exists(source));
        var persisted = Assert.IsType<ImportPlacementCheckpoint>(
            (await store.GetImportContextAsync(acquisitionId, CancellationToken.None))?.ImportPlacementCheckpoint);
        Assert.Null(Assert.Single(persisted.Units).FinalPath);

        var completed = Assert.IsType<ImportPlacementCheckpoint>(
            await ImportPlacementExecution.ExecuteAsync(
                store,
                new ImportFileMover(),
                acquisitionId,
                persisted,
                CancellationToken.None));

        Assert.Equal(Path.GetFullPath(target), Assert.Single(completed.Units).FinalPath);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(target)!, "Novel (2).epub")));
        Assert.Single(Directory.GetFiles(libraryRoot, "*.epub", SearchOption.AllDirectories));

        await store.MarkImportedWithQualityAsync(
            acquisitionId,
            BookQualityRank.Floor,
            "Imported.",
            CancellationToken.None);
        Assert.Null((await store.GetImportContextAsync(acquisitionId, CancellationToken.None))?.ImportPlacementCheckpoint);
    }

    [Fact]
    public void CheckpointCodecRejectsAbsoluteSourceThatDoesNotMatchPayloadRelativePath() {
        var payloadRoot = Path.GetFullPath(Path.Combine(_workRoot, "codec-payload"));
        var libraryRoot = Path.GetFullPath(Path.Combine(_workRoot, "codec-library"));
        var source = Path.Combine(payloadRoot, "Novel.epub");
        var target = Path.Combine(libraryRoot, "Novel.epub");
        var checkpoint = new ImportPlacementCheckpoint(
            EntityKind.Book,
            Guid.NewGuid(),
            libraryRoot,
            payloadRoot,
            ImportMode.Move,
            libraryRoot,
            libraryRoot,
            "Imported.",
            [new ImportPlacementCheckpointUnit("Novel.epub", source, target, IsMedia: true)],
            AttemptId: Guid.NewGuid(),
            ClaimJobId: Guid.NewGuid());
        var json = JsonNode.Parse(ImportPlacementCheckpointJson.Serialize(checkpoint))!.AsObject();
        json["Units"]!.AsArray()[0]!["SourceAbsolutePath"] = Path.Combine(_workRoot, "outside.epub");

        Assert.Throws<InvalidDataException>(() =>
            ImportPlacementCheckpointJson.Deserialize(json.ToJsonString()));
    }

    private static PrismediaDbContext CreateContext() => new(
        new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class CrashAfterExactPlacementMover(IImportFileMover inner) : IImportFileMover {
        public string ResolveExactTargetPath(
            string desiredTargetPath,
            IReadOnlyCollection<string> reservedTargetPaths) =>
            inner.ResolveExactTargetPath(desiredTargetPath, reservedTargetPaths);

        public Task<string> PlaceAsync(
            ResolvedImportItem item,
            ImportMode mode,
            CancellationToken cancellationToken) =>
            inner.PlaceAsync(item, mode, cancellationToken);

        public async Task<string> PlaceExactAsync(
            ResolvedImportItem item,
            ImportMode mode,
            CancellationToken cancellationToken) {
            await inner.PlaceExactAsync(item, mode, cancellationToken);
            throw new SyntheticPlacementCrashException();
        }
    }

    private sealed class SyntheticPlacementCrashException : Exception;
}
