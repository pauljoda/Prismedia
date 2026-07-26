using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Jobs;

public sealed class AdaptiveJobCapacityTests {
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(8, 4)]
    [InlineData(64, 4)]
    public void InteractiveLaneLimitUsesHalfTheLogicalProcessorsWithACap(
        int logicalProcessors,
        int expected) {
        Assert.Equal(expected, AdaptiveJobCapacity.InteractiveLaneLimit(logicalProcessors));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(4, 3)]
    [InlineData(8, 7)]
    public void CpuPermitBudgetLeavesOneLogicalProcessorFree(int logicalProcessors, int expected) {
        Assert.Equal(expected, AdaptiveJobCapacity.CpuPermitBudget(logicalProcessors));
    }

    [Theory]
    [InlineData(JobResourceClass.Light, 1, 0)]
    [InlineData(JobResourceClass.StandardCpu, 1, 1)]
    [InlineData(JobResourceClass.HeavyCpu, 1, 1)]
    [InlineData(JobResourceClass.HeavyCpu, 7, 2)]
    public void ResourceCostMatchesTheDeclaredProfile(
        JobResourceClass resourceClass,
        int totalPermits,
        int expected) {
        Assert.Equal(expected, AdaptiveJobCapacity.CpuCost(resourceClass, totalPermits));
    }
}
