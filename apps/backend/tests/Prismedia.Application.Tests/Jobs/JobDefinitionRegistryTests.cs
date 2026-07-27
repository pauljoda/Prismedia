using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Jobs;

public sealed class JobDefinitionRegistryTests {
    [Fact]
    public void SubtitleExtractionIsBestEffortHeavyCpuWork() {
        Assert.Equal(
            JobNodeImportance.BestEffort,
            JobDefinitionRegistry.Importance(JobType.ExtractSubtitles));
        Assert.Equal(
            JobResourceClass.HeavyCpu,
            JobDefinitionRegistry.ResourceClass(JobType.ExtractSubtitles));
    }
}
