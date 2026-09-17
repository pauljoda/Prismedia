using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Integrations;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Tests;

public sealed class IntegrationArtifactStagingMaintenanceTests : IDisposable {
    private readonly string workspace = Directory.CreateTempSubdirectory("prismedia-artifact-cleanup-").FullName;
    private string Staging => Path.Combine(workspace, "staging");

    [Fact]
    public async Task AgedCompletedImportDeletesOnlyAllowlistedStagingWithoutReadingProtectedPlan() {
        await using var db = Context();
        var (row, artifact) = CompletedRow(IntegrationTransferMode.RemoteExecutor);
        row.ProtectedPlan = "not decryptable";
        row.UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2);
        db.IntegrationTransfers.Add(row);
        await db.SaveChangesAsync();
        var directory = Stage(row.Id, artifact);
        var library = Path.Combine(workspace, "library.epub");
        await File.WriteAllTextAsync(library, "imported destination");

        var deleted = await new EfIntegrationArtifactStagingMaintenance(db, new(Staging))
            .SweepAsync(DateTimeOffset.UtcNow.AddHours(-24), default);

        Assert.Equal(1, deleted);
        Assert.False(Directory.Exists(directory));
        Assert.Equal("imported destination", await File.ReadAllTextAsync(library));
    }

    [Fact]
    public async Task UnfinishedRecentActiveAndIncompleteEvidenceAreRetained() {
        await using var db = Context();
        var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
        var cases = new List<(IntegrationTransferRow Row, IntegrationArtifact Artifact)>();
        for (var index = 0; index < 7; index++) {
            var item = CompletedRow(IntegrationTransferMode.SourceDownload);
            item.Row.UpdatedAt = cutoff.AddDays(-1);
            cases.Add(item);
        }
        cases[0].Row.Phase = IntegrationTransferPhase.Importing;
        cases[1].Row.Phase = IntegrationTransferPhase.NeedsReview;
        cases[2].Row.Phase = IntegrationTransferPhase.AwaitingAcknowledgement;
        cases[3].Row.UpdatedAt = DateTimeOffset.UtcNow;
        var incomplete = JsonSerializer.Deserialize<IntegrationTransferState>(cases[4].Row.StateJson, PluginProcessTransport.JsonOptions)!;
        cases[4].Row.StateJson = JsonSerializer.Serialize(incomplete with { Imports = [] }, PluginProcessTransport.JsonOptions);
        var noReceipt = JsonSerializer.Deserialize<IntegrationTransferState>(cases[5].Row.StateJson, PluginProcessTransport.JsonOptions)!;
        cases[5].Row.StateJson = JsonSerializer.Serialize(noReceipt with { ReceiptId = null }, PluginProcessTransport.JsonOptions);
        db.IntegrationTransfers.AddRange(cases.Select(item => item.Row));
        db.JobRuns.Add(new JobRunRow { Id = Guid.NewGuid(), Type = JobType.IntegrationTransfer, Status = JobRunStatus.Running,
            TargetEntityId = cases[6].Row.Id.ToString(), AvailableAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var directories = cases.Select(item => Stage(item.Row.Id, item.Artifact)).ToArray();

        Assert.Equal(0, await new EfIntegrationArtifactStagingMaintenance(db, new(Staging)).SweepAsync(cutoff, default));
        Assert.All(directories, directory => Assert.True(Directory.Exists(directory)));
    }

    [Fact]
    public async Task UnknownNestedAndExclusivelyLockedContentSkipsWholeOperation() {
        await using var db = Context();
        var rows = Enumerable.Range(0, 3).Select(_ => CompletedRow(IntegrationTransferMode.SourceDownload)).ToArray();
        foreach (var item in rows) item.Row.UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2);
        db.IntegrationTransfers.AddRange(rows.Select(item => item.Row));
        await db.SaveChangesAsync();
        var unknown = Stage(rows[0].Row.Id, rows[0].Artifact);
        await File.WriteAllTextAsync(Path.Combine(unknown, "unexpected"), "retain");
        var nested = Stage(rows[1].Row.Id, rows[1].Artifact);
        Directory.CreateDirectory(Path.Combine(nested, "nested"));
        var locked = Stage(rows[2].Row.Id, rows[2].Artifact);
        var lockPath = Path.Combine(locked, HttpIntegrationArtifactTransfer.ArtifactKey(rows[2].Artifact.Id) + ".lock");
        await using var ownership = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        Assert.Equal(0, await new EfIntegrationArtifactStagingMaintenance(db, new(Staging))
            .SweepAsync(DateTimeOffset.UtcNow.AddHours(-24), default));
        Assert.True(File.Exists(Path.Combine(unknown, "unexpected")));
        Assert.True(Directory.Exists(Path.Combine(nested, "nested")));
        Assert.True(Directory.Exists(locked));
    }

    [Fact]
    public async Task LinkedArtifactIsRejectedAndPreserved() {
        if (OperatingSystem.IsWindows()) return;
        await using var db = Context();
        var item = CompletedRow(IntegrationTransferMode.SourceDownload);
        item.Row.UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2);
        db.IntegrationTransfers.Add(item.Row);
        await db.SaveChangesAsync();
        var directory = Stage(item.Row.Id, item.Artifact);
        var path = Directory.GetFiles(directory).Single(file => Path.GetExtension(file) == ".epub");
        File.Delete(path);
        var outside = Path.Combine(workspace, "outside.epub");
        await File.WriteAllTextAsync(outside, "preserve");
        File.CreateSymbolicLink(path, outside);

        Assert.Equal(0, await new EfIntegrationArtifactStagingMaintenance(db, new(Staging))
            .SweepAsync(DateTimeOffset.UtcNow.AddHours(-24), default));
        Assert.True(File.Exists(path));
        Assert.Equal("preserve", await File.ReadAllTextAsync(outside));
    }

    private string Stage(Guid operationId, IntegrationArtifact artifact) {
        var directory = Directory.CreateDirectory(Path.Combine(Staging, operationId.ToString("N"))).FullName;
        var key = HttpIntegrationArtifactTransfer.ArtifactKey(artifact.Id);
        File.WriteAllText(Path.Combine(directory, key + Path.GetExtension(artifact.RelativePath).ToLowerInvariant()), "staged");
        File.WriteAllText(Path.Combine(directory, key + ".verified.json"), "receipt");
        return directory;
    }

    private static (IntegrationTransferRow Row, IntegrationArtifact Artifact) CompletedRow(IntegrationTransferMode mode) {
        var operationId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var artifact = new IntegrationArtifact("artifact", "selected", "book.epub", "application/epub+zip", 6,
            new string('a', 64), IntegrationArtifactRole.Content);
        var import = new IntegrationArtifactImport(artifact.Id, artifact.Sha256, [Guid.NewGuid()]);
        var state = new IntegrationTransferState(operationId, connectionId, mode == IntegrationTransferMode.RemoteExecutor ? "instance" : null,
            7, IntegrationTransferPhase.Completed, Artifacts: [artifact], VerifiedArtifactIds: [artifact.Id], Imports: [import],
            ReceiptId: Guid.NewGuid(), Mode: mode);
        return (new IntegrationTransferRow { Id = operationId, ConnectionId = connectionId, Revision = state.Revision,
            Phase = state.Phase, StateJson = JsonSerializer.Serialize(state, PluginProcessTransport.JsonOptions), ProtectedPlan = "ignored",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3), UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2) }, artifact);
    }

    private static PrismediaDbContext Context() => new(new DbContextOptionsBuilder<PrismediaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() { if (Directory.Exists(workspace)) Directory.Delete(workspace, true); }
}
