using System.Security.Cryptography;
using System.Text;
using Microsoft.Net.Http.Headers;
using Prismedia.IntegrationSimulator;

var builder = WebApplication.CreateBuilder(args);
var token = builder.Configuration[SimulatorConfiguration.Token] ?? throw new InvalidOperationException("Configure a simulator token before startup.");
if (token.Length < 24) throw new InvalidOperationException("The simulator token must contain at least 24 characters.");
var stateDirectory = builder.Configuration[SimulatorConfiguration.Data] ?? throw new InvalidOperationException("Configure an isolated simulator data directory.");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024 * 1024);
builder.Services.AddOpenApi();
builder.Services.AddSingleton(new SimulatorStore(stateDirectory));
builder.Services.AddHostedService<ExecutionWorker>();
var app = builder.Build();
app.Use(async (context, next) => {
    var expected = SHA256.HashData(Encoding.UTF8.GetBytes("Bearer " + token));
    var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(context.Request.Headers.Authorization.ToString()));
    if (!CryptographicOperations.FixedTimeEquals(expected, supplied)) {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new ApiProblem(ArchiverWire.Unauthorized, "A valid integration credential is required."));
        return;
    }
    try { await next(context); }
    catch (ApiFailure error) { context.Response.StatusCode = error.Status; await context.Response.WriteAsJsonAsync(new ApiProblem(error.Code, error.Message)); }
});
app.MapOpenApi();
var api = app.MapGroup("/api/v1");
api.MapGet("/system", (SimulatorStore store) => store.Info);
api.MapGet("/sources", () => new { items = new[] { new SourceInfo(ArchiverWire.SourceId, "Synthetic publications", [ArchiverWire.Inspect], [ArchiverWire.Book, ArchiverWire.Comic], true) }, nextCursor = (string?)null });
api.MapPost("/inspect", (InspectRequest input, SimulatorStore store) => store.InspectAsync(input));
api.MapGet("/operations/{operation:guid}", (Guid operation, SimulatorStore store) => store.FindOperationAsync(operation));
api.MapPost("/operations/{operation:guid}/cancel", (Guid operation, SimulatorStore store) => store.CancelOperationAsync(operation));
api.MapPost("/jobs", async (SubmitJob input, HttpContext context, SimulatorStore store) => {
    var (snapshot, lose) = await store.SubmitAsync(input, context.Request.Headers["Idempotency-Key"]);
    if (lose) { context.Abort(); return Results.Empty; }
    return Results.Accepted($"/api/v1/jobs/{snapshot.JobId}", snapshot);
}).Produces<JobSnapshot>(202);
api.MapGet("/jobs", (string? cursor, int? limit, SimulatorStore store) => store.ListAsync(cursor, limit ?? 100));
api.MapGet("/jobs/{id}", (string id, SimulatorStore store) => store.GetAsync(id));
api.MapPost("/jobs/{id}/cancel", (string id, SimulatorStore store) => store.CancelAsync(id));
api.MapGet("/jobs/{id}/artifacts", (string id, string revision, string? cursor, SimulatorStore store) => store.ManifestAsync(id, revision, cursor));
api.MapMethods("/artifacts/{id}/content", ["GET", "HEAD"], async (string id, SimulatorStore store) => {
    var (path, artifact) = await store.ContentAsync(id);
    return Results.File(path, artifact.MediaType, enableRangeProcessing: true, entityTag: new EntityTagHeaderValue('"' + artifact.Sha256 + '"'));
});
api.MapPost("/jobs/{id}/lease", (string id, LeaseRequest input, SimulatorStore store) => store.LeaseAsync(id, input));
api.MapPost("/jobs/{id}/receipts", async (string id, ReceiptRequest input, HttpContext context, SimulatorStore store) => {
    var (receipt, lose) = await store.ReceiptAsync(id, input);
    if (lose) { context.Abort(); return Results.Empty; }
    return Results.Ok(receipt);
}).Produces<ReceiptResult>();
app.MapPost("/simulation/controls", (SimulationControls controls, SimulatorStore store) => store.ConfigureAsync(controls));
await app.RunAsync();

/// <summary>Canonical simulator configuration keys, separate from any production application settings.</summary>
internal static class SimulatorConfiguration {
    internal const string Token = "SIMULATOR_TOKEN";
    internal const string Data = "SIMULATOR_DATA";
}
/// <summary>Advances bounded fixture jobs and recovers unfinished jobs after restart.</summary>
internal sealed class ExecutionWorker(SimulatorStore store) : BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken)) await store.AdvanceAsync();
    }
}
