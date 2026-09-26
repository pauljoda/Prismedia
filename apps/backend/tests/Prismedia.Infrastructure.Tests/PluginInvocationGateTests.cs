using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Prismedia.Contracts.Plugins;
using Prismedia.Infrastructure.Plugins;
using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Tests;

public sealed class PluginInvocationGateTests {
    [Fact]
    public async Task SeparateTransportsHonorTheSamePolicyBeforeLaunchingAndReleaseFailedCalls() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await using var source = NpgsqlDataSource.Create(db.Database.GetConnectionString()!);
        var root = Path.Combine(Path.GetTempPath(), "prismedia-paced-" + Guid.NewGuid().ToString("N"));
        try {
            var processes = new CountingExecutor();
            var options = new PluginCatalogOptions([], root, "3.8.0");
            var first = new PluginProcessTransport(processes, options, Gate(source));
            var second = new PluginProcessTransport(processes, options, Gate(source));
            var descriptor = new PluginDescriptor(new PluginManifest(2, [], "shared-provider", "Fixture", "1.0.0",
                DotnetPluginProcessRunner.Code, "fixture.dll", new("2.0.0", null, "1.0.0", null), [], false, [], Execution: new(1, 0)),
                Path.Combine(root, "manifest.json"), root, Path.Combine(root, "fixture.dll"));
            await Task.WhenAll(first.RunAsync(descriptor, new { }, default), second.RunAsync(descriptor, new { }, default));
            Assert.Equal(1, processes.MaximumActive);
            Assert.Equal(2, processes.Calls);
            processes.Fail = true;
            await Assert.ThrowsAsync<ProcessOutputLimitException>(() => first.RunAsync(descriptor, new { }, default));
            Assert.Empty(await db.PluginInvocationLeases.ToArrayAsync());
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "plugins", "requests")));
        } finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class CountingExecutor : ProcessExecutor {
        private int active;
        internal int Calls;
        private readonly System.Collections.Concurrent.ConcurrentBag<int> observed = [];
        internal int MaximumActive => observed.Max();
        internal bool Fail;
        public override async Task<ProcessExecutionResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment, CancellationToken cancellationToken, ProcessExecutionOptions options, bool lowPriority = false) {
            Interlocked.Increment(ref Calls);
            var count = Interlocked.Increment(ref active);
            observed.Add(count);
            try {
                if (Fail) throw new ProcessOutputLimitException();
                await Task.Delay(150, cancellationToken);
                return new(0, "{}", string.Empty);
            } finally { Interlocked.Decrement(ref active); }
        }
    }

    [Fact]
    public async Task IndependentRuntimesShareCapacityAndCancellationDoesNotClaimAnotherSlot() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await using var firstSource = NpgsqlDataSource.Create(db.Database.GetConnectionString()!);
        await using var secondSource = NpgsqlDataSource.Create(db.Database.GetConnectionString()!);
        var api = Gate(firstSource);
        var worker = Gate(secondSource);
        var lease = await api.AcquireAsync("shared-provider", new(1, 0), default);
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await worker.AcquireAsync("shared-provider", new(1, 0), cancelled.Token));
        Assert.Single(await db.PluginInvocationLeases.ToArrayAsync());
        await using (await worker.AcquireAsync("different-provider", new(1, 0), default)) { }
        await lease.DisposeAsync();
        await lease.DisposeAsync();
        await using (await worker.AcquireAsync("shared-provider", new(1, 0), default)) { }
        Assert.Empty(await db.PluginInvocationLeases.ToArrayAsync());
    }

    [Fact]
    public async Task ReleasingCapacityAndRestartingTheGateRetainsTheMinimumStartInterval() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await using var source = NpgsqlDataSource.Create(db.Database.GetConnectionString()!);
        var elapsed = Stopwatch.StartNew();
        await using (await Gate(source).AcquireAsync("paced-provider", new(4, 500), default)) { }
        await using (await Gate(source).AcquireAsync("paced-provider", new(4, 500), default)) { }
        Assert.True(elapsed.Elapsed >= TimeSpan.FromMilliseconds(500));
        Assert.Empty(await db.PluginInvocationLeases.ToArrayAsync());
    }

    [Fact]
    public async Task AbandonedExpiredSlotsCanBeRecoveredWithoutRemovingTheProviderBudget() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await using var source = NpgsqlDataSource.Create(db.Database.GetConnectionString()!);
        var abandoned = await Gate(source).AcquireAsync("crashed-provider", new(1, 0), default);
        await db.PluginInvocationLeases.ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        await using (await Gate(source).AcquireAsync("crashed-provider", new(1, 0), default)) {
            Assert.Single(await db.PluginInvocationLeases.AsNoTracking().ToArrayAsync());
        }
        await abandoned.DisposeAsync();
        Assert.Empty(await db.PluginInvocationLeases.ToArrayAsync());
        Assert.Single(await db.PluginInvocationStates.ToArrayAsync());
    }

    [Fact]
    public async Task SimultaneousAdmissionsRespectTheDeclaredConcurrency() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await using var source = NpgsqlDataSource.Create(db.Database.GetConnectionString()!);
        var active = 0;
        var observed = new System.Collections.Concurrent.ConcurrentBag<int>();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ => {
            await using var lease = await Gate(source).AcquireAsync("parallel-provider", new(2, 0), default);
            observed.Add(Interlocked.Increment(ref active));
            await Task.Delay(100);
            Interlocked.Decrement(ref active);
        }));
        Assert.Equal(2, observed.Max());
        Assert.Empty(await db.PluginInvocationLeases.ToArrayAsync());
    }

    private static PostgresPluginInvocationGate Gate(NpgsqlDataSource source) =>
        new(source, NullLogger<PostgresPluginInvocationGate>.Instance);
}
