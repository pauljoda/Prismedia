using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Prismedia.Archiver;
using Prismedia.ArchiverConformance;

namespace Prismedia.IntegrationSimulator.Tests;

/// <summary>The public acceptance checker must reject unverified bytes and retain the same durable operation when resumed.</summary>
public sealed class ConformanceRunnerTests : IDisposable {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "archiver-conformance-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(ArchiverWire.Book, ArchiverWire.Epub)]
    [InlineData(ArchiverWire.Gallery, ArchiverWire.ImageSet)]
    public async Task SealedOutputsAreVerifiedBeforeReceiptAndResumeKeepsTheOperation(string kind, string format) {
        var transport = new FixtureTransport(Path.Combine(directory, "server"));
        using var http = new HttpClient(transport) { BaseAddress = new("http://fixture.test/") };
        var runner = new ConformanceRunner(http);
        var result = await runner.RunAsync(new(http.BaseAddress, $"https://fixtures.example/{kind}", kind, format, directory, null), default);
        Assert.Equal(6, result.Checks.Count);
        Assert.Equal(2, transport.Receipts);
        var plan = JsonSerializer.Deserialize<ConformancePlan>(await File.ReadAllTextAsync(result.PlanPath!), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var before = await transport.Store.FindOperationAsync(plan.Request.ClientOperationId);
        var resumed = await runner.RunAsync(new(http.BaseAddress, null, null, null, null, result.PlanPath), default);
        Assert.Equal(result.PlanPath, resumed.PlanPath);
        Assert.Equal(before.JobId, (await transport.Store.FindOperationAsync(plan.Request.ClientOperationId)).JobId);
        Assert.Equal(4, transport.Receipts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptOrCrossOriginArtifactsNeverReceiveAnImportReceipt(bool crossOrigin) {
        var transport = new FixtureTransport(Path.Combine(directory, "server")) { CorruptBytes = !crossOrigin, CrossOrigin = crossOrigin };
        using var http = new HttpClient(transport) { BaseAddress = new("http://fixture.test/") };
        await Assert.ThrowsAsync<ConformanceFailure>(() => new ConformanceRunner(http).RunAsync(
            new(http.BaseAddress, "https://fixtures.example/book", ArchiverWire.Book, ArchiverWire.Epub, directory, null), default));
        Assert.Equal(0, transport.Receipts);
        Assert.False(transport.RequestedOtherOrigin);
        Assert.Single(Directory.GetFiles(directory, "plan.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task DefaultProbeHasNoExecutionEffects() {
        var transport = new FixtureTransport(Path.Combine(directory, "server"));
        using var http = new HttpClient(transport) { BaseAddress = new("http://fixture.test/") };
        var result = await new ConformanceRunner(http).RunAsync(ConformanceOptions.Parse(["--endpoint", http.BaseAddress.AbsoluteUri]), default);
        Assert.Single(result.Checks); Assert.Null(result.PlanPath); Assert.Equal(1, transport.Requests);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    private sealed class FixtureTransport(string directory) : HttpMessageHandler {
        public SimulatorStore Store { get; } = new(directory);
        public bool CorruptBytes { get; init; }
        public bool CrossOrigin { get; init; }
        public bool RequestedOtherOrigin { get; private set; }
        public int Receipts { get; private set; }
        public int Requests { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
            Requests++;
            RequestedOtherOrigin |= request.RequestUri!.Host != "fixture.test";
            await Store.ConfigureAsync(new(ExecutionDelaySeconds: 0));
            await Store.AdvanceAsync();
            var parts = request.RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            try {
                object value;
                if (parts[2] == "system") value = Store.Info;
                else if (parts[2] == "inspect") value = await Store.InspectAsync((await request.Content!.ReadFromJsonAsync<InspectRequest>(token))!);
                else if (parts[2] == "operations") value = parts.Length == 5
                    ? await Store.CancelOperationAsync(Guid.Parse(parts[3])) : await Store.FindOperationAsync(Guid.Parse(parts[3]));
                else if (parts[2] == "artifacts") {
                    var (path, artifact) = await Store.ContentAsync(parts[3]);
                    var bytes = await File.ReadAllBytesAsync(path, token);
                    if (CorruptBytes) bytes[0] ^= 1;
                    var range = request.Headers.Range is not null;
                    var content = new ByteArrayContent(range ? [bytes[0]] : bytes);
                    content.Headers.ContentLength = range ? 1 : artifact.SizeBytes;
                    if (range) content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, artifact.SizeBytes);
                    return new(range ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = content };
                } else if (parts.Length == 3) value = (await Store.SubmitAsync((await request.Content!.ReadFromJsonAsync<SubmitJob>(token))!, request.Headers.GetValues("Idempotency-Key").Single())).Snapshot;
                else if (parts.Length == 4) value = await Store.GetAsync(parts[3]);
                else if (parts[4] == "artifacts") {
                    var job = await Store.GetAsync(parts[3]);
                    var manifest = await Store.ManifestAsync(parts[3], job.ManifestRevision!, null);
                    value = CrossOrigin ? manifest with { Artifacts = manifest.Artifacts.Select(x => x with { ContentPath = "https://other.test/secret" }).ToArray() } : manifest;
                } else if (parts[4] == "lease") value = await Store.LeaseAsync(parts[3], (await request.Content!.ReadFromJsonAsync<LeaseRequest>(token))!);
                else {
                    value = (await Store.ReceiptAsync(parts[3], (await request.Content!.ReadFromJsonAsync<ReceiptRequest>(token))!)).Receipt;
                    Receipts++;
                }
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
            } catch (ApiFailure failure) {
                return new((HttpStatusCode)failure.Status) { Content = JsonContent.Create(new ApiProblem(failure.Code, failure.Message)) };
            }
        }
    }
}
