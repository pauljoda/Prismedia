using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Tests;

public sealed class ProwlarrSearchConcurrencyGateTests {
    [Fact]
    public async Task ThirdAggregateSearchWaitsForOneOfTwoSlots() {
        var gate = new ProwlarrSearchConcurrencyGate();
        using var first = await gate.EnterAsync(CancellationToken.None);
        using var second = await gate.EnterAsync(CancellationToken.None);

        var third = gate.EnterAsync(CancellationToken.None).AsTask();
        Assert.False(third.IsCompleted);

        first.Dispose();
        using var thirdLease = await third.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(third.IsCompletedSuccessfully);
    }
}
