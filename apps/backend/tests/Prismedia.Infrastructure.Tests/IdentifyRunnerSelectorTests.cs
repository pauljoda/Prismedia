using Prismedia.Contracts.Plugins;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Tests;

public sealed class IdentifyRunnerSelectorTests {
    [Fact]
    public void ConstructorRejectsCaseInsensitiveDuplicateRuntimeOwners() {
        var exception = Assert.Throws<InvalidOperationException>(() => new IdentifyRunnerSelector([
            new StubIdentifyRunner(DotnetPluginProcessRunner.Code),
            new StubIdentifyRunner(DotnetPluginProcessRunner.Code.ToUpperInvariant())
        ]));

        Assert.Contains(DotnetPluginProcessRunner.Code.ToUpperInvariant(), exception.Message);
    }

    [Fact]
    public void ResolveReportsUnsupportedRuntime() {
        var selector = new IdentifyRunnerSelector([
            new StubIdentifyRunner(DotnetPluginProcessRunner.Code)
        ]);

        var exception = Assert.Throws<InvalidOperationException>(() => selector.Resolve(Descriptor("unknown-runtime")));

        Assert.Contains("unknown-runtime", exception.Message);
    }

    [Fact]
    public void ResolveMatchesOwnedRuntimeCaseInsensitively() {
        var runner = new StubIdentifyRunner(DotnetPluginProcessRunner.Code);
        var selector = new IdentifyRunnerSelector([runner]);

        var selected = selector.Resolve(Descriptor(DotnetPluginProcessRunner.Code.ToUpperInvariant()));

        Assert.Equal(DotnetPluginProcessRunner.Code, selected.RuntimeCode);
    }

    private static PluginDescriptor Descriptor(string runtime) =>
        new(
            new PluginManifest(
                1,
                ["prismedia"],
                "test-plugin",
                "Test Plugin",
                "1.0.0",
                runtime,
                "test-plugin.dll",
                new PluginCompatibility("1.0.0", null, "1.0.0", null),
                [],
                false,
                []),
            "/plugins/test-plugin/manifest.json",
            "/plugins/test-plugin",
            "/plugins/test-plugin/test-plugin.dll");

    private sealed class StubIdentifyRunner(string runtimeCode) : IIdentifyRunner {
        public string RuntimeCode { get; } = runtimeCode;

        public Task<IdentifyPluginResponse> IdentifyAsync(
            PluginDescriptor descriptor,
            IdentifyPluginRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(IdentifyPluginResponse.NoMatch());
    }
}
