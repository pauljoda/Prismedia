using System.Security.Cryptography;
using Prismedia.IntegrationSimulator;

namespace Prismedia.IntegrationSimulator.Tests;

public sealed class ExecutorContractTests : IDisposable {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "prismedia-executor-" + Guid.NewGuid().ToString("N"));
    private SimulatorStore Open() => new(directory);
    private static async Task<SubmitJob> RequestAsync(SimulatorStore store) {
        var selection = await store.InspectAsync(new("https://fixtures.example/book", ArchiverWire.Book, 1));
        return new(Guid.NewGuid(), new(selection.CanonicalUrl), new(selection.Id, selection.Revision, [selection.Items[0].Id]),
            new(ArchiverWire.PublicationProfile, ArchiverWire.Epub), new(1, 1048576));
    }
    [Fact]
    public async Task ConcurrentDuplicateSubmissionAndRestartKeepOneJobWithOriginalSelection() {
        var store = Open();
        var request = await RequestAsync(store);
        var accepted = await Task.WhenAll(store.SubmitAsync(request, request.ClientOperationId.ToString()), store.SubmitAsync(request, request.ClientOperationId.ToString()));
        Assert.Equal(accepted[0].Snapshot.JobId, accepted[1].Snapshot.JobId);
        var restarted = Open();
        Assert.Equal(store.Info.InstanceId, restarted.Info.InstanceId);
        Assert.Equal(accepted[0].Snapshot.JobId, (await restarted.FindOperationAsync(request.ClientOperationId)).JobId);
        var replay = await restarted.SubmitAsync(request, request.ClientOperationId.ToString());
        Assert.Equal(accepted[0].Snapshot.JobId, replay.Snapshot.JobId);
        var failure = await Assert.ThrowsAsync<ApiFailure>(() => restarted.SubmitAsync(request with { Limits = new(1, 1) }, request.ClientOperationId.ToString()));
        Assert.Equal(409, failure.Status);
    }
    [Fact]
    public async Task LostResponseIsInjectedOnlyAfterAcceptanceCommits() {
        var store = Open();
        await store.ConfigureAsync(new(LoseNextSubmissionResponse: true));
        var request = await RequestAsync(store);
        var submitted = await store.SubmitAsync(request, request.ClientOperationId.ToString());
        Assert.True(submitted.LoseResponse);
        var restarted = Open();
        Assert.Equal(submitted.Snapshot.JobId, (await restarted.FindOperationAsync(request.ClientOperationId)).JobId);
        Assert.False((await restarted.SubmitAsync(request, request.ClientOperationId.ToString())).LoseResponse);
    }
    [Fact]
    public async Task SealedBytesAndReceiptSurviveRestartAndReceiptResponseLoss() {
        var store = Open();
        await store.ConfigureAsync(new(LoseNextReceiptResponse: true, ExecutionDelaySeconds: 0));
        var request = await RequestAsync(store);
        var (job, _) = await store.SubmitAsync(request, request.ClientOperationId.ToString());
        await store.AdvanceAsync();
        var restarted = Open();
        var finished = await restarted.GetAsync(job.JobId);
        Assert.Equal(ArchiverWire.Succeeded, finished.State);
        var manifest = await restarted.ManifestAsync(job.JobId, finished.ManifestRevision!, null);
        var file = Assert.Single(manifest.Artifacts);
        var (path, _) = await restarted.ContentAsync(file.Id);
        var content = await File.ReadAllBytesAsync(path);
        Assert.Equal(file.SizeBytes, content.Length);
        Assert.Equal(file.Sha256, Convert.ToHexStringLower(SHA256.HashData(content)));
        var receipt = new ReceiptRequest(Guid.NewGuid(), manifest.Revision, [new(file.Id, file.Sha256, "owned-local-source")]);
        Assert.True((await restarted.ReceiptAsync(job.JobId, receipt)).LoseResponse);
        var again = Open();
        Assert.False((await again.ReceiptAsync(job.JobId, receipt)).LoseResponse);
        await again.AdvanceAsync();
        Assert.Equal(finished.ManifestRevision, (await again.GetAsync(job.JobId)).ManifestRevision);
        Assert.Equal(file.Id, Assert.Single((await again.ManifestAsync(job.JobId, manifest.Revision, null)).Artifacts).Id);
    }
    [Fact]
    public async Task CancellationPreventsExecutionAndDoesNotDeleteDurableIdentity() {
        var store = Open();
        await store.ConfigureAsync(new(ExecutionDelaySeconds: 0));
        var request = await RequestAsync(store);
        var (job, _) = await store.SubmitAsync(request, request.ClientOperationId.ToString());
        await store.CancelAsync(job.JobId);
        await store.AdvanceAsync();
        var result = await Open().FindOperationAsync(request.ClientOperationId);
        Assert.Equal(ArchiverWire.Cancelled, result.State);
        Assert.Null(result.ManifestRevision);
    }
    [Fact]
    public async Task BudgetFailureDoesNotPublishImaginaryArtifacts() {
        var store = Open();
        await store.ConfigureAsync(new(ExecutionDelaySeconds: 0));
        var request = await RequestAsync(store);
        request = request with { Limits = new(1, 1) };
        var (job, _) = await store.SubmitAsync(request, request.ClientOperationId.ToString());
        await store.AdvanceAsync();
        var result = await store.GetAsync(job.JobId);
        Assert.Equal(ArchiverWire.Failed, result.State);
        Assert.Single(result.ItemFailures);
        Assert.Null(result.ManifestRevision);
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
