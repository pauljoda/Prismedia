using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Integrations;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Plugins;
using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Tests;

public sealed class IntegrationGatewayTests : IDisposable {
    private const string CredentialKey = "token";
    private const string Credential = "fixture-private-token";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"prismedia-gateway-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ResponsesMustMatchTheInvocationAndCurrentIntegrationProtocol(bool wrongInvocation, bool wrongVersion) {
        var executor = new ProbeExecutor(wrongInvocation, wrongVersion);
        await using var db = CreateContext();
        var options = new PluginCatalogOptions([], _root, "3.8.0");
        var gateway = new IntegrationPluginGateway(db, new PluginCatalogService(ProviderCredentialTestStore.Create(db), db, options), new PluginProcessTransport(executor, options));
        var call = () => gateway.InvokeAsync<ConnectionProbeInput, ConnectionProbeResult>(Descriptor(), IntegrationOperation.Probe,
            new(Guid.NewGuid(), "http://catalog.test", null, new Dictionary<string, string>(), new Dictionary<string, string> { [CredentialKey] = Credential }),
            new(), CancellationToken.None);
        if (wrongInvocation || wrongVersion) await Assert.ThrowsAsync<IntegrationInvocationException>(call);
        else Assert.Equal("fixture-installation", (await call()).InstanceId);
        Assert.Equal(Credential, executor.ReceivedCredential);
        Assert.False(File.Exists(executor.RequestPath));
    }

    [Fact]
    public async Task RemoteFailuresNeverReturnTheSuppliedCredential() {
        var executor = new ProbeExecutor(false, false, fail: true);
        await using var db = CreateContext();
        var options = new PluginCatalogOptions([], _root, "3.8.0");
        var gateway = new IntegrationPluginGateway(db, new PluginCatalogService(ProviderCredentialTestStore.Create(db), db, options), new PluginProcessTransport(executor, options));
        var error = await Assert.ThrowsAsync<IntegrationInvocationException>(() => gateway.InvokeAsync<ConnectionProbeInput, ConnectionProbeResult>(
            Descriptor(), IntegrationOperation.Probe, new(Guid.NewGuid(), "http://catalog.test", null, new Dictionary<string, string>(),
                new Dictionary<string, string> { [CredentialKey] = Credential }), new(), CancellationToken.None));
        Assert.DoesNotContain(Credential, error.Message);
    }

    private PluginDescriptor Descriptor() => new(new(2, [], "fixture", "Fixture", "1.0.0", DotnetPluginProcessRunner.Code,
        "fixture.dll", new("2.0.0", null, "3.8.0", null), [], false, []), "manifest.json", _root, "fixture.dll");
    private static PrismediaDbContext CreateContext() => new(new DbContextOptionsBuilder<PrismediaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private sealed class ProbeExecutor(bool wrongInvocation, bool wrongVersion, bool fail = false) : ProcessExecutor {
        public string? ReceivedCredential { get; private set; }
        public string? RequestPath { get; private set; }
        public override async Task<ProcessExecutionResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment, CancellationToken cancellationToken, ProcessExecutionOptions options, bool lowPriority = false) {
            RequestPath = arguments[1];
            var request = JsonSerializer.Deserialize<IntegrationPluginRequest<ConnectionProbeInput>>(
                await File.ReadAllTextAsync(RequestPath, cancellationToken), PluginProcessTransport.JsonOptions)!;
            ReceivedCredential = request.Connection.Auth[CredentialKey];
            var response = new IntegrationPluginResponse<ConnectionProbeResult>(IntegrationProtocol.Name,
                wrongVersion ? 999 : IntegrationProtocol.CurrentVersion, wrongInvocation ? Guid.NewGuid() : request.InvocationId,
                !fail, fail ? null : new("fixture-installation", "Fixture", "1.0.0", []), fail ? $"Credential rejected: {ReceivedCredential}" : null);
            return new(0, JsonSerializer.Serialize(response, PluginProcessTransport.JsonOptions), string.Empty);
        }
    }
}
