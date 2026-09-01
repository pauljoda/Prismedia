using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Jobs;

public sealed class JobDefinitionRegistryTests {
    [Fact]
    public void EveryDurableJobTypeHasExactlyOneDiscoveredDefinition() {
        Assert.Equal(Enum.GetValues<JobType>().Order(), JobDefinitionRegistry.All.Select(definition => definition.Type).Order());
        Assert.Equal(JobDefinitionRegistry.All.Count, JobDefinitionRegistry.All.Select(definition => definition.Type).Distinct().Count());
    }

    [Fact]
    public void DefinitionsPreserveRepresentativeSchedulingPolicies() {
        AssertDefinition(JobType.GeneratePreview, JobResourceClass.HeavyCpu, JobNodeImportance.BestEffort, blocksAutoIdentify: false);
        AssertDefinition(JobType.GenerateTrickplay, JobResourceClass.HeavyCpu, JobNodeImportance.Deferred, blocksAutoIdentify: false);
        AssertDefinition(JobType.ExtractSubtitles, JobResourceClass.HeavyCpu, JobNodeImportance.BestEffort, blocksAutoIdentify: false);
        AssertDefinition(JobType.FingerprintVideo, JobResourceClass.StandardCpu, JobNodeImportance.BestEffort, blocksAutoIdentify: true);
        Assert.True(JobDefinitionRegistry.IsQueueWideSingleton(JobType.ScanLibrary, hasTarget: false));
        Assert.False(JobDefinitionRegistry.IsQueueWideSingleton(JobType.ScanLibrary, hasTarget: true));
        Assert.True(JobDefinitionRegistry.BlocksAutoIdentify(JobType.ScanLibrary));
        Assert.True(JobDefinitionRegistry.IsQueueWideSingleton(JobType.MonitoredSearch, hasTarget: false));
        Assert.False(JobDefinitionRegistry.IsQueueWideSingleton(JobType.MonitoredSearch, hasTarget: true));
    }

    [Fact]
    public void DiscoveryFailsClearlyForDuplicateDefinitions() {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            JobDefinitionRegistry.BuildForHandlerTypes([typeof(DuplicateNoopHandler), typeof(SecondDuplicateNoopHandler)]));

        Assert.Contains(JobType.Noop.ToCode(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoveryFailsClearlyForMissingDefinitions() {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            JobDefinitionRegistry.BuildForHandlerTypes([typeof(DuplicateNoopHandler)]));

        Assert.Contains("missing definitions", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiscoveryRejectsHandlersThatDoNotDefineTheirJob() {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            JobDefinitionRegistry.BuildForHandlerTypes([typeof(UndeclaredHandler)]));

        Assert.Contains(nameof(UndeclaredHandler), exception.Message, StringComparison.Ordinal);
    }

    private static void AssertDefinition(
        JobType type,
        JobResourceClass resourceClass,
        JobNodeImportance importance,
        bool blocksAutoIdentify) {
        var definition = JobDefinitionRegistry.Get(type);
        Assert.Equal(resourceClass, definition.ResourceClass);
        Assert.Equal(importance, definition.Importance);
        Assert.Equal(blocksAutoIdentify, definition.BlocksAutoIdentify);
    }

    [JobDefinition(JobType.Noop)]
    private sealed class DuplicateNoopHandler : IJobHandler {
        public Task HandleAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [JobDefinition(JobType.Noop)]
    private sealed class SecondDuplicateNoopHandler : IJobHandler {
        public Task HandleAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class UndeclaredHandler : IJobHandler {
        public Task HandleAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
