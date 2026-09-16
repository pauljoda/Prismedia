using System.Text.Json;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Plugins;
using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Tests;

public sealed class PluginProcessSafetyTests : IDisposable {
    private const string CredentialKey = "token";
    private const string CredentialValue = "private-token+/example";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"prismedia-plugin-safety-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PluginFailuresRedactCredentialsAndDeleteProtectedRequestFiles(bool processFailure) {
        var error = $"Credential {CredentialValue} or {Uri.EscapeDataString(CredentialValue)} was rejected.";
        var executor = new InspectingExecutor(processFailure
            ? new ProcessExecutionResult(1, string.Empty, error)
            : new ProcessExecutionResult(0, JsonSerializer.Serialize(new { ok = false, error }), string.Empty));
        var response = await RunAsync(executor);

        Assert.False(response.Ok);
        Assert.DoesNotContain(CredentialValue, response.Error);
        Assert.DoesNotContain(Uri.EscapeDataString(CredentialValue), response.Error);
        Assert.False(File.Exists(executor.RequestPath));
        Assert.False(executor.Options?.InheritEnvironment ?? true);
        Assert.True(executor.Options?.MaxStandardOutputCharacters is > 0);
        Assert.True(executor.Options?.MaxStandardErrorCharacters is > 0);
        Assert.DoesNotContain(CredentialValue, executor.Environment?.Values ?? []);
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, executor.RequestMode);
    }

    [Fact]
    public async Task OutputLimitIsReportedAsFailureAndCleansTheRequest() {
        var executor = new InspectingExecutor(new ProcessExecutionResult(0, string.Empty, string.Empty), exceedLimit: true);

        var response = await RunAsync(executor);

        Assert.False(response.Ok);
        Assert.Contains("output", response.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(executor.RequestPath));
    }

    private Task<IdentifyPluginResponse> RunAsync(ProcessExecutor executor) {
        var descriptor = new PluginDescriptor(new PluginManifest(2, [], "fixture-provider", "Fixture", "1.0.0",
            DotnetPluginProcessRunner.Code, "fixture.dll", new PluginCompatibility("2.0.0", null, "1.0.0", null),
            [], false, []), Path.Combine(_root, "manifest.json"), _root, Path.Combine(_root, "fixture.dll"));
        var request = new IdentifyPluginRequest(PluginProtocol.CurrentVersion, IdentifyAction.Search,
            new Dictionary<string, string> { [CredentialKey] = CredentialValue },
            new IdentifyEntitySnapshot(Guid.NewGuid(), EntityKind.Book, "Fixture book"), new IdentifyQuery(null, null, null),
            new IdentifyMatchHints(new Dictionary<string, string>(), [], null, null));
        return new DotnetPluginProcessRunner(executor, new PluginCatalogOptions([], _root, "3.8.0"))
            .IdentifyAsync(descriptor, request, CancellationToken.None);
    }

    public void Dispose() {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class InspectingExecutor(ProcessExecutionResult result, bool exceedLimit = false) : ProcessExecutor {
        public string? RequestPath { get; private set; }
        public UnixFileMode? RequestMode { get; private set; }
        public ProcessExecutionOptions? Options { get; private set; }
        public IReadOnlyDictionary<string, string>? Environment { get; private set; }

        public override Task<ProcessExecutionResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment, CancellationToken cancellationToken, bool lowPriority = false) =>
            Inspect(arguments, environment, null);

        public override Task<ProcessExecutionResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment, CancellationToken cancellationToken,
            ProcessExecutionOptions options, bool lowPriority = false) => Inspect(arguments, environment, options);

        private Task<ProcessExecutionResult> Inspect(IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment, ProcessExecutionOptions? options) {
            RequestPath = arguments[1];
            if (!OperatingSystem.IsWindows()) RequestMode = File.GetUnixFileMode(RequestPath);
            Options = options;
            Environment = environment;
            return exceedLimit ? Task.FromException<ProcessExecutionResult>(new ProcessOutputLimitException()) : Task.FromResult(result);
        }
    }
}
