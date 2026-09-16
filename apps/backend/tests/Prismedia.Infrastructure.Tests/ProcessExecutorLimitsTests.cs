using System.Diagnostics;
using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Tests;

public sealed class ProcessExecutorLimitsTests {
    private const string TestEnvironmentKey = "PRISMEDIA_PROCESS_TEST_VALUE";
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExcessiveOutputTerminatesAStillRunningProcess(bool standardError) {
        var executor = new ProcessExecutor();
        var script = standardError ? "while :; do printf 'more output' >&2; done" : "while :; do printf 'more output'; done";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var watch = Stopwatch.StartNew();

        await Assert.ThrowsAsync<ProcessOutputLimitException>(() => executor.RunAsync(
            "/bin/sh", ["-c", script], null, timeout.Token,
            options: new ProcessExecutionOptions(MaxStandardOutputCharacters: 1024, MaxStandardErrorCharacters: 1024)));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "The output limit must stop the process before its deadline.");
    }

    [Fact]
    public async Task RestrictedEnvironmentIncludesOnlyExplicitlySuppliedVariables() {
        var executor = new ProcessExecutor();
        var result = await executor.RunAsync("/usr/bin/env", [],
            new Dictionary<string, string> { [TestEnvironmentKey] = "explicit value" },
            CancellationToken.None, options: new ProcessExecutionOptions(InheritEnvironment: false));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"{TestEnvironmentKey}=explicit value", result.StandardOutput.Trim());
    }

    [Fact]
    public async Task OutputAtTheLimitRemainsComplete() {
        var executor = new ProcessExecutor();
        var result = await executor.RunAsync("/bin/sh", ["-c", "printf '1234'; printf 'abcd' >&2"], null,
            CancellationToken.None, options: new ProcessExecutionOptions(MaxStandardOutputCharacters: 4, MaxStandardErrorCharacters: 4));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1234", result.StandardOutput);
        Assert.Equal("abcd", result.StandardError);
    }
}
